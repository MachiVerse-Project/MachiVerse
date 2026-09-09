using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains;

public delegate ValueTask<IReadOnlyList<MutationIntentCandidateV1>> DomainIntentEvaluatorV1(
    DomainRuntimeContextV1 context,
    CancellationToken cancellationToken);

public delegate ValueTask<IReadOnlyList<PartitionCandidateV1>> DomainPartitionCandidateEvaluatorV1(
    DomainRuntimeContextV1 context,
    CancellationToken cancellationToken);

public abstract class DeterministicDomainRuntimeV1 : IDomainRuntimeV1
{
    private readonly DomainIntentEvaluatorV1 _intentEvaluator;
    private readonly DomainPartitionCandidateEvaluatorV1? _partitionCandidateEvaluator;

    protected DeterministicDomainRuntimeV1(
        string domainToken,
        DomainIntentEvaluatorV1 intentEvaluator,
        DomainPartitionCandidateEvaluatorV1? partitionCandidateEvaluator = null)
    {
        DomainToken = new StableToken(domainToken);
        _intentEvaluator = intentEvaluator ?? throw new ArgumentNullException(nameof(intentEvaluator));
        _partitionCandidateEvaluator = partitionCandidateEvaluator;
    }

    public StableToken DomainToken { get; }

    public async ValueTask<DomainCandidateOutputV1> ExecuteAsync(
        DomainRuntimeContextV1 context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.PlanEntry.DomainToken != DomainToken)
            throw new InvalidDataException("domain-runtime.owner-mismatch");
        if (context.FrozenInput.BasisStep != context.State.Header.Step)
            throw new InvalidDataException("domain-runtime.basis-step-mismatch");

        var intents = await _intentEvaluator(context, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("domain-runtime.intent-evaluator-null");
        if (intents.Any(intent => intent.SourceDomain != DomainToken || intent.BasisStep != context.FrozenInput.BasisStep))
            throw new InvalidDataException("domain-runtime.intent-source-mismatch");
        StandardDomainRegistryAuthorityV1.Generation1.RequireAuthorizedEmissions(intents);

        IReadOnlyList<PartitionCandidateV1> localPartitionCandidates = Array.Empty<PartitionCandidateV1>();
        if (_partitionCandidateEvaluator is not null)
        {
            localPartitionCandidates = await _partitionCandidateEvaluator(context, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("domain-runtime.partition-candidate-evaluator-null");
        }

        return new DomainCandidateOutputV1(
            DomainToken,
            context.FrozenInput.BasisStep,
            intents,
            localPartitionCandidates);
    }
}

public sealed class SpatialDomainRuntimeV1 : DeterministicDomainRuntimeV1
{
    public SpatialDomainRuntimeV1(
        DomainIntentEvaluatorV1 intentEvaluator,
        DomainPartitionCandidateEvaluatorV1? partitionCandidateEvaluator = null,
        SpatialDomainStateV1? state = null)
        : base("spatial", intentEvaluator, partitionCandidateEvaluator)
    {
        State = state;
    }

    public SpatialDomainStateV1? State { get; }

    public SpatialDomainStateV1 RequireState()
        => State ?? throw new InvalidDataException("spatial.runtime-state.unavailable");

    public SpatialDomainSnapshotMaterialV1 BindSnapshotMaterial(WorldStateV1 frozenState)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        return RequireState().BindSnapshotMaterial(frozenState);
    }
}

public sealed class EnvironmentDomainRuntimeV1 : DeterministicDomainRuntimeV1
{
    private static readonly IReadOnlyList<DomainIntentCapabilityV1> Generation1EmittedCapabilities =
        Array.AsReadOnly(new[]
        {
            new DomainIntentCapabilityV1(
                new StableToken("environment"),
                new StableToken("spatial"),
                new StableToken("spatial.terrain_geometry"),
                new StableToken("spatial.intent.geometry-deform")),
        });

    public EnvironmentDomainRuntimeV1(
        DomainIntentEvaluatorV1 intentEvaluator,
        DomainPartitionCandidateEvaluatorV1? partitionCandidateEvaluator = null)
        : base("environment", intentEvaluator, partitionCandidateEvaluator)
    {
    }

    // This list is intentionally narrower than the Phase 4 future Intent catalog.
    // It reflects the Environment -> Spatial geometry-deform path exercised by the
    // current production runtime contract (SIM-07) and enforced by DomainRegistry.
    public static IReadOnlyList<DomainIntentCapabilityV1> EmittedCapabilities => Generation1EmittedCapabilities;
}

public static class DomainOwnedPartitionCandidateFactoryV1
{
    public static PartitionCandidateV1 CreateSpatial(
        WorldStateV1 state,
        string partitionId,
        ReadOnlySpan<byte> changeSetDigest)
        => Create(state, new StableToken("spatial"), partitionId, changeSetDigest);

    public static PartitionCandidateV1 CreateEnvironment(
        WorldStateV1 state,
        string partitionId,
        ReadOnlySpan<byte> changeSetDigest)
        => Create(state, new StableToken("environment"), partitionId, changeSetDigest);

    private static PartitionCandidateV1 Create(
        WorldStateV1 state,
        StableToken ownerDomain,
        string partitionId,
        ReadOnlySpan<byte> changeSetDigest)
    {
        ArgumentNullException.ThrowIfNull(state);
        var partitionToken = new StableToken(partitionId);
        var identity = StandardDomainPartitionRegistry.Get(partitionToken.Value);
        if (identity.OwnerDomain != ownerDomain)
            throw new InvalidDataException("domain.partition-candidate-foreign-owner");

        var basis = state.Partitions.Get(partitionToken.Value).Header;
        if (basis.BasisStep > state.Header.Step)
            throw new InvalidDataException("domain.partition-candidate-basis-ahead");
        return new PartitionCandidateV1(
            partitionToken,
            ownerDomain,
            basis.Revision,
            state.Header.Step,
            changeSetDigest);
    }
}
