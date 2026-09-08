using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;

namespace MachiVerse.Simulation.Core.Runtime;

public sealed record OperationSchedulingPolicyHistoryEntryV1(
    OperationSchedulingPolicyV1 Policy,
    ulong EffectiveFromStepInclusive,
    ulong? EffectiveUntilStepExclusive)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Policy);
        if (EffectiveUntilStepExclusive is { } until && until <= EffectiveFromStepInclusive)
            throw new InvalidDataException("operation.scheduling-policy-history-range-invalid");
    }
}

public interface IHistoricalOperationSchedulingPolicyResolverV1
{
    OperationSchedulingPolicyV1 Resolve(ulong admissionBasisStep, ulong schedulingPolicyGeneration);
}

/// <summary>
/// Immutable historical scheduling policy view. The caller may build this from persisted Config
/// history; the coordinator never substitutes the current Config for the admission generation.
/// </summary>
public sealed class OperationSchedulingPolicyHistoryV1 : IHistoricalOperationSchedulingPolicyResolverV1
{
    private readonly IReadOnlyDictionary<ulong, OperationSchedulingPolicyHistoryEntryV1> _byGeneration;

    public OperationSchedulingPolicyHistoryV1(IEnumerable<OperationSchedulingPolicyHistoryEntryV1> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var ordered = entries
            .OrderBy(static entry => entry.Policy.OwnerConfigGeneration)
            .ToArray();
        if (ordered.Length == 0)
            throw new ArgumentException("Scheduling policy history cannot be empty.", nameof(entries));

        var byGeneration = new Dictionary<ulong, OperationSchedulingPolicyHistoryEntryV1>();
        foreach (var entry in ordered)
        {
            entry.Validate();
            if (!byGeneration.TryAdd(entry.Policy.OwnerConfigGeneration, entry))
                throw new InvalidDataException("operation.scheduling-policy-history-duplicate-generation");
        }
        _byGeneration = byGeneration;
    }

    public OperationSchedulingPolicyV1 Resolve(ulong admissionBasisStep, ulong schedulingPolicyGeneration)
    {
        if (schedulingPolicyGeneration == 0)
            throw new InvalidDataException("operation.scheduling-policy-generation-invalid");
        if (!_byGeneration.TryGetValue(schedulingPolicyGeneration, out var entry))
            throw new InvalidDataException("operation.scheduling-policy-generation-unknown");
        if (admissionBasisStep < entry.EffectiveFromStepInclusive ||
            entry.EffectiveUntilStepExclusive is { } until && admissionBasisStep >= until)
            throw new InvalidDataException("operation.scheduling-policy-not-effective-at-admission");
        return entry.Policy;
    }
}

public sealed record OperationDurableObservationV1(
    OperationLifecycleStateV1 Lifecycle,
    bool Duplicate,
    ulong? AcceptedSequence,
    ulong? ScheduledSequence,
    ulong? EffectiveStep,
    ulong? TerminalSequence,
    CoreOperationResultStatusV1? TerminalStatus,
    StableToken? ResultCode);

/// <summary>
/// Orchestrates the durable Operation custody boundaries without owning protocol serialization.
/// HistoryRecordMaterial remains supplied by the schema-owned caller. Every observable lifecycle
/// returned by this coordinator is read after the corresponding persistence transaction commits.
/// </summary>
public sealed class DurableOperationCoordinatorV1
{
    private readonly SqlitePersistenceStore _store;
    private readonly IHistoricalOperationSchedulingPolicyResolverV1 _policyHistory;

