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
        ParticipationDomainSnapshotMaterialV1? snapshotMaterial = null,
        ParticipationDomainStateV1? state = null)
        : base("participation", intentEvaluator, partitionCandidateEvaluator)
    {
        if (snapshotMaterial is not null && state is not null)
            throw new InvalidDataException("participation.snapshot-material.runtime-authority-ambiguous");
        SnapshotMaterial = snapshotMaterial;
        State = state;
    }

    /// <summary>
    /// Legacy/pre-bound frozen material seam. New production runtime ownership should prefer State
    /// and derive Snapshot material against the exact frozen WorldState via BindSnapshotMaterial.
    /// </summary>
    public ParticipationDomainSnapshotMaterialV1? SnapshotMaterial { get; }

    public ParticipationDomainStateV1? State { get; }

    public ParticipationDomainSnapshotMaterialV1 RequireSnapshotMaterial()
        => SnapshotMaterial
            ?? throw new InvalidDataException("participation.snapshot-material.runtime-unavailable");

    public ParticipationDomainStateV1 RequireState()
        => State
            ?? throw new InvalidDataException("participation.runtime-state.unavailable");

    public ParticipationDomainSnapshotMaterialV1 BindSnapshotMaterial(WorldStateV1 frozenState)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        return RequireState().BindSnapshotMaterial(frozenState);
    }
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
