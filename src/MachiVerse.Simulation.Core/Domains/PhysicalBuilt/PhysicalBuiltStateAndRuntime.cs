using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.PhysicalBuilt;

public sealed record PhysicalPresenceStateV1(
    OpaqueId128 PresenceId,
    OpaqueId128 SubjectRef,
    PositionMmV1 PositionMm,
    VelocityUmPerSecondV1 VelocityUmPerSecond,
    StableToken PresenceMode,
    ulong Revision)
{
    public void Validate()
    {
        if (PresenceId.IsZero || SubjectRef.IsZero)
            throw new InvalidDataException("physical.presence-id-zero");
        if (Revision == 0)
            throw new InvalidDataException("physical.presence-revision-zero");
    }
}

public sealed record PhysicalContainmentStateV1(
    OpaqueId128 ItemRef,
    OpaqueId128 ContainerRef,
    ulong Revision)
{
    public void Validate()
    {
        if (ItemRef.IsZero || ContainerRef.IsZero)
            throw new InvalidDataException("physical.containment-id-zero");
        if (ItemRef == ContainerRef)
            throw new InvalidDataException("physical.containment-self");
        if (Revision == 0)
            throw new InvalidDataException("physical.containment-revision-zero");
    }
}

public sealed record BuiltOpeningStateV1(
    OpaqueId128 OpeningId,
    StableToken ApertureState,
    StableToken MechanismState,
    ulong Revision)
{
    public void Validate()
    {
        if (OpeningId.IsZero) throw new InvalidDataException("built.opening-id-zero");
        if (Revision == 0) throw new InvalidDataException("built.opening-revision-zero");
    }
}

public sealed record ConstructionWorksiteStateV1(
    OpaqueId128 WorksiteId,
    uint ProgressPpm,
    IReadOnlyList<OpaqueId128> DeliveredMaterialRefs,
    ulong Revision)
{
    public void Validate()
    {
        if (WorksiteId.IsZero) throw new InvalidDataException("built.worksite-id-zero");
        if (ProgressPpm > 1_000_000) throw new InvalidDataException("built.worksite-progress-range");
        if (Revision == 0) throw new InvalidDataException("built.worksite-revision-zero");
        ArgumentNullException.ThrowIfNull(DeliveredMaterialRefs);
        if (DeliveredMaterialRefs.Any(static id => id.IsZero))
            throw new InvalidDataException("built.worksite-material-ref-zero");
        if (DeliveredMaterialRefs.Distinct().Count() != DeliveredMaterialRefs.Count)
            throw new InvalidDataException("built.worksite-material-ref-duplicate");
    }
}

public enum MaterialHandoffStateV1 : byte
{
    Prepared = 1,
    Committed = 2,
}

public sealed record PhysicalMaterialHandoffStateV1(
    OpaqueId128 HandoffId,
    OpaqueId128 MaterialRef,
    long MassGram,
    OpaqueId128 SourceContainerRef,
    OpaqueId128 TargetScopeRef,
    MaterialHandoffStateV1 State,
    ulong Revision)
{
    public void Validate()
    {
        if (HandoffId.IsZero || MaterialRef.IsZero || SourceContainerRef.IsZero || TargetScopeRef.IsZero)
            throw new InvalidDataException("physical.material-handoff-id-zero");
        if (MassGram <= 0)
            throw new InvalidDataException("physical.material-handoff-mass-nonpositive");
        if (!Enum.IsDefined(State))
            throw new InvalidDataException("physical.material-handoff-state-invalid");
        if (Revision == 0)
            throw new InvalidDataException("physical.material-handoff-revision-zero");
    }

    public PhysicalMaterialHandoffStateV1 Commit()
    {
        if (State != MaterialHandoffStateV1.Prepared)
            throw new InvalidDataException("physical.material-handoff-not-prepared");
        return this with { State = MaterialHandoffStateV1.Committed, Revision = checked(Revision + 1) };
    }
}

public static class PhysicalBuiltSemanticRegistryV1
{
    public static IReadOnlyList<StableToken> OperationKinds { get; } = Tokens(
        "physical.move.request",
        "physical.item.pickup",
        "physical.item.drop",
        "physical.item.transfer",
        "built.opening.set-state",
        "built.construction.start",
        "built.construction.work",
        "built.demolition.start",
        "physical.repair.perform",
        "physical.combustion.ignite");

    public static IReadOnlyList<StableToken> EventKinds { get; } = Tokens(
        "physical.movement.completed",
        "physical.movement.blocked",
        "physical.contact.occurred",
        "physical.item.picked-up",
        "physical.item.dropped",
        "physical.item.transferred",
        "built.opening.changed",
        "built.construction.started",
        "built.construction.progressed",
        "built.construction.completed",
        "built.demolition.started",
        "built.demolition.completed",
        "physical.damage.occurred",
        "physical.repair.completed",
        "physical.combustion.started",
        "physical.combustion.ended",
        "physical.material-handoff.prepared",
        "physical.material-handoff.committed");

    public static IReadOnlyList<StableToken> TargetIntentKinds { get; } = Tokens(
        "physical.intent.move",
        "physical.intent.apply-force",
        "physical.intent.transfer-item",
        "physical.intent.set-opening",
        "physical.intent.apply-damage",
        "physical.intent.repair",
        "physical.intent.ignite",
        "physical.intent.extinguish",
        "physical.intent.create-worksite",
        "physical.intent.apply-work",
        "physical.intent.material-handoff");

