using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record SpatialTerrainGeometryStagedRecoveryResultV2(
    DomainSnapshotStreamingRecoveredReferenceSourceV1 ScopeRegistry,
    SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2 Terrain,
    SnapshotSectionSemanticVerificationV1 SemanticVerification);

/// <summary>
/// Bounded-memory staged recovery proof for canonical Terrain v2.
///
/// Pass 1 reads the staged exact-103 Snapshot once and retains only recovered scope RecordIds plus
/// the Terrain RecordId/kind/root-closure index. Pass 2 re-reads staged chunks and feeds only Terrain
/// fragments into the async semantic rehash. No complete Terrain fragment list or record payload set
/// is retained. The scope_ref authority is recovered from the same staged Snapshot, not borrowed from
/// the pre-Snapshot in-memory world.
/// </summary>
public static class SpatialTerrainGeometryStagedRecoveryV2
{
    private const string ScopeRegistryPartitionId = "spatial.scope_registry";

    public static async Task<SpatialTerrainGeometryStagedRecoveryResultV2> VerifyAsync(
        SnapshotPhysicalPaths physical,
        IEnumerable<CanonicalSnapshotStreamingSectionV1> expectedSections,
        PartitionStateHeaderV1 expectedTerrainHeader,
        WorldStateV1? frozenState = null,
        RunningSnapshotCutV1? expectedCut = null,
        ReadOnlyMemory<byte> expectedSnapshotDigest = default,
        ReadOnlyMemory<byte> expectedPhysicalManifestDigest = default,
        IEnumerable<ISnapshotChunkCompressionDecoderV1>? compressionDecoders = null,
        DomainNestedSnapshotCodecRegistryV1? nestedCodecs = null,
        RequiredAddonSnapshotMetadataCodecRegistryV1? addonCodecs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(physical);
        ArgumentNullException.ThrowIfNull(expectedSections);
        ArgumentNullException.ThrowIfNull(expectedTerrainHeader);

        var expected = CanonicalSnapshotStreamingSectionValidationV1.ValidateStandardMetadata(
            expectedSections,
            frozenState);
        RequireTerrainExpectedHeader(expectedTerrainHeader);

        var manifest = await SnapshotPhysicalManifestStagingValidationV1.ValidateAsync(
            physical,
            addonCodecs,
            cancellationToken).ConfigureAwait(false);
        SnapshotPhysicalManifestStreamingAuthorityValidationV1.RequireExpectedAuthority(
            manifest,
            expected,
            frozenState,
            expectedCut,
            expectedSnapshotDigest,
            expectedPhysicalManifestDigest);

        var scopeBuilder = new DomainSnapshotStreamingRecoveredReferenceSourceV1.Builder(
            ScopeRegistryPartitionId,
            nestedCodecs);
        var terrainBuilder = new SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2.Builder();

        await foreach (var fragment in CanonicalSnapshotStagedFragmentStreamV1.ReadValidatedAsync(
            physical,
            manifest,
            compressionDecoders,
            cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(fragment.SectionId, ScopeRegistryPartitionId, StringComparison.Ordinal))
                scopeBuilder.Add(fragment);
            else if (string.Equals(fragment.SectionId, SpatialTerrainGeometryRecordSchemaV2.PartitionId, StringComparison.Ordinal))
                terrainBuilder.Add(fragment);
        }

        var scope = scopeBuilder.Complete();
        var terrain = terrainBuilder.Complete();
        RequireRecoveredSectionAuthority(manifest.Logical, scope.Header, scope.ActualItemCount, scope.RecordSchema);
        RequireRecoveredSectionAuthority(manifest.Logical, terrain.Header, terrain.ActualItemCount, terrain.RecordSchema);
        RequireSameHeader(expectedTerrainHeader, terrain.Header);

        var references = new TerrainRecoveryReferenceResolver(scope, terrain);
        var semantic = await SpatialTerrainGeometryStreamingSemanticVerifierV2.VerifyAsync(
            expectedTerrainHeader,
            ReadTerrainFragmentsAsync(
                physical,
                manifest,
                compressionDecoders,
                cancellationToken),
            terrain,
            references,
            cancellationToken).ConfigureAwait(false);

        var terrainLogical = FindLogicalSection(
            manifest.Logical,
            SpatialTerrainGeometryRecordSchemaV2.PartitionId);
        if (semantic.LogicalItemCount != terrainLogical.LogicalItemCount ||
            !CryptographicOperations.FixedTimeEquals(
                semantic.LogicalContentDigest,
                terrainLogical.LogicalContentDigest))
        {
            throw new InvalidDataException(
                "persistence.snapshot.terrain-v2-staged-semantic-authority-mismatch");
        }

        return new SpatialTerrainGeometryStagedRecoveryResultV2(scope, terrain, semantic);
    }

