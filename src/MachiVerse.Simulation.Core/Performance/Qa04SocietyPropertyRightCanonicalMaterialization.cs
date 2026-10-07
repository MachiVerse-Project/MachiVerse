using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04SocietyPropertyRightCanonicalMaterializationV1
{
    internal Qa04SocietyPropertyRightCanonicalMaterializationV1(
        Qa04PhysicalD0PropertyAssetSupportMaterializationV1 support,
        DomainPartitionStateV1<SocietyPropertyRightPayloadV1> propertyRights,
        IReadOnlyList<DomainRecordEnvelopeV1<SocietyPropertyRightPayloadV1>> recordsByOrdinal,
        IDomainRecordSchemaResolverV1 references)
    {
        Support = support;
        PropertyRights = propertyRights;
        RecordsByOrdinal = recordsByOrdinal;
        References = references;
    }

    public Qa04PhysicalD0PropertyAssetSupportMaterializationV1 Support { get; }
    public DomainPartitionStateV1<SocietyPropertyRightPayloadV1> PropertyRights { get; }
    public IReadOnlyList<DomainRecordEnvelopeV1<SocietyPropertyRightPayloadV1>> RecordsByOrdinal { get; }
    public IDomainRecordSchemaResolverV1 References { get; }
    public ulong MaterializedRecordCount => PropertyRights.ItemCount;
}

/// <summary>
/// Production-path materialization for the #240-approved perf.reference.v1 society.property_right
/// 50,000-record package. Assets are actual Physical Presence records produced through the approved
/// first-50,000 Physical D0 support authority; holders are the corresponding actual Residents.
/// </summary>
public static class Qa04SocietyPropertyRightCanonicalAuthorityV1
{
    public const ulong CanonicalCount = 50_000;
    public const uint FullSharePpm = 1_000_000;
    public const ulong GenesisEffectiveFrom = 0;

    public static readonly StableToken RightKind = new("perf.ownership");

    public static void ValidateCanonicalContract()
    {
        Qa04SocietyGovernanceReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.ValidateCanonicalContract();

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyPropertyRightPayloadV1.PartitionId);
        if (slice.StartOrdinal != 270_000 || slice.Count != CanonicalCount || slice.UsesSpecializedIdentity)
            throw new InvalidDataException("qa04.society.property-right-slice-drift");
        if (CanonicalCount != 50_000 ||
            CanonicalCount != Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.CanonicalPhysicalCount ||
            FullSharePpm != 1_000_000 || GenesisEffectiveFrom != 0 || RightKind.Value != "perf.ownership")
            throw new InvalidDataException("qa04.society.property-right-contract-drift");

        var identity = StandardDomainPartitionRegistry.Get(SocietyPropertyRightPayloadV1.PartitionId);
        if (identity.OwnerDomain.Value != "society_economy")
            throw new InvalidDataException("qa04.society.property-right-owner-drift");