    private static IReadOnlyList<StableToken> Tokens(params string[] values)
    {
        var tokens = values.Select(static value => new StableToken(value)).ToArray();
        if (tokens.Select(static token => token.Value).Distinct(StringComparer.Ordinal).Count() != tokens.Length)
            throw new InvalidOperationException("Physical/Built semantic registry contains duplicate token.");
        return Array.AsReadOnly(tokens);
    }
}

public sealed record PhysicalItemTransferRequestV1(
    OpaqueId128 ItemRef,
    OpaqueId128 SourceContainerRef,
    OpaqueId128 TargetContainerRef,
    SameStepOrderKey OrderKey)
{
    public void Validate()
    {
        if (ItemRef.IsZero || SourceContainerRef.IsZero || TargetContainerRef.IsZero)
            throw new InvalidDataException("physical.item-transfer-id-zero");
        if (SourceContainerRef == TargetContainerRef)
            throw new InvalidDataException("physical.item-transfer-same-container");
        ArgumentNullException.ThrowIfNull(OrderKey);
    }
}

public sealed record PhysicalItemTransferDecisionV1(
    OpaqueId128 ItemRef,
    OpaqueId128 SourceContainerRef,
    OpaqueId128 TargetContainerRef,
    OpaqueId128 WinningIntentId);

public sealed class PhysicalItemTransferResolutionV1
{
    public PhysicalItemTransferResolutionV1(
        IReadOnlyDictionary<OpaqueId128, OpaqueId128> finalLocations,
        IReadOnlyList<PhysicalItemTransferDecisionV1> decisions)
    {
        FinalLocations = finalLocations;
        Decisions = decisions;
    }

    public IReadOnlyDictionary<OpaqueId128, OpaqueId128> FinalLocations { get; }
    public IReadOnlyList<PhysicalItemTransferDecisionV1> Decisions { get; }
}

public static class PhysicalItemTransferResolverV1
{
    public static PhysicalItemTransferResolutionV1 Resolve(
        IReadOnlyDictionary<OpaqueId128, OpaqueId128> basisLocations,
        IEnumerable<PhysicalItemTransferRequestV1> requests)
    {
        ArgumentNullException.ThrowIfNull(basisLocations);
        ArgumentNullException.ThrowIfNull(requests);

        var locations = new SortedDictionary<OpaqueId128, OpaqueId128>();
        foreach (var pair in basisLocations.OrderBy(static pair => pair.Key))
        {
            if (pair.Key.IsZero || pair.Value.IsZero)
                throw new InvalidDataException("physical.item-location-id-zero");
            if (!locations.TryAdd(pair.Key, pair.Value))
                throw new InvalidDataException("physical.item-location-duplicate");
        }

        var ordered = requests.OrderBy(static request => request.OrderKey).ToArray();
        foreach (var request in ordered) request.Validate();

        var decided = new HashSet<OpaqueId128>();
        var decisions = new List<PhysicalItemTransferDecisionV1>();
        foreach (var request in ordered)
        {
            if (decided.Contains(request.ItemRef))
                continue;
            if (!locations.TryGetValue(request.ItemRef, out var currentContainer))
                continue;
            if (currentContainer != request.SourceContainerRef)
                continue;

            locations[request.ItemRef] = request.TargetContainerRef;
            decided.Add(request.ItemRef);
            decisions.Add(new PhysicalItemTransferDecisionV1(
                request.ItemRef,
                request.SourceContainerRef,
                request.TargetContainerRef,
                request.OrderKey.IntentId));
        }

        return new PhysicalItemTransferResolutionV1(
            new SortedDictionary<OpaqueId128, OpaqueId128>(locations),
            Array.AsReadOnly(decisions
                .OrderBy(static decision => decision.ItemRef)
                .ToArray()));
    }
}

public sealed class PhysicalBuiltDomainRuntimeV1 : DeterministicDomainRuntimeV1
{
    public PhysicalBuiltDomainRuntimeV1(
        DomainIntentEvaluatorV1 intentEvaluator,
        DomainPartitionCandidateEvaluatorV1? partitionCandidateEvaluator = null)
        : base("physical_built", intentEvaluator, partitionCandidateEvaluator)
    {
    }
}

public static class PhysicalBuiltPartitionCandidateFactoryV1
{
    private static readonly StableToken OwnerDomain = new("physical_built");

    public static PartitionCandidateV1 Create(
        WorldStateV1 state,
        string partitionId,
        ReadOnlySpan<byte> changeSetDigest)
    {
        ArgumentNullException.ThrowIfNull(state);
        var partitionToken = new StableToken(partitionId);
        var identity = StandardDomainPartitionRegistry.Get(partitionToken.Value);
        if (identity.OwnerDomain != OwnerDomain)
            throw new InvalidDataException("domain.partition-candidate-foreign-owner");

        var basis = state.Partitions.Get(partitionToken.Value).Header;
        if (basis.BasisStep > state.Header.Step)
            throw new InvalidDataException("domain.partition-candidate-basis-ahead");
        return new PartitionCandidateV1(
            partitionToken,
            OwnerDomain,
            basis.Revision,
            state.Header.Step,
            changeSetDigest);
    }
}
