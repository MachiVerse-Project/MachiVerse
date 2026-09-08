using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains;

public sealed class GovernanceSecurityDomainRuntimeV1 : DeterministicDomainRuntimeV1
{
    public GovernanceSecurityDomainRuntimeV1(
        DomainIntentEvaluatorV1 intentEvaluator,
        DomainPartitionCandidateEvaluatorV1? partitionCandidateEvaluator = null)
        : base("governance_security", intentEvaluator, partitionCandidateEvaluator)
    {
    }
}

public static class GovernanceSecurityPartitionCandidateFactoryV1
{
    private static readonly StableToken GovernanceSecurityOwner = new("governance_security");

    public static PartitionCandidateV1 Create(
        WorldStateV1 state,
        string partitionId,
        ReadOnlySpan<byte> changeSetDigest)
    {
        ArgumentNullException.ThrowIfNull(state);
        var partitionToken = new StableToken(partitionId);
        var identity = StandardDomainPartitionRegistry.Get(partitionToken.Value);
        if (identity.OwnerDomain != GovernanceSecurityOwner)
            throw new InvalidDataException("domain.partition-candidate-foreign-owner");

        var basis = state.Partitions.Get(partitionToken.Value).Header;
        if (basis.BasisStep > state.Header.Step)
            throw new InvalidDataException("domain.partition-candidate-basis-ahead");
        return new PartitionCandidateV1(
            partitionToken,
            GovernanceSecurityOwner,
            basis.Revision,
            state.Header.Step,
            changeSetDigest);
    }
}
