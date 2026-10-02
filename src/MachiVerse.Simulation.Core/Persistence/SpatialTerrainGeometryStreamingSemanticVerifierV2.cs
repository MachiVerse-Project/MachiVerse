using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Bounded-memory Terrain v2 phase-2 semantic verifier.
///
/// The recovered phase-1 identity/kind index proves internal Terrain target kinds. This verifier
/// re-reads fragment payloads one at a time, validates cross-partition references through the
/// recovered resolver, and feeds canonical record envelopes directly into the streaming partition
/// digest. No complete Terrain record set is reconstructed.
/// </summary>
public static class SpatialTerrainGeometryStreamingSemanticVerifierV2
{
    public static SnapshotSectionSemanticVerificationV1 Verify(
        PartitionStateHeaderV1 expectedHeader,
        IEnumerable<SnapshotSectionFragmentMaterialV1> fragments,
        SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2 recoveredIndex,
        IDomainRecordSchemaResolverV1? references = null)
    {
        ArgumentNullException.ThrowIfNull(expectedHeader);
        ArgumentNullException.ThrowIfNull(fragments);
        ArgumentNullException.ThrowIfNull(recoveredIndex);
        RequireSameHeader(expectedHeader, recoveredIndex.Header);

        var recomputed = PartitionStateHeaderStreamingV1.CreateCanonical(
            SpatialTerrainGeometryPartitionIdentityV2.Identity,
            expectedHeader.Revision,
            expectedHeader.BasisStep,
            expectedHeader.DetailLevel,
            expectedHeader.ItemCount,
            EnumerateRecoveredEnvelopes(expectedHeader, fragments, recoveredIndex),
            payload => SpatialTerrainGeometryPayloadCanonicalDigestV2.Compute(payload, references));

        return Result(expectedHeader, recomputed);
    }

    public static async Task<SnapshotSectionSemanticVerificationV1> VerifyAsync(
        PartitionStateHeaderV1 expectedHeader,
        IAsyncEnumerable<SnapshotSectionFragmentMaterialV1> fragments,
        SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2 recoveredIndex,
        IDomainRecordSchemaResolverV1? references = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedHeader);
        ArgumentNullException.ThrowIfNull(fragments);
        ArgumentNullException.ThrowIfNull(recoveredIndex);
        RequireSameHeader(expectedHeader, recoveredIndex.Header);

        var recomputed = await PartitionStateHeaderAsyncStreamingV1.CreateCanonicalAsync(
            SpatialTerrainGeometryPartitionIdentityV2.Identity,
            expectedHeader.Revision,
            expectedHeader.BasisStep,
            expectedHeader.DetailLevel,
            expectedHeader.ItemCount,
            EnumerateRecoveredEnvelopesAsync(
                expectedHeader,
                fragments,
                recoveredIndex,
                cancellationToken),
            payload => SpatialTerrainGeometryPayloadCanonicalDigestV2.Compute(payload, references),
            cancellationToken).ConfigureAwait(false);

