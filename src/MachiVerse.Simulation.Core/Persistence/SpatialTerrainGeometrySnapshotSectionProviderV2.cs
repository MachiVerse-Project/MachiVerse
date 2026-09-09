using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Standalone section provider for spatial.terrain_geometry record schema 2.0.
/// It implements the common section-provider boundary but is not yet selected by the exact-97
/// production composition, whose v1 identity policy remains unchanged until migration activation.
/// </summary>
public sealed class SpatialTerrainGeometrySnapshotSectionProviderV2 : IDomainPartitionSnapshotSectionProviderV1
{
    private static readonly DomainPartitionIdentityV1 StandardIdentity =
        StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId);

    public string SectionId => SpatialTerrainGeometryRecordSchemaV2.PartitionId;
    public SchemaRefV1 SectionSchema => StandardIdentity.PartitionSchema;

    public CanonicalSnapshotSectionMaterialV1 Create(
        IDomainPartitionSnapshotAuthorityV1 authority,
        IDomainRecordSchemaResolverV1? references = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (authority is not SpatialTerrainGeometrySnapshotAuthorityV2 terrain)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-provider-authority-type");
        terrain.VerifyBoundAuthority();
        ValidateAuthorityReferences(terrain, references);

        var fragments = BuildFragments(terrain);
        var section = new CanonicalSnapshotSectionMaterialV1(
            SectionId,
            SectionSchema,
            terrain.ActualItemCount,
            terrain.Header.CanonicalDigest.ToArray(),
            fragments);
        ValidateCreatedSection(section);
        return section;
    }

    public SnapshotSectionSemanticVerifierV1 CreateSemanticVerifier(
        PartitionStateHeaderV1 expectedHeader,
        IDomainRecordSchemaResolverV1? references = null)
    {
        ArgumentNullException.ThrowIfNull(expectedHeader);
        RequireHeaderIdentity(expectedHeader);
        var frozen = CloneHeader(expectedHeader);
        return new SnapshotSectionSemanticVerifierV1(
            SectionId,
            SectionSchema,
            fragments => VerifyRecovered(frozen, fragments, references))
        {
            VerifyWithContext = (fragments, context) => VerifyRecovered(
                frozen,
                fragments,
                context.DomainReferences ?? references),
        };
    }

    private static IReadOnlyList<SnapshotSectionFragmentMaterialV1> BuildFragments(
        SpatialTerrainGeometrySnapshotAuthorityV2 authority)
    {
        var records = authority.Partition.RecordSet.RecordsCanonical;
        var headerBytes = DomainPartitionSnapshotWireCodecV1.EncodeHeader(authority.Header);
        var baseSize = LengthDelimitedFieldSize(1, headerBytes.Length);
        if (baseSize > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
            throw new InvalidDataException("persistence.snapshot-item-too-large");

        if (records.Count == 0)
        {
            var payload = SpatialTerrainGeometrySnapshotFragmentWireV2.Encode(
                authority.Header,
                Array.Empty<SpatialTerrainGeometryRecordMaterialV2>());
            return Array.AsReadOnly(new[]
            {
                new SnapshotSectionFragmentMaterialV1(
                    SpatialTerrainGeometryRecordSchemaV2.PartitionId,
                    0,
                    1,
                    null,
                    null,
                    0,
                    payload),
            });
        }

        var groups = new List<SpatialTerrainGeometryRecordMaterialV2[]>();
        var current = new List<SpatialTerrainGeometryRecordMaterialV2>();
        var currentSize = baseSize;
        foreach (var record in records)
        {
            var recordBytes = SpatialTerrainGeometryRecordWireCodecV2.Encode(record);
            var recordSize = LengthDelimitedFieldSize(2, recordBytes.Length);
            if (checked(baseSize + recordSize) > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");
            if (current.Count > 0 &&
                checked(currentSize + recordSize) > CanonicalSnapshotSectionValidationV1.TargetUncompressedBytes)
            {
                groups.Add(current.ToArray());
                current.Clear();
                currentSize = baseSize;
            }
            current.Add(record);
            currentSize = checked(currentSize + recordSize);
        }
        if (current.Count > 0) groups.Add(current.ToArray());
        if (groups.Count == 0 || groups.Count > uint.MaxValue)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-fragment-count");

        var count = checked((uint)groups.Count);
        var fragments = new SnapshotSectionFragmentMaterialV1[groups.Count];
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var payload = SpatialTerrainGeometrySnapshotFragmentWireV2.Encode(authority.Header, group);
            if (payload.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");
            fragments[index] = new SnapshotSectionFragmentMaterialV1(
                SpatialTerrainGeometryRecordSchemaV2.PartitionId,
                checked((uint)index),
                count,
                group[0].RecordId.ToBytes(),
                group[^1].RecordId.ToBytes(),
                checked((ulong)group.Length),
                payload);
        }
        return Array.AsReadOnly(fragments);
    }

    private static SnapshotSectionSemanticVerificationV1 VerifyRecovered(
        PartitionStateHeaderV1 expectedHeader,
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments,
        IDomainRecordSchemaResolverV1? references)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        if (fragments.Count == 0)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-section-fragment-missing");

        var records = new List<SpatialTerrainGeometryRecordMaterialV2>();
        OpaqueId128? previous = null;
        ulong total = 0;
        for (var index = 0; index < fragments.Count; index++)
        {
            var fragment = fragments[index]
                ?? throw new InvalidDataException("persistence.snapshot.terrain-v2-fragment-null");
            if (!string.Equals(fragment.SectionId, SpatialTerrainGeometryRecordSchemaV2.PartitionId, StringComparison.Ordinal) ||
                fragment.FragmentIndex != checked((uint)index) ||
                fragment.FragmentCount != checked((uint)fragments.Count))
                throw new InvalidDataException("persistence.snapshot.terrain-v2-fragment-shape");

            var decoded = SpatialTerrainGeometrySnapshotFragmentWireV2.Decode(fragment.FragmentPayload);
            RequireSameHeader(expectedHeader, decoded.Header);
            if (decoded.Records.Count != checked((int)fragment.ItemCount))
                throw new InvalidDataException("persistence.snapshot.terrain-v2-fragment-item-count");

            if (decoded.Records.Count == 0)
            {
                if (fragment.FirstRecordId is not null || fragment.LastRecordId is not null)
                    throw new InvalidDataException("persistence.snapshot.terrain-v2-fragment-empty-range");
            }
            else
            {
                var first = decoded.Records[0].RecordId.ToBytes();
                var last = decoded.Records[^1].RecordId.ToBytes();
                if (fragment.FirstRecordId is null || fragment.LastRecordId is null ||
                    !first.AsSpan().SequenceEqual(fragment.FirstRecordId) ||
                    !last.AsSpan().SequenceEqual(fragment.LastRecordId))
                    throw new InvalidDataException("persistence.snapshot.terrain-v2-fragment-range");
            }

            foreach (var record in decoded.Records)
            {
                if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                    throw new InvalidDataException("persistence.snapshot.terrain-v2-fragment-record-order");
                previous = record.RecordId;
                _ = SpatialTerrainGeometryPayloadCanonicalDigestV2.Compute(record.Payload, references);
                records.Add(record);
            }
            total = checked(total + fragment.ItemCount);
        }

        if (total != expectedHeader.ItemCount || total != checked((ulong)records.Count))
            throw new InvalidDataException("persistence.snapshot.terrain-v2-restored-count");
        if (expectedHeader.ItemCount == 0 && fragments.Count != 1)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-empty-fragment-count");

        var partition = new SpatialTerrainGeometryPartitionStateV2(records);
        var authority = new SpatialTerrainGeometrySnapshotAuthorityV2(partition, expectedHeader);
        authority.VerifyBoundAuthority();
        return new SnapshotSectionSemanticVerificationV1(
            authority.ActualItemCount,
            authority.Header.CanonicalDigest.ToArray());
    }

    private static void ValidateAuthorityReferences(
        SpatialTerrainGeometrySnapshotAuthorityV2 authority,
        IDomainRecordSchemaResolverV1? references)
    {
        foreach (var record in authority.Partition.RecordSet.RecordsCanonical)
            _ = SpatialTerrainGeometryPayloadCanonicalDigestV2.Compute(record.Payload, references);
    }

    private static void ValidateCreatedSection(CanonicalSnapshotSectionMaterialV1 section)
    {
        if (!string.Equals(section.SectionId, SpatialTerrainGeometryRecordSchemaV2.PartitionId, StringComparison.Ordinal) ||
            section.SectionSchema != StandardIdentity.PartitionSchema)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-section-identity");
        if (section.LogicalContentDigest.Length != 32)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-section-digest");
        if (section.LogicalItemCount != section.Fragments.Aggregate(
                0UL,
                static (sum, fragment) => checked(sum + fragment.ItemCount)))
            throw new InvalidDataException("persistence.snapshot.terrain-v2-section-item-count");
        for (var index = 0; index < section.Fragments.Count; index++)
        {
            var fragment = section.Fragments[index];
            if (fragment.FragmentIndex != checked((uint)index) ||
                fragment.FragmentCount != checked((uint)section.Fragments.Count) ||
                fragment.FragmentPayload.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot.terrain-v2-section-fragment-shape");
        }
    }

    private static void RequireHeaderIdentity(PartitionStateHeaderV1 header)
    {
        if (header.PartitionId != StandardIdentity.PartitionId ||
            header.OwnerDomain != StandardIdentity.OwnerDomain ||
            header.Schema != StandardIdentity.PartitionSchema)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-verifier-header-identity");
    }

    private static PartitionStateHeaderV1 CloneHeader(PartitionStateHeaderV1 header)
        => new(
            StandardIdentity,
            header.Revision,
            header.BasisStep,
            header.DetailLevel,
            header.ItemCount,
            header.CanonicalDigest.ToArray());

    private static void RequireSameHeader(PartitionStateHeaderV1 expected, PartitionStateHeaderV1 actual)
    {
        if (expected.PartitionId != actual.PartitionId ||
            expected.OwnerDomain != actual.OwnerDomain ||
            expected.Schema != actual.Schema ||
            expected.Revision != actual.Revision ||
            expected.BasisStep != actual.BasisStep ||
            expected.DetailLevel != actual.DetailLevel ||
            expected.ItemCount != actual.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(expected.CanonicalDigest, actual.CanonicalDigest))
            throw new InvalidDataException("persistence.snapshot.terrain-v2-restored-header");
    }

    private static int LengthDelimitedFieldSize(int fieldNumber, int payloadLength)
    {
        if (fieldNumber <= 0 || payloadLength < 0) throw new ArgumentOutOfRangeException();
        var tag = checked((ulong)fieldNumber << 3) | 2UL;
        return checked(VarUIntSize(tag) + VarUIntSize(checked((ulong)payloadLength)) + payloadLength);
    }

    private static int VarUIntSize(ulong value)
    {
        var size = 1;
        while (value >= 0x80)
        {
            size++;
            value >>= 7;
        }
        return size;
    }
}
