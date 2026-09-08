using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Runtime;

public enum StepCoreSubstateKindV1 : byte
{
    Scheduler = 1,
    Operation = 2,
    Detail = 3,
    DomainRegistry = 4,
}

public static class StepCoreSubstateRegistryV1
{
    public static string SchemaId(StepCoreSubstateKindV1 kind) => kind switch
    {
        StepCoreSubstateKindV1.Scheduler => "core.scheduler-state",
        StepCoreSubstateKindV1.Operation => "core.operation-state",
        StepCoreSubstateKindV1.Detail => "core.detail-state",
        StepCoreSubstateKindV1.DomainRegistry => "core.domain-registry-state",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static WorldSubstateRefV1 Get(WorldStateV1 state, StepCoreSubstateKindV1 kind)
    {
        ArgumentNullException.ThrowIfNull(state);
        return kind switch
        {
            StepCoreSubstateKindV1.Scheduler => state.SchedulerState,
            StepCoreSubstateKindV1.Operation => state.OperationState,
            StepCoreSubstateKindV1.Detail => state.DetailState,
            StepCoreSubstateKindV1.DomainRegistry => state.DomainRegistryState,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}

/// <summary>
/// StepCandidate-owned binding for a core WorldState substate. Basis and result digests are bound
/// into the candidate diagnostic so State(S+1) cannot substitute an arbitrary scheduler/operation
/// state after domain calculation has completed.
/// </summary>
public sealed class StepCoreSubstateCandidateV1
{
    public StepCoreSubstateCandidateV1(
        StepCoreSubstateKindV1 kind,
        ulong basisStep,
        WorldSubstateRefV1 basisState,
        WorldSubstateRefV1 resultingState)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (basisStep == ulong.MaxValue) throw new InvalidDataException("step-core-substate.step-overflow");
        ArgumentNullException.ThrowIfNull(basisState);
        ArgumentNullException.ThrowIfNull(resultingState);
        basisState.Validate("step-core-substate-basis");
        resultingState.Validate("step-core-substate-result");

        var schemaId = StepCoreSubstateRegistryV1.SchemaId(kind);
        if (!string.Equals(basisState.Schema.SchemaId.Value, schemaId, StringComparison.Ordinal) ||
            !string.Equals(resultingState.Schema.SchemaId.Value, schemaId, StringComparison.Ordinal) ||
            basisState.Schema != resultingState.Schema)
            throw new InvalidDataException("step-core-substate.schema-mismatch");

        Kind = kind;
        BasisStep = basisStep;
        TargetStep = basisStep + 1;
        BasisState = new WorldSubstateRefV1(basisState.Schema, basisState.CanonicalDigest.ToArray());
        ResultingState = new WorldSubstateRefV1(resultingState.Schema, resultingState.CanonicalDigest.ToArray());
        CandidateDigest = HashSuite.DomainHash("mv.step-core-substate-candidate.v1", writer =>
        {
            writer.WriteMapStart(7);
            writer.WriteUnsigned(0); writer.WriteUnsigned((byte)Kind);
            writer.WriteUnsigned(1); writer.WriteAsciiText(BasisState.Schema.SchemaId.Value);
            writer.WriteUnsigned(2); writer.WriteUnsigned(BasisStep);
            writer.WriteUnsigned(3); writer.WriteUnsigned(TargetStep);
            writer.WriteUnsigned(4); writer.WriteBytes(BasisState.CanonicalDigest);
            writer.WriteUnsigned(5); writer.WriteBytes(ResultingState.CanonicalDigest);
            writer.WriteUnsigned(6); writer.WriteAsciiText("core-substate");
        });
    }

    public StepCoreSubstateKindV1 Kind { get; }
    public ulong BasisStep { get; }
    public ulong TargetStep { get; }
    public WorldSubstateRefV1 BasisState { get; }
    public WorldSubstateRefV1 ResultingState { get; }
    public byte[] CandidateDigest { get; }

    internal void ValidateBasis(WorldStateV1 state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (BasisStep != state.Header.Step || TargetStep != checked(state.Header.Step + 1))
            throw new InvalidDataException("step-core-substate.basis-step-mismatch");
        var actual = StepCoreSubstateRegistryV1.Get(state, Kind);
        if (actual.Schema != BasisState.Schema ||
            !CryptographicOperations.FixedTimeEquals(actual.CanonicalDigest, BasisState.CanonicalDigest))
            throw new InvalidDataException("step-core-substate.basis-state-mismatch");
    }
}

public sealed class StepCoreSubstateStateMaterialV1
{
    public StepCoreSubstateStateMaterialV1(
        StepCoreSubstateKindV1 kind,
        WorldSubstateRefV1 resultingState)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(resultingState);
        resultingState.Validate("step-core-substate-material");
        var schemaId = StepCoreSubstateRegistryV1.SchemaId(kind);
        if (!string.Equals(resultingState.Schema.SchemaId.Value, schemaId, StringComparison.Ordinal))
            throw new InvalidDataException("step-core-substate.material-schema-mismatch");
        Kind = kind;
        ResultingState = new WorldSubstateRefV1(resultingState.Schema, resultingState.CanonicalDigest.ToArray());
    }

    public StepCoreSubstateKindV1 Kind { get; }
    public WorldSubstateRefV1 ResultingState { get; }

    internal void ValidateFor(StepCoreSubstateCandidateV1 candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (Kind != candidate.Kind || ResultingState.Schema != candidate.ResultingState.Schema)
            throw new InvalidDataException("step-core-substate.material-kind-mismatch");
        if (!CryptographicOperations.FixedTimeEquals(
                ResultingState.CanonicalDigest,
                candidate.ResultingState.CanonicalDigest))
            throw new InvalidDataException("step-core-substate.material-digest-mismatch");
    }
}

public static class OperationSchedulerSubstateV1
{
    private static readonly SchemaRefV1 Schema = new("core.scheduler-state");

