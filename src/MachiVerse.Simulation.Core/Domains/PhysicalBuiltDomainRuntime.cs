using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains;

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
