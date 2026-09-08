using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains;

public sealed class InfrastructureInformationDomainRuntimeV1 : DeterministicDomainRuntimeV1
{
    public InfrastructureInformationDomainRuntimeV1(
        DomainIntentEvaluatorV1 intentEvaluator,
        DomainPartitionCandidateEvaluatorV1? partitionCandidateEvaluator = null)
        : base("infrastructure_information", intentEvaluator, partitionCandidateEvaluator)
    {
    }
}

public static class InfrastructureInformationPartitionCandidateFactoryV1
{
    private static readonly StableToken InfrastructureInformationOwner = new("infrastructure_information");

    public static PartitionCandidateV1 Create(
        WorldStateV1 state,
        string partitionId,
        ReadOnlySpan<byte> changeSetDigest)
    {
        ArgumentNullException.ThrowIfNull(state);
        var partitionToken = new StableToken(partitionId);
        var identity = StandardDomainPartitionRegistry.Get(partitionToken.Value);
        if (identity.OwnerDomain != InfrastructureInformationOwner)
            throw new InvalidDataException("domain.partition-candidate-foreign-owner");

        var basis = state.Partitions.Get(partitionToken.Value).Header;
        if (basis.BasisStep > state.Header.Step)
            throw new InvalidDataException("domain.partition-candidate-basis-ahead");

        return new PartitionCandidateV1(
            partitionToken,
            InfrastructureInformationOwner,
            basis.Revision,
            state.Header.Step,
            changeSetDigest);
    }
}