    public static WorldSubstateRefV1 Canonicalize(OperationSchedulerStateV1 scheduler, ulong worldStep)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        var buckets = scheduler.CanonicalBuckets.ToArray();
        if (scheduler.FreezeStep is null && scheduler.NextSchedulableStep == worldStep && buckets.Length == 0)
            return WorldStateV1.EmptySubstate(Schema.SchemaId.Value);

        if (scheduler.FreezeStep is { } freeze && freeze >= scheduler.NextSchedulableStep)
            throw new InvalidDataException("scheduler-substate.freeze-barrier-invalid");

        var digest = HashSuite.DomainHash("mv.core-scheduler-state.v1", writer =>
        {
            writer.WriteMapStart(5);
            writer.WriteUnsigned(0); writer.WriteAsciiText(Schema.SchemaId.Value);
            writer.WriteUnsigned(1); writer.WriteUnsigned(worldStep);
            writer.WriteUnsigned(2); writer.WriteUnsigned(scheduler.NextSchedulableStep);
            writer.WriteUnsigned(3);
            if (scheduler.FreezeStep is { } frozen)
            {
                writer.WriteArrayStart(1);
                writer.WriteUnsigned(frozen);
            }
            else writer.WriteArrayStart(0);
            writer.WriteUnsigned(4);
            writer.WriteArrayStart((ulong)buckets.Length);
            foreach (var bucket in buckets)
            {
                writer.WriteArrayStart(2);
                writer.WriteUnsigned(bucket.Key);
                writer.WriteArrayStart((ulong)bucket.Value.Count);
                foreach (var operation in bucket.Value)
                {
                    operation.Validate();
                    if (operation.EffectiveStep != bucket.Key)
                        throw new InvalidDataException("scheduler-substate.bucket-effective-step-mismatch");
                    writer.WriteArrayStart(2);
                    writer.WriteBytes(operation.OperationId.ToBytes());
                    writer.WriteBytes(operation.OrderKey.ToDatabaseBytes());
                }
            }
        });
        return new WorldSubstateRefV1(Schema, digest);
    }

    public static OperationSchedulerStateV1 ProjectAfterFinalization(
        OperationSchedulerStateV1 scheduler,
        FrozenStepInputV1 frozenInput)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(frozenInput);
        if (scheduler.FreezeStep != frozenInput.BasisStep ||
            scheduler.NextSchedulableStep != frozenInput.BasisStep + 1)
            throw new InvalidDataException("scheduler-substate.frozen-barrier-mismatch");

        var current = scheduler.ForEffectiveStep(frozenInput.BasisStep);
        if (current.Count != frozenInput.ScheduledOperations.Count)
            throw new InvalidDataException("scheduler-substate.frozen-set-mismatch");
        for (var index = 0; index < current.Count; index++)
        {
            if (current[index].OperationId != frozenInput.ScheduledOperations[index].OperationId ||
                !current[index].OrderKey.ToDatabaseBytes().AsSpan()
                    .SequenceEqual(frozenInput.ScheduledOperations[index].OrderKey.ToDatabaseBytes()))
                throw new InvalidDataException("scheduler-substate.frozen-set-mismatch");
        }

