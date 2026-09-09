using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.WorldState;

internal static class DomainRecordSchemaMigrationRegistryInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        StandardDomainRecordSchemaMigrationRegistryV1.ValidateCanonicalContract();
        Require(StandardDomainRecordSchemaMigrationRegistryV1.Entries.Count == 1,
            "Only the exact Terrain migration may be registered at this checkpoint.");

        var terrain = StandardDomainPartitionRegistry.Get("spatial.terrain_geometry");
        var migration = StandardDomainRecordSchemaMigrationRegistryV1.Get(terrain.PartitionId.Value);
        Require(migration.SourceRecordSchema == terrain.RecordSchema,
            "Terrain migration source must remain the standard v1 record schema.");
        Require(migration.TargetRecordSchema == new SchemaRefV1(
                terrain.RecordSchema.SchemaId,
                new SchemaVersionV1(2, 0)),
            "Terrain migration target must be exact 2.0.");

        Require(StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedRecordSchema(
                terrain.PartitionId.Value,
                terrain.RecordSchema),
            "The standard Terrain v1 schema must remain allowed.");
        Require(StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedRecordSchema(
                terrain.PartitionId.Value,
                migration.TargetRecordSchema),
            "The registered Terrain v2 schema must be allowed.");
        Require(!StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedRecordSchema(
                terrain.PartitionId.Value,
                new SchemaRefV1(terrain.RecordSchema.SchemaId, new SchemaVersionV1(2, 1))),
            "Unregistered Terrain 2.1 must fail closed.");
        Require(!StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedRecordSchema(
                terrain.PartitionId.Value,
                new SchemaRefV1(terrain.RecordSchema.SchemaId, new SchemaVersionV1(3, 0))),
            "Unregistered Terrain 3.0 must fail closed.");

        var migratedTerrainIdentity = terrain with { RecordSchema = migration.TargetRecordSchema };
        Require(StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedPartitionIdentity(migratedTerrainIdentity),
            "Terrain identity with only the registered record-schema migration must be allowed.");
        Require(!StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedPartitionIdentity(
                migratedTerrainIdentity with { OwnerDomainRank = checked((ushort)(terrain.OwnerDomainRank + 1)) }),
            "Migration compatibility must not relax non-schema partition identity fields.");

        var physical = StandardDomainPartitionRegistry.Get("physical.presence");
        Require(!StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedRecordSchema(
                physical.PartitionId.Value,
                new SchemaRefV1(physical.RecordSchema.SchemaId, new SchemaVersionV1(2, 0))),
            "Unimplemented Physical v2 must remain rejected.");
        Require(!StandardDomainRecordSchemaMigrationRegistryV1.TryGet(physical.PartitionId.Value, out _),
            "Unimplemented partition migrations must not appear in the migration registry.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