        var indexes = StandardSecondaryIndexRegistry.ForPartition(SocietyPropertyRightPayloadV1.PartitionId);
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "society.property-by-asset",
            "society.property-by-holder",
        };
        if (indexes.Count != expected.Count ||
            indexes.Any(static index => index.Authority != IndexAuthorityV1.DerivedRebuildable) ||
            !expected.SetEquals(indexes.Select(static index => index.IndexId.Value)))
            throw new InvalidDataException("qa04.society.property-right-secondary-index-registration-drift");
    }

    public static Qa04SocietyPropertyRightCanonicalMaterializationV1 MaterializeCanonical()
    {
        ValidateCanonicalContract();
        var support = Qa04PhysicalD0PropertyAssetSupportCanonicalAuthorityV1.MaterializeCanonical();
        if (support.Presences.ItemCount != CanonicalCount ||
            support.PhysicalRecordsByOrdinal.Count != checked((int)CanonicalCount))
            throw new InvalidDataException("qa04.society.property-right-physical-authority-count-drift");

        var references = support.References;
        var validator = new StandardDomainPayloadCodecValidatorV1();
        var identity = StandardDomainPartitionRegistry.Get(SocietyPropertyRightPayloadV1.PartitionId);
        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyPropertyRightPayloadV1.PartitionId);
        var records = new DomainRecordEnvelopeV1<SocietyPropertyRightPayloadV1>[checked((int)CanonicalCount)];

        for (ulong p = 0; p < CanonicalCount; p++)
        {
            var physical = support.PhysicalRecordsByOrdinal[checked((int)p)];
            var resident = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(p);
            var assetRef = new PartitionRecordRefV1(PhysicalPresencePayloadV1.PartitionId, physical.Presence.RecordId);
            var holderRef = new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId);
            var payload = new SocietyPropertyRightPayloadV1(
                assetRef,
                holderRef,
                RightKind,
                FullSharePpm,
                GenesisEffectiveFrom,
                EffectiveUntil: null,
                ClaimRef: null);
            validator.Validate(SocietyPropertyRightPayloadV1.PartitionId, payload.ToStandardPayload(), references);

            var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + p));
            if (binding.PartitionId.Value != SocietyPropertyRightPayloadV1.PartitionId ||
                binding.PartitionLocalOrdinal != p || binding.UsesSpecializedIdentity ||
                binding.Descriptor.DetailLevel != DetailLevelV1.D2RegionalAggregate)
                throw new InvalidDataException("qa04.society.property-right-descriptor-binding-drift");

            records[checked((int)p)] = new DomainRecordEnvelopeV1<SocietyPropertyRightPayloadV1>(
                binding.Descriptor.RecordId,
                identity.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                binding.Descriptor.DetailLevel,
                lineageRef: null,
                payload);
        }

        ValidateInvariants(records);
        for (ulong p = 0; p < CanonicalCount; p++)
            ValidateCanonicalRecord(p, records[checked((int)p)], support, references);

        var state = new DomainPartitionStateV1<SocietyPropertyRightPayloadV1>(identity, records);
        if (state.ItemCount != CanonicalCount)
            throw new InvalidDataException("qa04.society.property-right-partition-count-drift");
        ValidateSecondaryIndexes(state);

        return new Qa04SocietyPropertyRightCanonicalMaterializationV1(
            support,
            state,
            Array.AsReadOnly(records),
            references);
    }

    public static void ValidateCanonicalRecord(
        ulong localOrdinal,
        DomainRecordEnvelopeV1<SocietyPropertyRightPayloadV1> record,
        Qa04PhysicalD0PropertyAssetSupportMaterializationV1 support,
        IDomainRecordSchemaResolverV1 references)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(support);
        ArgumentNullException.ThrowIfNull(references);
        if (localOrdinal >= CanonicalCount) throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var slice = Qa04SocietyGovernanceReferenceDecompositionV1.Get(SocietyPropertyRightPayloadV1.PartitionId);
        var binding = Qa04SocietyGovernanceReferenceDecompositionV1.Bind(checked(slice.StartOrdinal + localOrdinal));
        var physical = support.PhysicalRecordsByOrdinal[checked((int)localOrdinal)];
        var resident = Qa04ReferenceWorldMaterializerV1.CreateResidentRecord(localOrdinal);
        var expectedAsset = new PartitionRecordRefV1(PhysicalPresencePayloadV1.PartitionId, physical.Presence.RecordId);
        var expectedHolder = new PartitionRecordRefV1(ResidentIdentityLifecyclePayloadV1.PartitionId, resident.RecordId);
        var identity = StandardDomainPartitionRegistry.Get(SocietyPropertyRightPayloadV1.PartitionId);

        if (record.RecordId != binding.Descriptor.RecordId || record.RecordSchema != identity.RecordSchema ||
            record.Revision != 1 || record.CreatedStep != 0 || record.RetiredStep is not null ||
            record.DetailLevel != DetailLevelV1.D2RegionalAggregate || record.LineageRef is not null)
            throw new InvalidDataException("qa04.society.property-right-envelope-drift");

        var payload = record.Payload;
        if (payload.AssetRef != expectedAsset || payload.HolderRef != expectedHolder ||
            payload.RightKind != RightKind || payload.SharePpm != FullSharePpm ||
            payload.EffectiveFrom != GenesisEffectiveFrom || payload.EffectiveUntil is not null ||
            payload.ClaimRef is not null)
            throw new InvalidDataException("qa04.society.property-right-genesis-drift");

        new StandardDomainPayloadCodecValidatorV1().Validate(
            SocietyPropertyRightPayloadV1.PartitionId,
            payload.ToStandardPayload(),
            references);
    }

    public static void ValidateInvariants(IEnumerable<DomainRecordEnvelopeV1<SocietyPropertyRightPayloadV1>> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var material = records.ToArray();
        if ((ulong)material.Length != CanonicalCount)
            throw new InvalidDataException("qa04.society.property-right-count-drift");

        var ids = new HashSet<OpaqueId128>();
        var assets = new HashSet<PartitionRecordRefV1>();
        var holders = new HashSet<PartitionRecordRefV1>();
        var relationKeys = new HashSet<(PartitionRecordRefV1 Asset, PartitionRecordRefV1 Holder, string Kind)>();
        foreach (var record in material)
        {
            if (!ids.Add(record.RecordId))
                throw new InvalidDataException("qa04.society.property-right-record-id-duplicate");
            if (!assets.Add(record.Payload.AssetRef))
                throw new InvalidDataException("qa04.society.property-right-asset-duplicate");
            if (!holders.Add(record.Payload.HolderRef))
                throw new InvalidDataException("qa04.society.property-right-holder-duplicate");
            if (!relationKeys.Add((record.Payload.AssetRef, record.Payload.HolderRef, record.Payload.RightKind.Value)))
                throw new InvalidDataException("qa04.society.property-right-relation-duplicate");
        }

        foreach (var group in material.GroupBy(static record => record.Payload.AssetRef))
        {
            var total = group.Aggregate(0UL, static (sum, record) => checked(sum + record.Payload.SharePpm));
            if (total != FullSharePpm)
                throw new InvalidDataException("qa04.society.property-right-share-aggregate-drift");
        }
    }

    public static void ValidateSecondaryIndexes(DomainPartitionStateV1<SocietyPropertyRightPayloadV1> propertyRights)
    {
        ArgumentNullException.ThrowIfNull(propertyRights);
        var byAsset = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "society.property-by-asset",
            propertyRights,
            static record => new[] { record.Payload.AssetRef.RecordId });
        var byHolder = DerivedRecordIndexV1<OpaqueId128>.Rebuild(
            "society.property-by-holder",
            propertyRights,
            static record => new[] { record.Payload.HolderRef.RecordId });

        ValidateOneToOneIndex(byAsset.CanonicalEntries.ToArray(), "qa04.society.property-right-by-asset-index-drift");
        ValidateOneToOneIndex(byHolder.CanonicalEntries.ToArray(), "qa04.society.property-right-by-holder-index-drift");
    }

    private static void ValidateOneToOneIndex(
        IReadOnlyList<KeyValuePair<OpaqueId128, IReadOnlyList<OpaqueId128>>> entries,
        string failureCode)
    {
        if (entries.Count != checked((int)CanonicalCount) ||
            entries.Aggregate(0UL, static (sum, entry) => checked(sum + (ulong)entry.Value.Count)) != CanonicalCount ||
            entries.Any(static entry => entry.Value.Count != 1))
            throw new InvalidDataException(failureCode);
    }
}