        var future = scheduler.CanonicalBuckets
            .Where(pair => pair.Key > frozenInput.BasisStep)
            .SelectMany(static pair => pair.Value)
            .ToArray();
        return new OperationSchedulerStateV1(
            checked(frozenInput.BasisStep + 1),
            freezeStep: null,
            future);
    }

    public static StepCoreSubstateCandidateV1 CreatePostFinalizationCandidate(
        WorldStateV1 basisState,
        OperationSchedulerStateV1 frozenScheduler,
        FrozenStepInputV1 frozenInput)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        var projected = ProjectAfterFinalization(frozenScheduler, frozenInput);
        var resulting = Canonicalize(projected, frozenInput.BasisStep + 1);
        return new StepCoreSubstateCandidateV1(
            StepCoreSubstateKindV1.Scheduler,
            frozenInput.BasisStep,
            basisState.SchedulerState,
            resulting);
    }
}

public static class DurableOperationSubstateV1
{
    private static readonly SchemaRefV1 Schema = new("core.operation-state");

    public static WorldSubstateRefV1 Canonicalize(IEnumerable<DurableOperationStateV1> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        var ordered = states
            .Select(static state => state ?? throw new ArgumentNullException(nameof(states)))
            .OrderBy(static state => state.OperationId)
            .ToArray();
        if (ordered.Select(static state => state.OperationId).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("operation-substate.duplicate-operation-id");
        if (ordered.Length == 0)
            return WorldStateV1.EmptySubstate(Schema.SchemaId.Value);
        foreach (var state in ordered) Validate(state);

        var digest = HashSuite.DomainHash("mv.core-operation-state.v1", writer =>
        {
            writer.WriteMapStart(2);
            writer.WriteUnsigned(0); writer.WriteAsciiText(Schema.SchemaId.Value);
            writer.WriteUnsigned(1);
            writer.WriteArrayStart((ulong)ordered.Length);
            foreach (var state in ordered)
            {
                writer.WriteMapStart(10);
                writer.WriteUnsigned(0); writer.WriteBytes(state.OperationId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(state.OperationPayloadDigest);
                writer.WriteUnsigned(2); writer.WriteUnsigned((byte)state.Lifecycle);
                WriteOptional(writer, 3, state.AcceptedSequence);
                WriteOptional(writer, 4, state.ScheduledSequence);
                WriteOptional(writer, 5, state.EffectiveStep);
                WriteOptional(writer, 6, state.TerminalSequence);
                writer.WriteUnsigned(7);
                if (state.TerminalStatus is { } status)
                {
                    writer.WriteArrayStart(1); writer.WriteSigned(status);
                }
                else writer.WriteArrayStart(0);
                writer.WriteUnsigned(8);
                if (state.ResultCode is { } code)
                {
                    writer.WriteArrayStart(1); writer.WriteAsciiText(code);
                }
                else writer.WriteArrayStart(0);
                writer.WriteUnsigned(9);
                if (state.RichResultPayload is { } rich)
                {
                    writer.WriteArrayStart(1); writer.WriteBytes(rich);
                }
                else writer.WriteArrayStart(0);
            }
        });
        return new WorldSubstateRefV1(Schema, digest);
    }

    public static IReadOnlyList<DurableOperationStateV1> ProjectTerminalCommit(
        IEnumerable<DurableOperationStateV1> basisStates,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        ulong effectiveStep,
        ulong terminalHistorySequence)
    {
        ArgumentNullException.ThrowIfNull(basisStates);
        ArgumentNullException.ThrowIfNull(terminalOperations);
        if (terminalHistorySequence == 0) throw new ArgumentOutOfRangeException(nameof(terminalHistorySequence));

        var byId = basisStates.ToDictionary(static state => state.OperationId);
        foreach (var terminal in terminalOperations.OrderBy(static terminal => terminal.OperationId))
        {
            if (!byId.TryGetValue(terminal.OperationId, out var existing))
                throw new InvalidDataException("operation-substate.terminal-operation-missing");
            if (existing.Lifecycle != DurableOperationLifecycleV1.ScheduledDurable ||
                existing.EffectiveStep != effectiveStep)
                throw new InvalidDataException("operation-substate.terminal-operation-not-scheduled-for-step");
            if (!Enum.IsDefined(typeof(CoreOperationResultStatusV1), terminal.TerminalStatus) ||
                !OperationLifecycleRulesV1.IsTerminalResult((CoreOperationResultStatusV1)terminal.TerminalStatus))
                throw new InvalidDataException("operation-substate.terminal-status-invalid");
            _ = new StableToken(terminal.ResultCode);

            byId[terminal.OperationId] = existing with
            {
                Lifecycle = DurableOperationLifecycleV1.TerminalDurable,
                TerminalSequence = terminalHistorySequence,
                TerminalStatus = terminal.TerminalStatus,
                ResultCode = terminal.ResultCode,
                RichResultPayload = terminal.RichResultPayload?.ToArray(),
            };
        }
        var projected = byId.Values.OrderBy(static state => state.OperationId).ToArray();
        foreach (var state in projected) Validate(state);
        return Array.AsReadOnly(projected);
    }

    public static StepCoreSubstateCandidateV1 CreatePostTransitionCandidate(
        WorldStateV1 basisState,
        IEnumerable<DurableOperationStateV1> durableBeforeTransition,
        IReadOnlyCollection<TerminalOperationCommit> terminalOperations,
        ulong effectiveStep,
        ulong terminalHistorySequence)
    {
        ArgumentNullException.ThrowIfNull(basisState);
        if (effectiveStep != basisState.Header.Step)
            throw new InvalidDataException("operation-substate.effective-step-mismatch");
        var projected = ProjectTerminalCommit(
            durableBeforeTransition,
            terminalOperations,
            effectiveStep,
            terminalHistorySequence);
        return new StepCoreSubstateCandidateV1(
            StepCoreSubstateKindV1.Operation,
            effectiveStep,
            basisState.OperationState,
            Canonicalize(projected));
    }

    private static void Validate(DurableOperationStateV1 state)
    {
        if (state.OperationId.IsZero)
            throw new InvalidDataException("operation-substate.operation-id-zero");
        if (state.OperationPayloadDigest is null || state.OperationPayloadDigest.Length != 32)
            throw new InvalidDataException("operation-substate.payload-digest-invalid");
        switch (state.Lifecycle)
        {
            case DurableOperationLifecycleV1.AcceptedDurable:
                if (state.AcceptedSequence is null || state.ScheduledSequence is not null ||
                    state.EffectiveStep is not null || state.TerminalSequence is not null ||
                    state.TerminalStatus is not null || state.ResultCode is not null)
                    throw new InvalidDataException("operation-substate.accepted-shape-invalid");
                break;
            case DurableOperationLifecycleV1.ScheduledDurable:
                if (state.AcceptedSequence is null || state.ScheduledSequence is null ||
                    state.EffectiveStep is null || state.TerminalSequence is not null ||
                    state.TerminalStatus is not null || state.ResultCode is not null)
                    throw new InvalidDataException("operation-substate.scheduled-shape-invalid");
                break;
            case DurableOperationLifecycleV1.TerminalDurable:
                if (state.TerminalSequence is null || state.TerminalStatus is null || state.ResultCode is null)
                    throw new InvalidDataException("operation-substate.terminal-shape-invalid");
                _ = new StableToken(state.ResultCode);
                if (!Enum.IsDefined(typeof(CoreOperationResultStatusV1), state.TerminalStatus.Value) ||
                    !OperationLifecycleRulesV1.IsTerminalResult((CoreOperationResultStatusV1)state.TerminalStatus.Value))
                    throw new InvalidDataException("operation-substate.terminal-status-invalid");
                var directTerminal = state.AcceptedSequence is null && state.ScheduledSequence is null && state.EffectiveStep is null;
                var scheduledTerminal = state.AcceptedSequence is not null && state.ScheduledSequence is not null && state.EffectiveStep is not null;
                if (!directTerminal && !scheduledTerminal)
                    throw new InvalidDataException("operation-substate.terminal-provenance-invalid");
                break;
            default:
                throw new InvalidDataException("operation-substate.lifecycle-invalid");
        }
    }

    private static void WriteOptional(MvDcborWriter writer, ulong key, ulong? value)
    {
        writer.WriteUnsigned(key);
        if (value is { } present)
        {
            writer.WriteArrayStart(1); writer.WriteUnsigned(present);
        }
        else writer.WriteArrayStart(0);
    }
}