        return Result(expectedHeader, recomputed);
    }

    private static SnapshotSectionSemanticVerificationV1 Result(
        PartitionStateHeaderV1 expectedHeader,
        PartitionStateHeaderV1 recomputed)
    {
        RequireSameHeader(expectedHeader, recomputed);
        return new SnapshotSectionSemanticVerificationV1(
            recomputed.ItemCount,
            recomputed.CanonicalDigest.ToArray());
    }

    private static IEnumerable<DomainRecordEnvelopeV1<SpatialTerrainGeometryPayloadV2>> EnumerateRecoveredEnvelopes(
        PartitionStateHeaderV1 expectedHeader,
        IEnumerable<SnapshotSectionFragmentMaterialV1> fragments,
        SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2 recoveredIndex)
    {
        uint expectedFragmentIndex = 0;
        uint? declaredFragmentCount = null;
        OpaqueId128? previous = null;
        ulong total = 0;

        foreach (var fragment in fragments)
        {
            foreach (var envelope in ProcessFragment(
                expectedHeader,
                fragment,
                recoveredIndex,
                ref expectedFragmentIndex,
                ref declaredFragmentCount,
                ref previous,
                ref total))
                yield return envelope;
        }

        Complete(expectedHeader, recoveredIndex, expectedFragmentIndex, declaredFragmentCount, total);
    }

    private static async IAsyncEnumerable<DomainRecordEnvelopeV1<SpatialTerrainGeometryPayloadV2>> EnumerateRecoveredEnvelopesAsync(
        PartitionStateHeaderV1 expectedHeader,
        IAsyncEnumerable<SnapshotSectionFragmentMaterialV1> fragments,
        SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2 recoveredIndex,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        uint expectedFragmentIndex = 0;
        uint? declaredFragmentCount = null;
        OpaqueId128? previous = null;
        ulong total = 0;

        await foreach (var fragment in fragments
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            foreach (var envelope in ProcessFragment(
                expectedHeader,
                fragment,
                recoveredIndex,
                ref expectedFragmentIndex,
                ref declaredFragmentCount,
                ref previous,
                ref total))
                yield return envelope;
        }

        Complete(expectedHeader, recoveredIndex, expectedFragmentIndex, declaredFragmentCount, total);
    }

    private static IEnumerable<DomainRecordEnvelopeV1<SpatialTerrainGeometryPayloadV2>> ProcessFragment(
        PartitionStateHeaderV1 expectedHeader,
        SnapshotSectionFragmentMaterialV1 fragment,
        SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2 recoveredIndex,
        ref uint expectedFragmentIndex,
        ref uint? declaredFragmentCount,
        ref OpaqueId128? previous,
        ref ulong total)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (!string.Equals(fragment.SectionId, SpatialTerrainGeometryRecordSchemaV2.PartitionId, StringComparison.Ordinal) ||
            fragment.FragmentCount == 0 ||
            fragment.FragmentIndex != expectedFragmentIndex)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-fragment-shape");

        declaredFragmentCount ??= fragment.FragmentCount;
        if (fragment.FragmentCount != declaredFragmentCount.Value ||
            fragment.FragmentIndex >= fragment.FragmentCount)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-fragment-shape");

        var decoded = SpatialTerrainGeometrySnapshotFragmentWireV2.Decode(fragment.FragmentPayload);
        RequireSameHeader(expectedHeader, decoded.Header);
        if (decoded.Records.Count != checked((int)fragment.ItemCount))
            throw new InvalidDataException("persistence.snapshot.terrain-v2-fragment-item-count");
        RequireFragmentRange(fragment, decoded.Records);

        var envelopes = new DomainRecordEnvelopeV1<SpatialTerrainGeometryPayloadV2>[decoded.Records.Count];
        for (var index = 0; index < decoded.Records.Count; index++)
        {
            var record = decoded.Records[index];
            if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                throw new InvalidDataException("persistence.snapshot.terrain-v2-fragment-record-order");
            previous = record.RecordId;

            if (!recoveredIndex.TryGetKind(record.RecordId, out var recoveredKind))
                throw new InvalidDataException("persistence.snapshot.terrain-v2-recovered-index-missing");
            var actualKind = record.Payload switch
            {
                SpatialTerrainBrickPayloadV2 => SpatialTerrainGeometryRecoveredRecordKindV2.Brick,
                SpatialTerrainRootPayloadV2 => SpatialTerrainGeometryRecoveredRecordKindV2.Root,
                _ => throw new InvalidDataException("persistence.snapshot.terrain-v2-record-kind"),
            };
            if (actualKind != recoveredKind)
                throw new InvalidDataException("persistence.snapshot.terrain-v2-recovered-kind-mismatch");

            total = checked(total + 1);
            envelopes[index] = new DomainRecordEnvelopeV1<SpatialTerrainGeometryPayloadV2>(
                record.RecordId,
                record.RecordSchema,
                record.Revision,
                record.CreatedStep,
                record.RetiredStep,
                record.DetailLevel,
                record.LineageRef,
                record.Payload);
        }

        expectedFragmentIndex = checked(expectedFragmentIndex + 1);
        return envelopes;
    }

    private static void Complete(
        PartitionStateHeaderV1 expectedHeader,
        SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2 recoveredIndex,
        uint expectedFragmentIndex,
        uint? declaredFragmentCount,
        ulong total)
    {
        if (declaredFragmentCount is null || expectedFragmentIndex != declaredFragmentCount.Value)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-section-fragment-missing");
        if (total != expectedHeader.ItemCount || total != recoveredIndex.ActualItemCount)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-restored-count");
        if (expectedHeader.ItemCount == 0 && declaredFragmentCount.Value != 1)
            throw new InvalidDataException("persistence.snapshot.terrain-v2-empty-fragment-count");
    }

    private static void RequireFragmentRange(
        SnapshotSectionFragmentMaterialV1 fragment,
        IReadOnlyList<SpatialTerrainGeometryRecordMaterialV2> records)
    {
        if (records.Count == 0)
        {
            if (fragment.FirstRecordId is not null || fragment.LastRecordId is not null)
                throw new InvalidDataException("persistence.snapshot.terrain-v2-fragment-empty-range");
            return;
        }

        var first = records[0].RecordId.ToBytes();
        var last = records[^1].RecordId.ToBytes();
        if (fragment.FirstRecordId is null || fragment.LastRecordId is null ||
            !first.AsSpan().SequenceEqual(fragment.FirstRecordId) ||
            !last.AsSpan().SequenceEqual(fragment.LastRecordId))
            throw new InvalidDataException("persistence.snapshot.terrain-v2-fragment-range");
    }

    private static void RequireSameHeader(
        PartitionStateHeaderV1 expected,
        PartitionStateHeaderV1 actual)
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
}
