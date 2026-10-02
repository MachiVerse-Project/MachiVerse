using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class DomainSnapshotCompactReferenceResolverSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        ProveReducedAuthorityParity();
        ProveMigratedSchemaParity();
    }

    private static void ProveReducedAuthorityParity()
    {
        var resident = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(8);
        var authorities = Qa04ReducedWorldTypedAuthorityV1.BindAll97(resident);
        var legacy = new DomainSnapshotReferenceResolverV1(authorities.CanonicalAuthorities);
        var compact = new DomainSnapshotCompactReferenceResolverV1(authorities.CanonicalAuthorities);

        Require(compact.RecordCount == legacy.RecordCount && compact.RecordCount == 8,
            "Compact resolver must preserve the exact reduced all-97 record count.");

        foreach (var authority in authorities.CanonicalAuthorities)
        {
            foreach (var recordId in authority.RecordIdsCanonical)
            {
                var reference = new PartitionRecordRefV1(authority.PartitionId, recordId);
                var legacyFound = legacy.TryGetRecordSchema(reference, out var legacySchema);
                var compactFound = compact.TryGetRecordSchema(reference, out var compactSchema);
                Require(legacyFound && compactFound && legacySchema == compactSchema,
                    "Compact resolver must preserve actual all-97 reference/schema lookup semantics.");
            }

            var missing = new PartitionRecordRefV1(
                authority.PartitionId,
                OpaqueId128.Parse("ffffffffffffffffffffffffffffffff"));
            Require(legacy.Exists(missing) == compact.Exists(missing),
                "Compact resolver must preserve missing-reference lookup semantics.");
        }
    }

    private static void ProveMigratedSchemaParity()
    {
        var terrainId = SpatialTerrainGeometryRecordSchemaV2.PartitionId;
        var terrainRecordId = OpaqueId128.Parse("00000000000000000000000000abcdef");
        var sources = StandardDomainPartitionRegistry.Entries
            .Select(identity => (IDomainPartitionSnapshotReferenceSourceV1)new Source(
                identity.PartitionId,
                string.Equals(identity.PartitionId.Value, terrainId, StringComparison.Ordinal)
                    ? SpatialTerrainGeometryRecordSchemaV2.RecordSchema
                    : identity.RecordSchema,
                string.Equals(identity.PartitionId.Value, terrainId, StringComparison.Ordinal)
                    ? new[] { terrainRecordId }
                    : Array.Empty<OpaqueId128>()))
            .ToArray();

        var legacy = new DomainSnapshotReferenceResolverV1(sources);
        var compact = new DomainSnapshotCompactReferenceResolverV1(sources);
        var terrainRef = new PartitionRecordRefV1(terrainId, terrainRecordId);

        Require(legacy.TryGetRecordSchema(terrainRef, out var legacySchema) &&
                compact.TryGetRecordSchema(terrainRef, out var compactSchema) &&
                legacySchema == SpatialTerrainGeometryRecordSchemaV2.RecordSchema &&
                compactSchema == legacySchema,
            "Compact resolver must preserve explicitly registered Terrain v2 record-schema migration lookup.");
        Require(compact.RecordCount == legacy.RecordCount && compact.RecordCount == 1,
            "Compact resolver must preserve migrated-source record count.");
    }

    private sealed class Source : IDomainPartitionSnapshotReferenceSourceV1
    {
        public Source(StableToken partitionId, SchemaRefV1 recordSchema, IReadOnlyList<OpaqueId128> ids)
        {
            PartitionId = partitionId;
            RecordSchema = recordSchema;
            RecordIdsCanonical = ids;
        }

        public StableToken PartitionId { get; }
        public SchemaRefV1 RecordSchema { get; }
        public ulong ActualItemCount => checked((ulong)RecordIdsCanonical.Count);
        public IReadOnlyList<OpaqueId128> RecordIdsCanonical { get; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
