using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Runtime;

public sealed class FrozenStepInputV1
{
    public FrozenStepInputV1(
        OpaqueId128 worldId,
        ulong basisStep,
        IEnumerable<ScheduledOperationRefV1> scheduledOperations)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        ArgumentNullException.ThrowIfNull(scheduledOperations);

        var ordered = scheduledOperations
            .OrderBy(static item => item.OrderKey)
            .ThenBy(static item => item.OperationId)
            .ToArray();
        if (ordered.Select(static item => item.OperationId).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("step-input.duplicate-operation");
        foreach (var item in ordered)
        {
            item.Validate();
            if (item.EffectiveStep != basisStep)
                throw new InvalidDataException("step-input.operation-effective-step-mismatch");
        }

        WorldId = worldId;
        BasisStep = basisStep;
        ScheduledOperations = Array.AsReadOnly(ordered);
    }

    public OpaqueId128 WorldId { get; }
    public ulong BasisStep { get; }
    public IReadOnlyList<ScheduledOperationRefV1> ScheduledOperations { get; }
}

public static class StepInputFreezerV1
{
    public static FrozenStepInputV1 Freeze(WorldStateV1 state, OperationSchedulerStateV1 scheduler)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scheduler);

        var step = state.Header.Step;
        if (scheduler.FreezeStep is not null)
            throw new InvalidDataException("step-input.already-frozen");
        if (scheduler.NextSchedulableStep != step)
            throw new InvalidDataException("step-input.scheduler-barrier-mismatch");

        var scheduled = scheduler.ForEffectiveStep(step);
        scheduler.FreezeExternalInput(step);
        return new FrozenStepInputV1(state.Header.WorldId, step, scheduled);
    }
}

public sealed record DomainExecutionPlanEntryV1(
    StableToken DomainToken,
    ushort DomainRank,
    IReadOnlyList<StableToken> OwnedPartitions);

public sealed class StandardDomainExecutionPlanV1
{
    private StandardDomainExecutionPlanV1(IReadOnlyList<DomainExecutionPlanEntryV1> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<DomainExecutionPlanEntryV1> Entries { get; }

    public static StandardDomainExecutionPlanV1 Create()
    {
        var entries = StandardDomainPartitionRegistry.Entries
            .GroupBy(static partition => partition.OwnerDomain)
            .Select(static group =>
            {
                var first = group.First();
                var partitions = group
                    .Select(static item => item.PartitionId)
                    .OrderBy(static token => token.Value, StringComparer.Ordinal)
                    .ToArray();
                if (group.Any(item => item.OwnerDomainRank != first.OwnerDomainRank))
                    throw new InvalidDataException("step-plan.domain-rank-mismatch");
                return new DomainExecutionPlanEntryV1(
                    first.OwnerDomain,
                    first.OwnerDomainRank,
                    Array.AsReadOnly(partitions));
            })
            .OrderBy(static entry => entry.DomainRank)
            .ThenBy(static entry => entry.DomainToken.Value, StringComparer.Ordinal)
            .ToArray();

        if (entries.Length != 8)
            throw new InvalidDataException("step-plan.standard-domain-count-mismatch");
        if (entries.Sum(static entry => entry.OwnedPartitions.Count) != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("step-plan.standard-partition-count-mismatch");

        return new StandardDomainExecutionPlanV1(Array.AsReadOnly(entries));
    }
}

public sealed class DomainRuntimeContextV1
{
    public DomainRuntimeContextV1(
        WorldStateV1 state,
        FrozenStepInputV1 frozenInput,
        DomainExecutionPlanEntryV1 planEntry)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(frozenInput);
        ArgumentNullException.ThrowIfNull(planEntry);
        if (state.Header.WorldId != frozenInput.WorldId || state.Header.Step != frozenInput.BasisStep)
            throw new InvalidDataException("domain-runtime.frozen-input-basis-mismatch");

        State = state;
        FrozenInput = frozenInput;
        PlanEntry = planEntry;
    }

    public WorldStateV1 State { get; }
    public FrozenStepInputV1 FrozenInput { get; }
    public DomainExecutionPlanEntryV1 PlanEntry { get; }
}

public sealed class DomainCandidateOutputV1
{
    public DomainCandidateOutputV1(
        StableToken domainToken,
        ulong basisStep,
        IEnumerable<MutationIntentCandidateV1>? intents = null)
    {
        var ordered = (intents ?? Array.Empty<MutationIntentCandidateV1>())
            .OrderBy(static intent => intent.OrderKey)
            .ToArray();
        if (ordered.Select(static intent => intent.IntentId).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("domain-output.duplicate-intent-id");
        if (ordered.Any(intent => intent.SourceDomain != domainToken || intent.BasisStep != basisStep))
            throw new InvalidDataException("domain-output.intent-source-mismatch");

        DomainToken = domainToken;
        BasisStep = basisStep;
        Intents = Array.AsReadOnly(ordered);
    }

    public StableToken DomainToken { get; }
    public ulong BasisStep { get; }
    public IReadOnlyList<MutationIntentCandidateV1> Intents { get; }
}

public interface IDomainRuntimeV1
{
    StableToken DomainToken { get; }
    ValueTask<DomainCandidateOutputV1> ExecuteAsync(DomainRuntimeContextV1 context, CancellationToken cancellationToken);
}

public static class DomainRuntimeExecutorV1
{
    public static async Task<IReadOnlyList<DomainCandidateOutputV1>> ExecuteAsync(
        StandardDomainExecutionPlanV1 plan,
        WorldStateV1 state,
        FrozenStepInputV1 frozenInput,
        IReadOnlyCollection<IDomainRuntimeV1> runtimes,
        int workerCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(frozenInput);
        ArgumentNullException.ThrowIfNull(runtimes);

        var byDomain = new Dictionary<StableToken, IDomainRuntimeV1>();
        foreach (var runtime in runtimes)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            if (!byDomain.TryAdd(runtime.DomainToken, runtime))
                throw new InvalidDataException("domain-runtime.duplicate-domain");
        }
        if (byDomain.Count != plan.Entries.Count || plan.Entries.Any(entry => !byDomain.ContainsKey(entry.DomainToken)))
            throw new InvalidDataException("domain-runtime.plan-coverage-mismatch");

