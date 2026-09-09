using System.Collections.ObjectModel;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.Participation;

/// <summary>
/// Actual authoritative material root for the five Participation partitions. This type never
/// fabricates empty material from WorldState headers; callers must supply bound actual authorities.
/// </summary>
public sealed class ParticipationDomainSnapshotMaterialV1
{
    public ParticipationDomainSnapshotMaterialV1(IEnumerable<IDomainPartitionSnapshotAuthorityV1> authorities)
    {
        ArgumentNullException.ThrowIfNull(authorities);
        var materialized = authorities.ToArray();
        if (materialized.Length != 5)
            throw new InvalidDataException("participation.snapshot-material.authority-count");

        var byId = new Dictionary<string, IDomainPartitionSnapshotAuthorityV1>(StringComparer.Ordinal);
        foreach (var authority in materialized)
        {
            ArgumentNullException.ThrowIfNull(authority);
            authority.VerifyBoundAuthority();
            if (!string.Equals(authority.Identity.OwnerDomain.Value, "participation", StringComparison.Ordinal))
                throw new InvalidDataException($"participation.snapshot-material.foreign-owner:{authority.PartitionId.Value}");
            if (!byId.TryAdd(authority.PartitionId.Value, authority))
                throw new InvalidDataException($"participation.snapshot-material.duplicate:{authority.PartitionId.Value}");
        }

        Binding = Require<ParticipationBindingPayloadV1>(byId, ParticipationBindingPayloadV1.PartitionId);
        AbsencePolicy = Require<ParticipationAbsencePolicyPayloadV1>(byId, ParticipationAbsencePolicyPayloadV1.PartitionId);
        ControlMode = Require<ParticipationControlModePayloadV1>(byId, ParticipationControlModePayloadV1.PartitionId);
        History = Require<ParticipationHistoryPayloadV1>(byId, ParticipationHistoryPayloadV1.PartitionId);
        DetailRequirement = Require<ParticipationDetailRequirementPayloadV1>(byId, ParticipationDetailRequirementPayloadV1.PartitionId);

        Authorities = Array.AsReadOnly(new IDomainPartitionSnapshotAuthorityV1[]
        {
            AbsencePolicy,
            Binding,
            ControlMode,
            DetailRequirement,
            History,
        }.OrderBy(static authority => authority.PartitionId.Value, StringComparer.Ordinal).ToArray());

        ReferenceSources = Array.AsReadOnly<IDomainPartitionSnapshotReferenceSourceV1>(
        [
            new DomainPartitionSnapshotReferenceSourceV1<ParticipationBindingPayloadV1>(Binding),
            new DomainPartitionSnapshotReferenceSourceV1<ParticipationAbsencePolicyPayloadV1>(AbsencePolicy),
            new DomainPartitionSnapshotReferenceSourceV1<ParticipationControlModePayloadV1>(ControlMode),
            new DomainPartitionSnapshotReferenceSourceV1<ParticipationHistoryPayloadV1>(History),
            new DomainPartitionSnapshotReferenceSourceV1<ParticipationDetailRequirementPayloadV1>(DetailRequirement),
        ]);
    }

    public DomainPartitionSnapshotAuthorityV1<ParticipationBindingPayloadV1> Binding { get; }
    public DomainPartitionSnapshotAuthorityV1<ParticipationAbsencePolicyPayloadV1> AbsencePolicy { get; }
    public DomainPartitionSnapshotAuthorityV1<ParticipationControlModePayloadV1> ControlMode { get; }
    public DomainPartitionSnapshotAuthorityV1<ParticipationHistoryPayloadV1> History { get; }
    public DomainPartitionSnapshotAuthorityV1<ParticipationDetailRequirementPayloadV1> DetailRequirement { get; }
    public IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> Authorities { get; }
    public IReadOnlyList<IDomainPartitionSnapshotReferenceSourceV1> ReferenceSources { get; }

    /// <summary>
    /// Binds five already-existing typed empty Participation partitions to the corresponding frozen
    /// WorldState headers. This is intentionally not a header-to-material factory: every typed
    /// DomainPartitionStateV1 root must be supplied by the domain runtime and must actually be empty.
    /// </summary>
    public static ParticipationDomainSnapshotMaterialV1 BindTypedEmpty(
        WorldStateV1 frozenState,
        DomainPartitionStateV1<ParticipationBindingPayloadV1> binding,
        DomainPartitionStateV1<ParticipationAbsencePolicyPayloadV1> absencePolicy,
        DomainPartitionStateV1<ParticipationControlModePayloadV1> controlMode,
        DomainPartitionStateV1<ParticipationHistoryPayloadV1> history,
        DomainPartitionStateV1<ParticipationDetailRequirementPayloadV1> detailRequirement)
    {
        ArgumentNullException.ThrowIfNull(frozenState);
        return new ParticipationDomainSnapshotMaterialV1(
        [
            BindEmpty(
                frozenState,
                binding,
                ParticipationBindingPayloadV1.PartitionId,
                static payload => payload.CanonicalDigest()),
            BindEmpty(
                frozenState,
                absencePolicy,
                ParticipationAbsencePolicyPayloadV1.PartitionId,
                static payload => payload.CanonicalDigest()),
            BindEmpty(
                frozenState,
                controlMode,
                ParticipationControlModePayloadV1.PartitionId,
                static payload => payload.CanonicalDigest()),
            BindEmpty(
                frozenState,
                history,
                ParticipationHistoryPayloadV1.PartitionId,
                static payload => payload.CanonicalDigest()),
            BindEmpty(
                frozenState,
                detailRequirement,
                ParticipationDetailRequirementPayloadV1.PartitionId,
                static payload => payload.CanonicalDigest()),
        ]);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> BindEmpty<TPayload>(
        WorldStateV1 frozenState,
        DomainPartitionStateV1<TPayload> partition,
        string expectedPartitionId,
        Func<TPayload, byte[]> canonicalPayloadDigest)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(canonicalPayloadDigest);
        if (!string.Equals(partition.Identity.PartitionId.Value, expectedPartitionId, StringComparison.Ordinal))
            throw new InvalidDataException($"participation.snapshot-material.empty-partition-identity:{expectedPartitionId}");
        if (partition.ItemCount != 0)
            throw new InvalidDataException($"participation.snapshot-material.empty-partition-nonempty:{expectedPartitionId}");

        var frozenHeader = frozenState.Partitions.Get(expectedPartitionId).Header;
        if (frozenHeader.ItemCount != 0)
            throw new InvalidDataException($"participation.snapshot-material.empty-header-nonzero:{expectedPartitionId}");

        // The authority constructor recomputes PartitionStateHeaderV1.CreateCanonical from the
        // supplied typed partition and rejects any digest/revision/basis/detail mismatch.
        return new DomainPartitionSnapshotAuthorityV1<TPayload>(
            partition,
            frozenHeader,
            canonicalPayloadDigest);
    }

    private static DomainPartitionSnapshotAuthorityV1<TPayload> Require<TPayload>(
        IReadOnlyDictionary<string, IDomainPartitionSnapshotAuthorityV1> byId,
        string partitionId)
    {
        if (!byId.TryGetValue(partitionId, out var authority))
            throw new InvalidDataException($"participation.snapshot-material.missing:{partitionId}");
        if (authority is not DomainPartitionSnapshotAuthorityV1<TPayload> typed)
            throw new InvalidDataException($"participation.snapshot-material.payload-type:{partitionId}");
        return typed;
    }
}
