using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class DomainSnapshotReferenceSchemaMigrationInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        VerifyResolverPreservesRegisteredInfrastructureV2();
        VerifyResolverPreservesRegisteredPhysicalOccupancyV2();
        VerifyResolverPreservesRegisteredMarketV2();
        VerifyResolverPreservesRegisteredTerrainV2();
        VerifyResolverRejectsUnregisteredSchema();
        VerifyCrossPartitionReferenceValidationUsesMigrationRegistry();
    }

    private static void VerifyResolverPreservesRegisteredInfrastructureV2()
    {
        var topologyId = Id("00000000000000000000000000024501");
        var migration = StandardDomainRecordSchemaMigrationRegistryV1.Get("infrastructure.network_topology");
        var sources = StandardDomainPartitionRegistry.Entries
            .Select(identity => (IDomainPartitionSnapshotReferenceSourceV1)new FakeSource(
                identity.PartitionId,
                identity.PartitionId.Value == "infrastructure.network_topology"
                    ? migration.TargetRecordSchema
                    : identity.RecordSchema,
                identity.PartitionId.Value == "infrastructure.network_topology"
                    ? new[] { topologyId }
                    : Array.Empty<OpaqueId128>()))
            .ToArray();

        var resolver = new DomainSnapshotReferenceResolverV1(sources);
        var reference = new PartitionRecordRefV1("infrastructure.network_topology", topologyId);
        Require(resolver.Exists(reference), "all-97 resolver must preserve migrated Infrastructure topology record existence.");
        Require(resolver.TryGetRecordSchema(reference, out var actual) && actual == migration.TargetRecordSchema,
            "all-97 resolver must preserve the actual registered Infrastructure topology v2 schema rather than rewriting it to v1.");
    }

    private static void VerifyResolverPreservesRegisteredPhysicalOccupancyV2()
    {
        var occupancyId = Id("00000000000000000000000000025001");
        var migration = StandardDomainRecordSchemaMigrationRegistryV1.Get("physical.occupancy");
        var sources = StandardDomainPartitionRegistry.Entries
            .Select(identity => (IDomainPartitionSnapshotReferenceSourceV1)new FakeSource(
                identity.PartitionId,
                identity.PartitionId.Value == "physical.occupancy"
                    ? migration.TargetRecordSchema
                    : identity.RecordSchema,
                identity.PartitionId.Value == "physical.occupancy"
                    ? new[] { occupancyId }
                    : Array.Empty<OpaqueId128>()))
            .ToArray();

        var resolver = new DomainSnapshotReferenceResolverV1(sources);
        var reference = new PartitionRecordRefV1("physical.occupancy", occupancyId);
        Require(resolver.Exists(reference), "all-97 resolver must preserve migrated Physical occupancy record existence.");
        Require(resolver.TryGetRecordSchema(reference, out var actual) && actual == migration.TargetRecordSchema,
            "all-97 resolver must preserve the actual registered Physical occupancy v2 schema rather than rewriting it to v1.");
    }

    private static void VerifyResolverPreservesRegisteredMarketV2()
    {
        var marketId = Id("00000000000000000000000000025501");
        var migration = StandardDomainRecordSchemaMigrationRegistryV1.Get("society.market_transaction");
        var sources = StandardDomainPartitionRegistry.Entries
            .Select(identity => (IDomainPartitionSnapshotReferenceSourceV1)new FakeSource(
                identity.PartitionId,
                identity.PartitionId.Value == "society.market_transaction"
                    ? migration.TargetRecordSchema
                    : identity.RecordSchema,
                identity.PartitionId.Value == "society.market_transaction"
                    ? new[] { marketId }
                    : Array.Empty<OpaqueId128>()))
            .ToArray();

        var resolver = new DomainSnapshotReferenceResolverV1(sources);
        var reference = new PartitionRecordRefV1("society.market_transaction", marketId);
        Require(resolver.Exists(reference), "all-97 resolver must preserve migrated Society market record existence.");
        Require(resolver.TryGetRecordSchema(reference, out var actual) && actual == migration.TargetRecordSchema,
            "all-97 resolver must preserve the actual registered Society market v2 schema rather than rewriting it to v1.");
    }

    private static void VerifyResolverPreservesRegisteredTerrainV2()
    {
        var terrainId = Id("00000000000000000000000000026001");
        var terrainMigration = StandardDomainRecordSchemaMigrationRegistryV1.Get("spatial.terrain_geometry");
        var sources = StandardDomainPartitionRegistry.Entries
            .Select(identity => (IDomainPartitionSnapshotReferenceSourceV1)new FakeSource(
                identity.PartitionId,
                identity.PartitionId.Value == "spatial.terrain_geometry"
                    ? terrainMigration.TargetRecordSchema
                    : identity.RecordSchema,
                identity.PartitionId.Value == "spatial.terrain_geometry"
                    ? new[] { terrainId }
                    : Array.Empty<OpaqueId128>()))
            .ToArray();

        var resolver = new DomainSnapshotReferenceResolverV1(sources);
        var reference = new PartitionRecordRefV1("spatial.terrain_geometry", terrainId);
        Require(resolver.Exists(reference), "all-97 resolver must preserve migrated Terrain record existence.");
        Require(resolver.TryGetRecordSchema(reference, out var actual) && actual == terrainMigration.TargetRecordSchema,
            "all-97 resolver must preserve the actual registered Terrain v2 schema rather than rewriting it to v1.");
    }

    private static void VerifyResolverRejectsUnregisteredSchema()
    {
        var physical = StandardDomainPartitionRegistry.Get("physical.presence");
        var badSchema = new SchemaRefV1(physical.RecordSchema.SchemaId, new SchemaVersionV1(2, 0));
        var sources = StandardDomainPartitionRegistry.Entries
            .Select(identity => (IDomainPartitionSnapshotReferenceSourceV1)new FakeSource(
                identity.PartitionId,
                identity.PartitionId == physical.PartitionId ? badSchema : identity.RecordSchema,
                Array.Empty<OpaqueId128>()))
            .ToArray();

        ExpectReject(
            () => new DomainSnapshotReferenceResolverV1(sources),
            "persistence.snapshot.reference-source-schema-mismatch:physical.presence");
    }

    private static void VerifyCrossPartitionReferenceValidationUsesMigrationRegistry()
    {
        var terrainId = Id("00000000000000000000000000026002");
        var terrainRef = new PartitionRecordRefV1("spatial.terrain_geometry", terrainId);
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["scope_class"] = "test_scope",
            ["geometry_ref"] = terrainRef,
            ["active_from"] = 0UL,
            ["scope_flags"] = 0u,
        };
        var migration = StandardDomainRecordSchemaMigrationRegistryV1.Get("spatial.terrain_geometry");
        var validator = new StandardDomainPayloadCodecValidatorV1();

        validator.Validate(
            "spatial.scope_registry",
            payload,
            new SingleReferenceResolver(terrainRef, migration.TargetRecordSchema));

        ExpectReject(
            () => validator.Validate(
                "spatial.scope_registry",
                payload,
                new SingleReferenceResolver(
                    terrainRef,
                    new SchemaRefV1(migration.TargetRecordSchema.SchemaId, new SchemaVersionV1(2, 1)))),
            "domain.payload.reference-schema:spatial.scope_registry:geometry_ref");
    }

    private sealed class FakeSource(
        StableToken partitionId,
        SchemaRefV1 recordSchema,
        IReadOnlyList<OpaqueId128> recordIds) : IDomainPartitionSnapshotReferenceSourceV1
    {
        public StableToken PartitionId { get; } = partitionId;
        public SchemaRefV1 RecordSchema { get; } = recordSchema;
        public ulong ActualItemCount => checked((ulong)RecordIdsCanonical.Count);
        public IReadOnlyList<OpaqueId128> RecordIdsCanonical { get; } = recordIds;
    }

    private sealed class SingleReferenceResolver(
        PartitionRecordRefV1 expected,
        SchemaRefV1 schema) : IDomainRecordSchemaResolverV1
    {
        public bool Exists(PartitionRecordRefV1 reference) => reference == expected;

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 actual)
        {
            if (reference == expected)
            {
                actual = schema;
                return true;
            }
            actual = default;
            return false;
        }
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void ExpectReject(Action action, string code)
    {
        try
        {
            action();
            throw new InvalidOperationException($"Expected rejection containing '{code}'.");
        }
        catch (InvalidDataException ex) when (ex.Message.Contains(code, StringComparison.Ordinal))
        {
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
