using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains;

public sealed class PhysicalBuiltDomainRuntimeV1 : DeterministicDomainRuntimeV1
{
    public PhysicalBuiltDomainRuntimeV1(
        DomainIntentEvaluatorV1 intentEvaluator,
        DomainPartitionCandidateEvaluatorV1? partitionCandidateEvaluator = null,
        PhysicalBuiltDomainStateV1? state = null)
        : base("physical_built", intentEvaluator, partitionCandidateEvaluator)
    {
        State = state;
    }

    public PhysicalBuiltDomainStateV1? State { get; }

    public PhysicalBuiltDomainStateV1 RequireState()
        => State ?? throw new InvalidDataException("physical-built.runtime-state.unavailable");

    public PhysicalBuiltDomainSnapshotMaterialV1 BindSnapshotMaterial(WorldStateV1 frozenState)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        return RequireState().BindSnapshotMaterial(frozenState);
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