    public DurableOperationCoordinatorV1(
        SqlitePersistenceStore store,
        IHistoricalOperationSchedulingPolicyResolverV1 policyHistory)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _policyHistory = policyHistory ?? throw new ArgumentNullException(nameof(policyHistory));
    }

    public OperationSchedulingDecisionV1 Plan(
        OperationSchedulingAdmissionV1 admission,
        OperationSchedulingBarrierV1 barrier,
        ulong? reportedCandidateStep = null)
    {
        ArgumentNullException.ThrowIfNull(admission);
        var policy = _policyHistory.Resolve(
            admission.AdmissionBasisStep,
            admission.SchedulingPolicyGeneration);
        return OperationSchedulingPlannerV1.Plan(policy, admission, barrier, reportedCandidateStep);
    }

    public async Task<OperationDurableObservationV1?> ObserveAsync(
        OpaqueId128 operationId,
        byte[] operationPayloadDigest,
        CancellationToken cancellationToken = default)
    {
        RequireDigest(operationPayloadDigest);
        var state = await _store.ReadOperationStateAsync(operationId, cancellationToken);
        return state is null ? null : ObserveExisting(state, operationPayloadDigest, duplicate: true);
    }

    public async Task<OperationDurableObservationV1> AcceptOrConvergeAsync(
        OpaqueId128 operationId,
        byte[] operationPayloadDigest,
        HistoryRecordMaterial acceptedHistory,
        CancellationToken cancellationToken = default)
    {
        RequireDigest(operationPayloadDigest);
        var existing = await _store.ReadOperationStateAsync(operationId, cancellationToken);
        if (existing is not null)
            return ObserveExisting(existing, operationPayloadDigest, duplicate: true);

        var result = await _store.PersistAcceptedOperationAsync(
            operationId,
            operationPayloadDigest,
            acceptedHistory,
            cancellationToken);
        var durable = await RequireStateAsync(operationId, cancellationToken);
        return ObserveExisting(
            durable,
            operationPayloadDigest,
            duplicate: result.Status == DurableAcceptanceStatus.Duplicate);
    }

    public async Task<OperationDurableObservationV1> ScheduleOrConvergeAsync(
        OpaqueId128 operationId,
        byte[] operationPayloadDigest,
        OperationSchedulingDecisionV1 decision,
        SameStepOrderKey orderKey,
        HistoryRecordMaterial scheduledHistory,
        CancellationToken cancellationToken = default)
    {
        RequireDigest(operationPayloadDigest);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(orderKey);
        if (decision.Kind != OperationSchedulingDecisionKindV1.Scheduled || decision.EffectiveStep is null)
            throw new InvalidOperationException("Only a scheduled decision can cross the durable scheduling boundary.");

        var existing = await RequireStateAsync(operationId, cancellationToken);
        var observation = ObserveExisting(existing, operationPayloadDigest, duplicate: true);
        if (observation.Lifecycle is OperationLifecycleStateV1.ScheduledDurable or OperationLifecycleStateV1.TerminalDurable)
            return observation;
        if (observation.Lifecycle != OperationLifecycleStateV1.AcceptedDurable)
            throw new InvalidDataException("persistence.operation-invalid-lifecycle-for-schedule");

        var result = await _store.PersistScheduledOperationAsync(
            operationId,
            decision.EffectiveStep.Value,
            orderKey,
            scheduledHistory,
            cancellationToken);
        var durable = await RequireStateAsync(operationId, cancellationToken);
        return ObserveExisting(
            durable,
            operationPayloadDigest,
            duplicate: result.Status == DurableSchedulingStatus.Duplicate);
    }

    public async Task<OperationDurableObservationV1> RejectUnseenOrConvergeAsync(
        OpaqueId128 operationId,
        byte[] operationPayloadDigest,
        StableToken resultCode,
        HistoryRecordMaterial terminalHistory,
        byte[]? richResultPayload = null,
        CancellationToken cancellationToken = default)
    {
        RequireDigest(operationPayloadDigest);
        var existing = await _store.ReadOperationStateAsync(operationId, cancellationToken);
        if (existing is not null)
            return ObserveExisting(existing, operationPayloadDigest, duplicate: true);

        var result = await _store.PersistRejectedUnseenOperationAsync(
            operationId,
            operationPayloadDigest,
            (int)CoreOperationResultStatusV1.Rejected,
            resultCode.Value,
            terminalHistory,
            richResultPayload,
            cancellationToken);
        return ObserveExisting(
            result.State,
            operationPayloadDigest,
            duplicate: result.Status == DirectTerminalPersistenceStatusV1.Duplicate);
    }

    private async Task<DurableOperationStateV1> RequireStateAsync(
        OpaqueId128 operationId,
        CancellationToken cancellationToken)
        => await _store.ReadOperationStateAsync(operationId, cancellationToken)
            ?? throw new InvalidDataException("persistence.operation-state-missing-after-commit");

    private static OperationDurableObservationV1 ObserveExisting(
        DurableOperationStateV1 state,
        byte[] expectedDigest,
        bool duplicate)
    {
        if (!CryptographicOperations.FixedTimeEquals(state.OperationPayloadDigest, expectedDigest))
            throw new InvalidDataException("protocol.operation-payload-mismatch");

        var lifecycle = state.Lifecycle switch
        {
            DurableOperationLifecycleV1.AcceptedDurable => OperationLifecycleStateV1.AcceptedDurable,
            DurableOperationLifecycleV1.ScheduledDurable => OperationLifecycleStateV1.ScheduledDurable,
            DurableOperationLifecycleV1.TerminalDurable => OperationLifecycleStateV1.TerminalDurable,
            _ => throw new InvalidDataException("persistence.operation-lifecycle-invalid"),
        };

        CoreOperationResultStatusV1? terminalStatus = null;
        StableToken? resultCode = null;
        if (lifecycle == OperationLifecycleStateV1.TerminalDurable)
        {
            if (state.TerminalStatus is not { } raw ||
                !Enum.IsDefined(typeof(CoreOperationResultStatusV1), raw) ||
                !OperationLifecycleRulesV1.IsTerminalResult((CoreOperationResultStatusV1)raw) ||
                state.ResultCode is null)
                throw new InvalidDataException("persistence.operation-terminal-state-incomplete");
            terminalStatus = (CoreOperationResultStatusV1)raw;
            resultCode = new StableToken(state.ResultCode);
        }

        return new OperationDurableObservationV1(
            lifecycle,
            duplicate,
            state.AcceptedSequence,
            state.ScheduledSequence,
            state.EffectiveStep,
            state.TerminalSequence,
            terminalStatus,
            resultCode);
    }

    private static void RequireDigest(byte[] digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (digest.Length != 32)
            throw new ArgumentException("Operation payload digest must be exactly 32 bytes.", nameof(digest));
    }
}
