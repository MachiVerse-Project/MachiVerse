using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains;

public sealed class SocietyEconomyDomainRuntimeV1 : DeterministicDomainRuntimeV1
{
    public SocietyEconomyDomainRuntimeV1(
        DomainIntentEvaluatorV1 intentEvaluator,
        DomainPartitionCandidateEvaluatorV1? partitionCandidateEvaluator = null,
        SocietyEconomyDomainStateV1? state = null)
        : base("society_economy", intentEvaluator, partitionCandidateEvaluator)
    {
        State = state;
    }

    public SocietyEconomyDomainStateV1? State { get; }

    public SocietyEconomyDomainStateV1 RequireState()
        => State ?? throw new InvalidDataException("society.runtime-state.unavailable");

    public SocietyEconomyDomainSnapshotMaterialV1 BindSnapshotMaterial(WorldStateV1 frozenState)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        return RequireState().BindSnapshotMaterial(frozenState);
    }
}

public static class SocietyEconomyPartitionCandidateFactoryV1
{
    private static readonly StableToken SocietyEconomyOwner = new("society_economy");

    public static PartitionCandidateV1 Create(
        WorldStateV1 state,
        string partitionId,
        ReadOnlySpan<byte> changeSetDigest)
    {
        ArgumentNullException.ThrowIfNull(state);
        var partitionToken = new StableToken(partitionId);
        var identity = StandardDomainPartitionRegistry.Get(partitionToken.Value);
        if (identity.OwnerDomain != SocietyEconomyOwner)
            throw new InvalidDataException("domain.partition-candidate-foreign-owner");

        var basis = state.Partitions.Get(partitionToken.Value).Header;
        if (basis.BasisStep > state.Header.Step)
            throw new InvalidDataException("domain.partition-candidate-basis-ahead");
        return new PartitionCandidateV1(
            partitionToken,
            SocietyEconomyOwner,
            basis.Revision,
            state.Header.Step,
            changeSetDigest);
    }
}