        var outputs = await DeterministicBatchExecutor.RunAsync(
            plan.Entries,
            workerCount,
            async (entry, ct) =>
            {
                var runtime = byDomain[entry.DomainToken];
                var output = await runtime.ExecuteAsync(new DomainRuntimeContextV1(state, frozenInput, entry), ct);
                if (output.DomainToken != entry.DomainToken || output.BasisStep != frozenInput.BasisStep)
                    throw new InvalidDataException("domain-runtime.output-basis-mismatch");
                return output;
            },
            cancellationToken);

        return outputs;
    }
}

public enum CausalityRefKindV1
{
    Operation = 0,
    Event = 1,
    Intent = 2,
    Entity = 3,
    Transaction = 4,
    ConfigGeneration = 5,
    HistoryRecord = 6,
    PartitionRevision = 7,
}

public sealed class CausalityRefV1
{
    public CausalityRefV1(CausalityRefKindV1 kind, ReadOnlySpan<byte> id, ulong? basisStep = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (id.IsEmpty) throw new ArgumentException("CausalityRef id cannot be empty.", nameof(id));
        Kind = kind;
        Id = id.ToArray();
        BasisStep = basisStep;
    }

    public CausalityRefKindV1 Kind { get; }
    public byte[] Id { get; }
    public ulong? BasisStep { get; }
}

public enum InvariantSeverityV1
{
    Diagnostic = 0,
    CommitBlocking = 1,
    FatalAuthority = 2,
}

public enum InvariantOutcomeV1
{
    Pass = 0,
    Fail = 1,
}

public sealed class InvariantResultV1
{
    public InvariantResultV1(
        StableToken invariantId,
        InvariantSeverityV1 severity,
        InvariantOutcomeV1 outcome,
        IEnumerable<CausalityRefV1>? participantRefs = null,
        StableToken? diagnosticCode = null)
    {
        if (!Enum.IsDefined(severity)) throw new ArgumentOutOfRangeException(nameof(severity));
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        var ordered = (participantRefs ?? Array.Empty<CausalityRefV1>())
            .OrderBy(static item => item.Kind)
            .ThenBy(static item => Convert.ToHexString(item.Id), StringComparer.Ordinal)
            .ThenBy(static item => item.BasisStep)
            .ToArray();

        InvariantId = invariantId;
        Severity = severity;
        Outcome = outcome;
        ParticipantRefs = Array.AsReadOnly(ordered);
        DiagnosticCode = diagnosticCode;
    }

    public StableToken InvariantId { get; }
    public InvariantSeverityV1 Severity { get; }
    public InvariantOutcomeV1 Outcome { get; }
    public IReadOnlyList<CausalityRefV1> ParticipantRefs { get; }
    public StableToken? DiagnosticCode { get; }
}

public sealed record InvariantBarrierDecisionV1(
    bool CanCommit,
    bool FatalAuthorityFailure,
    IReadOnlyList<StableToken> FailedCommitBlocking,
    IReadOnlyList<StableToken> FailedFatal);

public static class InvariantBarrierV1
{
    public static InvariantBarrierDecisionV1 Evaluate(IEnumerable<InvariantResultV1> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var ordered = results
            .OrderBy(static result => result.InvariantId.Value, StringComparer.Ordinal)
            .ThenBy(static result => result.Severity)
            .ToArray();
        if (ordered.GroupBy(static result => (result.InvariantId, result.Severity)).Any(static group => group.Count() != 1))
            throw new InvalidDataException("step-candidate.duplicate-invariant-result");

        var blocking = ordered
            .Where(static result => result.Outcome == InvariantOutcomeV1.Fail && result.Severity == InvariantSeverityV1.CommitBlocking)
            .Select(static result => result.InvariantId)
            .ToArray();
        var fatal = ordered
            .Where(static result => result.Outcome == InvariantOutcomeV1.Fail && result.Severity == InvariantSeverityV1.FatalAuthority)
            .Select(static result => result.InvariantId)
            .ToArray();

        return new InvariantBarrierDecisionV1(
            blocking.Length == 0 && fatal.Length == 0,
            fatal.Length != 0,
            Array.AsReadOnly(blocking),
            Array.AsReadOnly(fatal));
    }
}
