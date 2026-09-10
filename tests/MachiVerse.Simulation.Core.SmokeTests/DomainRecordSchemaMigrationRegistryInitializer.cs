using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.WorldState;

internal static class DomainRecordSchemaMigrationRegistryInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        StandardDomainRecordSchemaMigrationRegistryV1.ValidateCanonicalContract();
        Require(StandardDomainRecordSchemaMigrationRegistryV1.Entries.Count == 2,
            "Only the exact Physical occupancy and Terrain migrations may be registered at this checkpoint.");

        VerifyV1ToV2Migration("physical.occupancy", "Physical occupancy");
        VerifyV1ToV2Migration("spatial.terrain_geometry", "Terrain");

        var terrain = StandardDomainPartitionRegistry.Get("spatial.terrain_geometry");
        var terrainMigration = StandardDomainRecordSchemaMigrationRegistryV1.Get(terrain.PartitionId.Value);
        var migratedTerrainIdentity = terrain with { RecordSchema = terrainMigration.TargetRecordSchema };
        Require(StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedPartitionIdentity(migratedTerrainIdentity),
            "Terrain identity with only the registered record-schema migration must be allowed.");
        Require(!StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedPartitionIdentity(
                migratedTerrainIdentity with { OwnerDomainRank = checked((ushort)(terrain.OwnerDomainRank + 1)) }),
            "Migration compatibility must not relax non-schema partition identity fields.");

        var occupancy = StandardDomainPartitionRegistry.Get("physical.occupancy");
        var occupancyMigration = StandardDomainRecordSchemaMigrationRegistryV1.Get(occupancy.PartitionId.Value);
        var migratedOccupancyIdentity = occupancy with { RecordSchema = occupancyMigration.TargetRecordSchema };
        Require(StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedPartitionIdentity(migratedOccupancyIdentity),
            "Physical occupancy identity with only the registered record-schema migration must be allowed.");
        Require(!StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedPartitionIdentity(
                migratedOccupancyIdentity with { OwnerDomainRank = checked((ushort)(occupancy.OwnerDomainRank + 1)) }),
            "Physical occupancy migration compatibility must not relax non-schema partition identity fields.");

        var presence = StandardDomainPartitionRegistry.Get("physical.presence");
        Require(!StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedRecordSchema(
                presence.PartitionId.Value,
                new SchemaRefV1(presence.RecordSchema.SchemaId, new SchemaVersionV1(2, 0))),
            "Unregistered Physical presence v2 must remain rejected.");
        Require(!StandardDomainRecordSchemaMigrationRegistryV1.TryGet(presence.PartitionId.Value, out _),
            "Unimplemented partition migrations must not appear in the migration registry.");
    }

    private static void VerifyV1ToV2Migration(string partitionId, string displayName)
    {
        var standard = StandardDomainPartitionRegistry.Get(partitionId);
        var migration = StandardDomainRecordSchemaMigrationRegistryV1.Get(partitionId);
        Require(migration.SourceRecordSchema == standard.RecordSchema,
            $"{displayName} migration source must remain the standard v1 record schema.");
        Require(migration.TargetRecordSchema == new SchemaRefV1(
                standard.RecordSchema.SchemaId,
                new SchemaVersionV1(2, 0)),
            $"{displayName} migration target must be exact 2.0.");
        Require(StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedRecordSchema(partitionId, standard.RecordSchema),
            $"The standard {displayName} v1 schema must remain allowed.");
        Require(StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedRecordSchema(partitionId, migration.TargetRecordSchema),
            $"The registered {displayName} v2 schema must be allowed.");
        Require(!StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedRecordSchema(
                partitionId,
                new SchemaRefV1(standard.RecordSchema.SchemaId, new SchemaVersionV1(2, 1))),
            $"Unregistered {displayName} 2.1 must fail closed.");
        Require(!StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedRecordSchema(
                partitionId,
                new SchemaRefV1(standard.RecordSchema.SchemaId, new SchemaVersionV1(3, 0))),
            $"Unregistered {displayName} 3.0 must fail closed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
