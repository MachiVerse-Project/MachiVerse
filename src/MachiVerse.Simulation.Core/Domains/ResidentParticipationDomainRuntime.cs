using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains;

public sealed class ResidentDomainRuntimeV1 : DeterministicDomainRuntimeV1
{
    public ResidentDomainRuntimeV1(
        DomainIntentEvaluatorV1 intentEvaluator,
        DomainPartitionCandidateEvaluatorV1? partitionCandidateEvaluator = null)
        : base("resident", intentEvaluator, partitionCandidateEvaluator)
    {
    }
}

public sealed class ParticipationDomainRuntimeV1 : DeterministicDomainRuntimeV1
{
    public ParticipationDomainRuntimeV1(
        DomainIntentEvaluatorV1 intentEvaluator,
        DomainPartitionCandidateEvaluatorV1? partitionCandidateEvaluator = null,
        ParticipationDomainSnapshotMaterialV1? snapshotMaterial = null)
        : base("participation", intentEvaluator, partitionCandidateEvaluator)
    {
        SnapshotMaterial = snapshotMaterial;
    }

    public ParticipationDomainSnapshotMaterialV1? SnapshotMaterial { get; }

    public ParticipationDomainSnapshotMaterialV1 RequireSnapshotMaterial()
        => SnapshotMaterial
            ?? throw new InvalidDataException("participation.snapshot-material.runtime-unavailable");
}

public static class ResidentParticipationPartitionCandidateFactoryV1
{
    private static readonly StableToken ResidentOwner = new("resident");
    private static readonly StableToken ParticipationOwner = new("participation");

    public static PartitionCandidateV1 CreateResident(
        WorldStateV1 state,
        string partitionId,
        ReadOnlySpan<byte> changeSetDigest)
        => Create(state, ResidentOwner, partitionId, changeSetDigest);

    public static PartitionCandidateV1 CreateParticipation(
        WorldStateV1 state,
        string partitionId,
        ReadOnlySpan<byte> changeSetDigest)
        => Create(state, ParticipationOwner, partitionId, changeSetDigest);

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