    private static async IAsyncEnumerable<SnapshotSectionFragmentMaterialV1> ReadTerrainFragmentsAsync(
        SnapshotPhysicalPaths physical,
        PhysicalSnapshotManifestMaterialV1 manifest,
        IEnumerable<ISnapshotChunkCompressionDecoderV1>? compressionDecoders,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var fragment in CanonicalSnapshotStagedFragmentStreamV1.ReadValidatedAsync(
            physical,
            manifest,
            compressionDecoders,
            cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(
                fragment.SectionId,
                SpatialTerrainGeometryRecordSchemaV2.PartitionId,
                StringComparison.Ordinal))
            {
                yield return fragment;
            }
        }
    }

    private static void RequireRecoveredSectionAuthority(
        LogicalSnapshotManifest logical,
        PartitionStateHeaderV1 header,
        ulong actualItemCount,
        SchemaRefV1 recordSchema)
    {
        var section = FindLogicalSection(logical, header.PartitionId.Value);
        var identity = StandardDomainPartitionRegistry.Get(header.PartitionId.Value);
        if (header.PartitionId != identity.PartitionId ||
            header.OwnerDomain != identity.OwnerDomain ||
            header.Schema != identity.PartitionSchema ||
            actualItemCount != header.ItemCount ||
            !StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedRecordSchema(
                identity.PartitionId.Value,
                recordSchema))
        {
            throw new InvalidDataException(
                $"persistence.snapshot.recovered-reference-section-authority:{header.PartitionId.Value}");
        }

        if (!string.Equals(section.SchemaId, header.Schema.SchemaId.Value, StringComparison.Ordinal) ||
            section.SchemaMajor != header.Schema.Version.Major ||
            section.SchemaMinor != header.Schema.Version.Minor ||
            section.LogicalItemCount != actualItemCount ||
            !CryptographicOperations.FixedTimeEquals(section.LogicalContentDigest, header.CanonicalDigest))
        {
            throw new InvalidDataException(
                $"persistence.snapshot.recovered-reference-section-authority:{header.PartitionId.Value}");
        }
    }

    private static LogicalSnapshotSection FindLogicalSection(
        LogicalSnapshotManifest logical,
        string sectionId)
    {
        var match = logical.Sections.SingleOrDefault(section =>
            string.Equals(section.SectionId, sectionId, StringComparison.Ordinal));
        return match
            ?? throw new InvalidDataException($"persistence.snapshot-section-missing:{sectionId}");
    }

    private static void RequireTerrainExpectedHeader(PartitionStateHeaderV1 header)
    {
        var identity = StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId);
        if (header.PartitionId != identity.PartitionId ||
            header.OwnerDomain != identity.OwnerDomain ||
            header.Schema != identity.PartitionSchema)
        {
            throw new InvalidDataException("persistence.snapshot.terrain-v2-verifier-header-identity");
        }
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
        {
            throw new InvalidDataException("persistence.snapshot.terrain-v2-restored-header");
        }
    }

    private sealed class TerrainRecoveryReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly DomainSnapshotStreamingRecoveredReferenceSourceV1 _scope;
        private readonly SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2 _terrain;

        public TerrainRecoveryReferenceResolver(
            DomainSnapshotStreamingRecoveredReferenceSourceV1 scope,
            SpatialTerrainGeometryStreamingRecoveredReferenceSourceV2 terrain)
        {
            _scope = scope ?? throw new ArgumentNullException(nameof(scope));
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            if (!string.Equals(_scope.PartitionId.Value, ScopeRegistryPartitionId, StringComparison.Ordinal))
                throw new InvalidDataException("persistence.snapshot.terrain-v2-scope-reference-source");
        }

        public bool Exists(PartitionRecordRefV1 reference)
            => TryGetRecordSchema(reference, out _);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            if (string.Equals(reference.PartitionId.Value, ScopeRegistryPartitionId, StringComparison.Ordinal) &&
                Contains(_scope.RecordIdsCanonical, reference.RecordId))
            {
                schema = _scope.RecordSchema;
                return true;
            }

            if (string.Equals(
                    reference.PartitionId.Value,
                    SpatialTerrainGeometryRecordSchemaV2.PartitionId,
                    StringComparison.Ordinal) &&
                _terrain.TryGetKind(reference.RecordId, out _))
            {
                schema = _terrain.RecordSchema;
                return true;
            }

            schema = default;
            return false;
        }

        private static bool Contains(IReadOnlyList<OpaqueId128> values, OpaqueId128 target)
        {
            var low = 0;
            var high = values.Count - 1;
            while (low <= high)
            {
                var mid = low + ((high - low) >> 1);
                var comparison = values[mid].CompareTo(target);
                if (comparison == 0) return true;
                if (comparison < 0) low = mid + 1;
                else high = mid - 1;
            }
            return false;
        }
    }
}
