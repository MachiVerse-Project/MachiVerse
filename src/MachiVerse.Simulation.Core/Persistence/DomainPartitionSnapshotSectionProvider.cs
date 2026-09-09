using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Production provider for one standard Domain partition schema. The provider serializes only
/// actual DomainPartitionStateV1 material, fragments at record boundaries, and verifies recovery
/// by rebuilding the semantic partition and recomputing PartitionStateHeaderV1.CreateCanonical.
/// </summary>
public sealed class DomainPartitionSnapshotSectionProviderV1<TPayload> : IDomainPartitionSnapshotSectionProviderV1
{
    private readonly DomainPartitionIdentityV1 _identity;
    private readonly Func<TPayload, IReadOnlyDictionary<string, object?>> _toStandardPayload;
    private readonly Func<IReadOnlyDictionary<string, object?>, TPayload> _fromStandardPayload;
    private readonly Func<TPayload, byte[]> _canonicalPayloadDigest;

    public DomainPartitionSnapshotSectionProviderV1(
        string partitionId,
        Func<TPayload, IReadOnlyDictionary<string, object?>> toStandardPayload,
        Func<IReadOnlyDictionary<string, object?>, TPayload> fromStandardPayload,
        Func<TPayload, byte[]> canonicalPayloadDigest)
    {
        _identity = StandardDomainPartitionRegistry.Get(partitionId);
        _toStandardPayload = toStandardPayload ?? throw new ArgumentNullException(nameof(toStandardPayload));
        _fromStandardPayload = fromStandardPayload ?? throw new ArgumentNullException(nameof(fromStandardPayload));
        _canonicalPayloadDigest = canonicalPayloadDigest ?? throw new ArgumentNullException(nameof(canonicalPayloadDigest));

        var descriptor = StandardDomainPayloadSchemaRegistry.Get(partitionId);
        if (descriptor.RecordSchema != _identity.RecordSchema)
            throw new InvalidDataException($"persistence.snapshot.partition-provider-payload-schema-mismatch:{partitionId}");
    }

    public string SectionId => _identity.PartitionId.Value;
    public SchemaRefV1 SectionSchema => _identity.PartitionSchema;

    public CanonicalSnapshotSectionMaterialV1 Create(IDomainPartitionSnapshotAuthorityV1 authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (authority is not DomainPartitionSnapshotAuthorityV1<TPayload> typed)
            throw new InvalidDataException($"persistence.snapshot.partition-provider-authority-type:{SectionId}");
        typed.VerifyBoundAuthority();
        if (typed.Identity != _identity)
            throw new InvalidDataException($"persistence.snapshot.partition-provider-identity-mismatch:{SectionId}");

        var fragments = BuildFragments(typed);
        var section = new CanonicalSnapshotSectionMaterialV1(
            SectionId,
            SectionSchema,
            typed.ActualItemCount,
            typed.Header.CanonicalDigest.ToArray(),
            fragments);

        // Standalone validation intentionally verifies the physical fragment structure now;
        // full 103-section set validation happens at the production composition boundary.
        ValidateCreatedSection(section);
        return section;
    }

    public SnapshotSectionSemanticVerifierV1 CreateSemanticVerifier(PartitionStateHeaderV1 expectedHeader)
    {
        ArgumentNullException.ThrowIfNull(expectedHeader);
        RequireExpectedHeaderIdentity(expectedHeader);
        var frozen = CloneHeader(expectedHeader);
        return new SnapshotSectionSemanticVerifierV1(
            SectionId,
            SectionSchema,
            fragments => VerifyRecoveredFragments(frozen, fragments));
    }

