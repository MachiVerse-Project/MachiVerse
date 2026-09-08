using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.PhysicalBuilt;

public sealed record PhysicalContainerLocationV1(
    PartitionRecordRefV1 SubjectRef,
    PartitionRecordRefV1 ContainerRef,
    StableToken? SlotToken,
    StableToken ContainmentMode,
    long Quantity,
    long MassGram)
{
    public PhysicalContainerLocationV1 Validate()
    {
        if (Quantity < 0) throw new InvalidDataException("physical.container-negative-quantity");
        if (MassGram < 0) throw new InvalidDataException("physical.container-negative-mass");
        if (SubjectRef.Equals(ContainerRef)) throw new InvalidDataException("physical.container-self-containment");
        return this;
    }
}

public enum PhysicalMaterialHandoffPhaseV1 : byte
{
    Prepared = 1,
    Committed = 2,
}

public sealed record PhysicalMaterialHandoffV1(
    OpaqueId128 TransactionRef,
    StableToken MaterialKind,
    PartitionRecordRefV1 SourceRef,
    PartitionRecordRefV1 TargetRef,
    long MassGram,
    PhysicalMaterialHandoffPhaseV1 Phase,
    ulong PreparedStep,
    ulong? CommittedStep)
{
    public PhysicalMaterialHandoffV1 Validate()
    {
        if (TransactionRef.IsZero) throw new InvalidDataException("physical.handoff-transaction-zero");
        if (SourceRef.Equals(TargetRef)) throw new InvalidDataException("physical.handoff-same-endpoint");
        if (MassGram <= 0) throw new InvalidDataException("physical.handoff-mass-nonpositive");
        switch (Phase)
        {
            case PhysicalMaterialHandoffPhaseV1.Prepared when CommittedStep is not null:
                throw new InvalidDataException("physical.handoff-prepared-has-committed-step");
            case PhysicalMaterialHandoffPhaseV1.Committed when CommittedStep is null:
                throw new InvalidDataException("physical.handoff-committed-step-missing");
            case PhysicalMaterialHandoffPhaseV1.Committed when CommittedStep!.Value < PreparedStep:
                throw new InvalidDataException("physical.handoff-committed-before-prepared");
            case PhysicalMaterialHandoffPhaseV1.Prepared:
            case PhysicalMaterialHandoffPhaseV1.Committed:
                break;
            default:
                throw new InvalidDataException("physical.handoff-phase-invalid");
        }
        return this;
    }

    public PhysicalMaterialHandoffV1 Commit(ulong committedStep)
    {
        Validate();
        if (Phase != PhysicalMaterialHandoffPhaseV1.Prepared)
            throw new InvalidDataException("physical.handoff-already-terminal");
        if (committedStep < PreparedStep)
            throw new InvalidDataException("physical.handoff-committed-before-prepared");
        return this with
        {
            Phase = PhysicalMaterialHandoffPhaseV1.Committed,
            CommittedStep = committedStep,
        };
    }
}

/// <summary>
/// Canonical physical location projection for identity-bearing subjects. A subject may have exactly
/// one authoritative container/location record. Prepared handoffs do not create a second authority;
/// the authority moves to the target only in the atomic commit result.
/// </summary>
public sealed class PhysicalLocationAuthorityV1
{
    private readonly SortedDictionary<PartitionRecordRefV1, PhysicalContainerLocationV1> _bySubject =
        new(PartitionRecordRefComparerV1.Instance);

    public PhysicalLocationAuthorityV1(IEnumerable<PhysicalContainerLocationV1> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        foreach (var location in locations)
        {
            location.Validate();
            if (!_bySubject.TryAdd(location.SubjectRef, location))
                throw new InvalidDataException("physical.item-multiple-location-authority");
        }
    }

    public IReadOnlyList<PhysicalContainerLocationV1> LocationsCanonical =>
        Array.AsReadOnly(_bySubject.Values.ToArray());

    public PhysicalContainerLocationV1 Get(PartitionRecordRefV1 subjectRef)
        => _bySubject.TryGetValue(subjectRef, out var location)
            ? location
            : throw new KeyNotFoundException("physical.item-location-not-found");

    public PhysicalTransferCommitV1 CommitTransfer(
        PartitionRecordRefV1 subjectRef,
        PhysicalMaterialHandoffV1 preparedHandoff,
        PartitionRecordRefV1 targetContainerRef,
        StableToken? targetSlotToken,
        StableToken targetContainmentMode,
        ulong committedStep)
    {
        var basis = Get(subjectRef).Validate();
        preparedHandoff.Validate();
        if (preparedHandoff.Phase != PhysicalMaterialHandoffPhaseV1.Prepared)
            throw new InvalidDataException("physical.handoff-not-prepared");
        if (preparedHandoff.SourceRef != basis.ContainerRef)
            throw new InvalidDataException("physical.handoff-source-authority-mismatch");
        if (preparedHandoff.TargetRef != targetContainerRef)
            throw new InvalidDataException("physical.handoff-target-mismatch");
        if (preparedHandoff.MassGram != basis.MassGram)
            throw new InvalidDataException("physical.handoff-mass-discontinuity");

        var nextLocation = basis with
        {
            ContainerRef = targetContainerRef,
            SlotToken = targetSlotToken,
            ContainmentMode = targetContainmentMode,
        };
        nextLocation.Validate();
        var committed = preparedHandoff.Commit(committedStep);

        // This object is the candidate result; the mutable projection is intentionally not changed here.
        // StepCandidate/transaction finalization installs both records atomically after invariant success.
        return new PhysicalTransferCommitV1(basis, nextLocation, committed);
    }
}

public sealed record PhysicalTransferCommitV1(
    PhysicalContainerLocationV1 PreviousLocation,
    PhysicalContainerLocationV1 NextLocation,
    PhysicalMaterialHandoffV1 Handoff)
{
    public void ValidateExclusiveAuthority()
    {
        PreviousLocation.Validate();
        NextLocation.Validate();
        Handoff.Validate();
        if (PreviousLocation.SubjectRef != NextLocation.SubjectRef)
            throw new InvalidDataException("physical.handoff-subject-changed");
        if (PreviousLocation.ContainerRef != Handoff.SourceRef || NextLocation.ContainerRef != Handoff.TargetRef)
            throw new InvalidDataException("physical.handoff-endpoint-authority-mismatch");
        if (PreviousLocation.MassGram != NextLocation.MassGram || NextLocation.MassGram != Handoff.MassGram)
            throw new InvalidDataException("physical.handoff-mass-discontinuity");
        if (Handoff.Phase != PhysicalMaterialHandoffPhaseV1.Committed)
            throw new InvalidDataException("physical.handoff-commit-required");
    }
}

internal sealed class PartitionRecordRefComparerV1 : IComparer<PartitionRecordRefV1>
{
    public static readonly PartitionRecordRefComparerV1 Instance = new();

    public int Compare(PartitionRecordRefV1 left, PartitionRecordRefV1 right)
    {
        var partition = string.CompareOrdinal(left.PartitionId.Value, right.PartitionId.Value);
        return partition != 0 ? partition : left.RecordId.CompareTo(right.RecordId);
    }
}
