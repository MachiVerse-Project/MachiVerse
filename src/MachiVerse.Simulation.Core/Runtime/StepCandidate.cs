using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Runtime;

public enum CausalityRefKindV1 : byte
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

public sealed record CausalityRefV1(
    CausalityRefKindV1 Kind,
    byte[] Id,
    ulong? BasisStep)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
            throw new InvalidDataException("candidate.causality-kind-invalid");
        ArgumentNullException.ThrowIfNull(Id);
        if (Id.Length is < 1 or > 64)
            throw new InvalidDataException("candidate.causality-id-length-invalid");
    }
}

public enum InvariantSeverityV1 : byte
{
    Diagnostic = 0,
    CommitBlocking = 1,
    FatalAuthority = 2,
}

public enum InvariantOutcomeV1 : byte
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
        if (string.IsNullOrEmpty(invariantId.Value))
            throw new ArgumentException("Invariant id is required.", nameof(invariantId));
        if (!Enum.IsDefined(severity)) throw new ArgumentOutOfRangeException(nameof(severity));
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        if (diagnosticCode is { } code && string.IsNullOrEmpty(code.Value))
            throw new ArgumentException("Diagnostic code must be a valid StableToken.", nameof(diagnosticCode));

        var participants = participantRefs?.ToArray() ?? Array.Empty<CausalityRefV1>();
        foreach (var participant in participants)
        {
            ArgumentNullException.ThrowIfNull(participant);
            participant.Validate();
        }

        InvariantId = invariantId;
        Severity = severity;
        Outcome = outcome;
        ParticipantRefs = Array.AsReadOnly(participants);
        DiagnosticCode = diagnosticCode;
    }

    public StableToken InvariantId { get; }
    public InvariantSeverityV1 Severity { get; }
    public InvariantOutcomeV1 Outcome { get; }
    public IReadOnlyList<CausalityRefV1> ParticipantRefs { get; }
    public StableToken? DiagnosticCode { get; }
}

public enum InvariantBarrierDecisionV1
{
    CommitAllowed = 1,
    StepAbort = 2,
    FatalAuthority = 3,
}

public static class InvariantBarrierV1
{
    public static InvariantBarrierDecisionV1 Evaluate(IEnumerable<InvariantResultV1> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var sawCommitBlockingFailure = false;
        foreach (var result in results)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (result.Outcome != InvariantOutcomeV1.Fail) continue;
            if (result.Severity == InvariantSeverityV1.FatalAuthority)
                return InvariantBarrierDecisionV1.FatalAuthority;
            if (result.Severity == InvariantSeverityV1.CommitBlocking)
                sawCommitBlockingFailure = true;
        }
        return sawCommitBlockingFailure
            ? InvariantBarrierDecisionV1.StepAbort
            : InvariantBarrierDecisionV1.CommitAllowed;
    }
}

/// <summary>
/// Pre-commit candidate for exactly one State(S) -> State(S+1) transition. This type deliberately
/// has no "confirmed" or "committed" state; durable authority is established only by the later
/// persistence finalization boundary.
/// </summary>
public sealed class StepCandidateV1
{
    public StepCandidateV1(
        OpaqueId128 candidateId,
        FrozenStepInputV1 frozenInput,
        CanonicalIntentMergeResultV1 mergedIntents,
        IEnumerable<InvariantResultV1> invariantResults)
    {
        if (candidateId.IsZero) throw new ArgumentException("CandidateId ZERO is invalid.", nameof(candidateId));
        ArgumentNullException.ThrowIfNull(frozenInput);
        ArgumentNullException.ThrowIfNull(mergedIntents);
        ArgumentNullException.ThrowIfNull(invariantResults);

        var invariants = invariantResults
            .OrderBy(static result => result.InvariantId.Value, StringComparer.Ordinal)
            .ToArray();
        if (invariants.Select(static result => result.InvariantId.Value).Distinct(StringComparer.Ordinal).Count() != invariants.Length)
            throw new InvalidDataException("candidate.invariant-id-duplicate");

        CandidateId = candidateId;
        WorldId = frozenInput.WorldId;
        BasisStep = frozenInput.BasisStep;
        TargetStep = frozenInput.TargetStep;
        ConfigGeneration = frozenInput.ConfigGeneration;
        OrderedIntents = mergedIntents.OrderedIntents;
        ConflictGroups = mergedIntents.ConflictGroups;
        InvariantResults = Array.AsReadOnly(invariants);
        BarrierDecision = InvariantBarrierV1.Evaluate(invariants);
    }

    public OpaqueId128 CandidateId { get; }
    public OpaqueId128 WorldId { get; }
    public ulong BasisStep { get; }
    public ulong TargetStep { get; }
    public ulong ConfigGeneration { get; }
    public IReadOnlyList<MutationIntentEnvelopeV1> OrderedIntents { get; }
    public IReadOnlyList<CanonicalConflictGroupV1> ConflictGroups { get; }
    public IReadOnlyList<InvariantResultV1> InvariantResults { get; }
    public InvariantBarrierDecisionV1 BarrierDecision { get; }

    public void RequireCommitAllowed()
    {
        switch (BarrierDecision)
        {
            case InvariantBarrierDecisionV1.CommitAllowed:
                return;
            case InvariantBarrierDecisionV1.StepAbort:
                throw new InvalidDataException("candidate.commit-blocked-by-invariant");
            case InvariantBarrierDecisionV1.FatalAuthority:
                throw new InvalidDataException("candidate.fatal-authority-invariant");
            default:
                throw new InvalidDataException("candidate.invariant-barrier-invalid");
        }
    }
}

public sealed record StepCoordinatorFoundationResultV1(
    FrozenStepInputV1 FrozenInput,
    DomainExecutionPlanV1 ExecutionPlan,
    StepCandidateV1 Candidate);

public static class StepCoordinatorFoundationV1
{
    public static StepCoordinatorFoundationResultV1 Prepare(
        OpaqueId128 candidateId,
        FrozenStepInputV1 frozenInput,
        IEnumerable<DomainExecutionDescriptorV1> domains,
        IEnumerable<MutationIntentEnvelopeV1> intents,
        IEnumerable<MutationKindRegistrationV1> mutationRegistry,
        IEnumerable<InvariantResultV1> invariants)
    {
        ArgumentNullException.ThrowIfNull(frozenInput);
        var plan = DomainExecutionPlanV1.Build(domains);
        var merged = CanonicalIntentMergerV1.Build(frozenInput, intents, mutationRegistry);
        var candidate = new StepCandidateV1(candidateId, frozenInput, merged, invariants);
        return new StepCoordinatorFoundationResultV1(frozenInput, plan, candidate);
    }
}