    private IReadOnlyList<SnapshotSectionFragmentMaterialV1> BuildFragments(
        DomainPartitionSnapshotAuthorityV1<TPayload> authority)
    {
        var records = authority.Partition.RecordsCanonical.ToArray();
        if (records.Length == 0)
        {
            var payload = DomainPartitionSnapshotWireCodecV1.EncodeFragment(
                authority,
                Array.Empty<DomainRecordEnvelopeV1<TPayload>>(),
                _toStandardPayload);
            if (payload.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");
            return Array.AsReadOnly(new[]
            {
                new SnapshotSectionFragmentMaterialV1(
                    SectionId,
                    FragmentIndex: 0,
                    FragmentCount: 1,
                    FirstRecordId: null,
                    LastRecordId: null,
                    ItemCount: 0,
                    FragmentPayload: payload),
            });
        }

        var headerBytes = DomainPartitionSnapshotWireCodecV1.EncodeHeader(authority.Header);
        var baseWireSize = LengthDelimitedFieldSize(1, headerBytes.Length);
        if (baseWireSize > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
            throw new InvalidDataException("persistence.snapshot-item-too-large");

        var groups = new List<DomainRecordEnvelopeV1<TPayload>[]>();
        var current = new List<DomainRecordEnvelopeV1<TPayload>>();
        var currentWireSize = baseWireSize;

        foreach (var record in records)
        {
            var recordBytes = DomainPartitionSnapshotWireCodecV1.EncodeRecord(
                SectionId,
                record,
                _toStandardPayload);
            var recordWireSize = LengthDelimitedFieldSize(2, recordBytes.Length);
            if (checked(baseWireSize + recordWireSize) > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");

            if (current.Count > 0 &&
                checked(currentWireSize + recordWireSize) > CanonicalSnapshotSectionValidationV1.TargetUncompressedBytes)
            {
                groups.Add(current.ToArray());
                current.Clear();
                currentWireSize = baseWireSize;
            }

            current.Add(record);
            currentWireSize = checked(currentWireSize + recordWireSize);
        }
        if (current.Count > 0) groups.Add(current.ToArray());
        if (groups.Count == 0 || groups.Count > uint.MaxValue)
            throw new InvalidDataException($"persistence.snapshot-fragment-invalid:{SectionId}");

        var fragmentCount = checked((uint)groups.Count);
        var fragments = new SnapshotSectionFragmentMaterialV1[groups.Count];
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var payload = DomainPartitionSnapshotWireCodecV1.EncodeFragment(
                authority,
                group,
                _toStandardPayload);
            if (payload.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException("persistence.snapshot-item-too-large");
            fragments[i] = new SnapshotSectionFragmentMaterialV1(
                SectionId,
                checked((uint)i),
                fragmentCount,
                group[0].RecordId.ToBytes(),
                group[^1].RecordId.ToBytes(),
                checked((ulong)group.Length),
                payload);
        }
        return Array.AsReadOnly(fragments);
    }

    private SnapshotSectionSemanticVerificationV1 VerifyRecoveredFragments(
        PartitionStateHeaderV1 expectedHeader,
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        if (fragments.Count == 0)
            throw new InvalidDataException($"persistence.snapshot.section-fragment-missing:{SectionId}");

        var restored = new List<DomainRecordEnvelopeV1<TPayload>>();
        OpaqueId128? previousRecord = null;
        ulong total = 0;

        for (var i = 0; i < fragments.Count; i++)
        {
            var fragment = fragments[i] ?? throw new InvalidDataException($"persistence.snapshot.fragment-null:{SectionId}");
            if (!string.Equals(fragment.SectionId, SectionId, StringComparison.Ordinal) ||
                fragment.FragmentIndex != checked((uint)i) ||
                fragment.FragmentCount != checked((uint)fragments.Count))
                throw new InvalidDataException($"persistence.snapshot-fragment-invalid:{SectionId}");

            var decoded = DomainPartitionSnapshotWireCodecV1.DecodeFragment(SectionId, fragment.FragmentPayload);
            RequireSameHeader(expectedHeader, decoded.Header);
            if (decoded.Records.Count != checked((int)fragment.ItemCount))
                throw new InvalidDataException($"persistence.snapshot.fragment-item-count-mismatch:{SectionId}");

            if (decoded.Records.Count == 0)
            {
                if (fragment.FirstRecordId is not null || fragment.LastRecordId is not null)
                    throw new InvalidDataException($"persistence.snapshot.fragment-record-range-incomplete:{SectionId}");
            }
            else
            {
                var first = decoded.Records[0].RecordId.ToBytes();
                var last = decoded.Records[^1].RecordId.ToBytes();
                if (fragment.FirstRecordId is null || fragment.LastRecordId is null ||
                    !first.AsSpan().SequenceEqual(fragment.FirstRecordId) ||
                    !last.AsSpan().SequenceEqual(fragment.LastRecordId))
                    throw new InvalidDataException($"persistence.snapshot.fragment-record-range-mismatch:{SectionId}");
            }

            foreach (var material in decoded.Records)
            {
                if (previousRecord is { } previous && previous.CompareTo(material.RecordId) >= 0)
                    throw new InvalidDataException($"persistence.snapshot.fragment-record-order:{SectionId}");
                previousRecord = material.RecordId;

                TPayload payload;
                try
                {
                    payload = _fromStandardPayload(material.Payload)
                        ?? throw new InvalidDataException($"persistence.snapshot.partition-payload-null:{SectionId}");
                }
                catch (InvalidDataException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"persistence.snapshot.partition-payload-reconstruct:{SectionId}", ex);
                }

                restored.Add(new DomainRecordEnvelopeV1<TPayload>(
                    material.RecordId,
                    material.RecordSchema,
                    material.Revision,
                    material.CreatedStep,
                    material.RetiredStep,
                    material.DetailLevel,
                    material.LineageRef,
                    payload));
            }

            total = checked(total + fragment.ItemCount);
        }

        if (total != expectedHeader.ItemCount || total != checked((ulong)restored.Count))
            throw new InvalidDataException($"persistence.snapshot.partition-restored-count-mismatch:{SectionId}");
        if (expectedHeader.ItemCount == 0 && fragments.Count != 1)
            throw new InvalidDataException($"persistence.snapshot.partition-empty-fragment-count:{SectionId}");

        var partition = new DomainPartitionStateV1<TPayload>(_identity, restored);
        var recomputed = PartitionStateHeaderV1.CreateCanonical(
            partition,
            expectedHeader.Revision,
            expectedHeader.BasisStep,
            expectedHeader.DetailLevel,
            payload =>
            {
                var digest = _canonicalPayloadDigest(payload)
                    ?? throw new InvalidDataException($"persistence.snapshot.partition-payload-digest-null:{SectionId}");
                if (digest.Length != 32)
                    throw new InvalidDataException($"persistence.snapshot.partition-payload-digest-length:{SectionId}");
                return digest;
            });
        RequireSameHeader(expectedHeader, recomputed);

        return new SnapshotSectionSemanticVerificationV1(
            recomputed.ItemCount,
            recomputed.CanonicalDigest.ToArray());
    }

    private void ValidateCreatedSection(CanonicalSnapshotSectionMaterialV1 section)
    {
        if (section.LogicalItemCount != section.Fragments.Aggregate(0UL, static (sum, fragment) => checked(sum + fragment.ItemCount)))
            throw new InvalidDataException($"persistence.snapshot.fragment-item-count-mismatch:{SectionId}");
        if (section.LogicalContentDigest.Length != 32)
            throw new InvalidDataException($"persistence.snapshot.section-digest-invalid:{SectionId}");
        for (var i = 0; i < section.Fragments.Count; i++)
        {
            var fragment = section.Fragments[i];
            if (fragment.FragmentIndex != checked((uint)i) ||
                fragment.FragmentCount != checked((uint)section.Fragments.Count) ||
                fragment.FragmentPayload.Length > CanonicalSnapshotSectionValidationV1.HardMaxUncompressedBytes)
                throw new InvalidDataException($"persistence.snapshot-fragment-invalid:{SectionId}");
        }
    }

    private void RequireExpectedHeaderIdentity(PartitionStateHeaderV1 header)
    {
        if (header.PartitionId != _identity.PartitionId ||
            header.OwnerDomain != _identity.OwnerDomain ||
            header.Schema != _identity.PartitionSchema)
            throw new InvalidDataException($"persistence.snapshot.partition-verifier-header-identity:{SectionId}");
    }

    private static PartitionStateHeaderV1 CloneHeader(PartitionStateHeaderV1 header)
    {
        var identity = StandardDomainPartitionRegistry.Get(header.PartitionId.Value);
        return new PartitionStateHeaderV1(
            identity,
            header.Revision,
            header.BasisStep,
            header.DetailLevel,
            header.ItemCount,
            header.CanonicalDigest.ToArray());
    }

    private void RequireSameHeader(PartitionStateHeaderV1 expected, PartitionStateHeaderV1 actual)
    {
        if (expected.PartitionId != actual.PartitionId ||
            expected.OwnerDomain != actual.OwnerDomain ||
            expected.Schema != actual.Schema ||
            expected.Revision != actual.Revision ||
            expected.BasisStep != actual.BasisStep ||
            expected.DetailLevel != actual.DetailLevel ||
            expected.ItemCount != actual.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(expected.CanonicalDigest, actual.CanonicalDigest))
            throw new InvalidDataException($"persistence.snapshot.partition-restored-header-mismatch:{SectionId}");
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
