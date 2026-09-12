using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.WorldState;

public sealed record DomainRecordSchemaMigrationRegistrationV1(
    StableToken PartitionId,
    SchemaRefV1 SourceRecordSchema,
    SchemaRefV1 TargetRecordSchema);

/// <summary>
/// Explicit standard record-schema migration catalog. Registration means the exact target schema
/// is known and may be recognized by migration-aware validation; it does not by itself flip
/// StandardDomainPartitionRegistry or activate a production migration.
/// </summary>
public static class StandardDomainRecordSchemaMigrationRegistryV1
{
    private static readonly DomainRecordSchemaMigrationRegistrationV1[] CanonicalEntries =
    [
        Registration("infrastructure.network_topology", 2, 0),
        Registration("physical.occupancy", 2, 0),
        Registration("society.market_transaction", 2, 0),
        Registration("spatial.terrain_geometry", 2, 0),
    ];

    private static readonly IReadOnlyDictionary<string, DomainRecordSchemaMigrationRegistrationV1> ByPartition =
        CanonicalEntries.ToDictionary(static entry => entry.PartitionId.Value, StringComparer.Ordinal);

    public static IReadOnlyList<DomainRecordSchemaMigrationRegistrationV1> Entries { get; } =
        Array.AsReadOnly(CanonicalEntries);

    static StandardDomainRecordSchemaMigrationRegistryV1()
    {
        ValidateCanonicalContract();
    }

    public static bool TryGet(string partitionId, out DomainRecordSchemaMigrationRegistrationV1? registration)
        => ByPartition.TryGetValue(partitionId, out registration);

    public static DomainRecordSchemaMigrationRegistrationV1 Get(string partitionId)
        => ByPartition.TryGetValue(partitionId, out var registration)
            ? registration
            : throw new KeyNotFoundException($"No registered standard record-schema migration: {partitionId}");

    public static bool IsAllowedRecordSchema(string partitionId, SchemaRefV1 schema)
    {
        var standard = StandardDomainPartitionRegistry.Get(partitionId);
        if (schema == standard.RecordSchema) return true;
        return ByPartition.TryGetValue(partitionId, out var migration) && schema == migration.TargetRecordSchema;
    }

    public static bool IsAllowedPartitionIdentity(DomainPartitionIdentityV1 identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var standard = StandardDomainPartitionRegistry.Get(identity.PartitionId.Value);
        return identity.PartitionId == standard.PartitionId &&
               identity.OwnerDomain == standard.OwnerDomain &&
               identity.OwnerDomainRank == standard.OwnerDomainRank &&
               identity.PartitionSchema == standard.PartitionSchema &&
               identity.PrimaryKeyKind == standard.PrimaryKeyKind &&
               identity.PersistenceClass == standard.PersistenceClass &&
               identity.CanonicalOrder == standard.CanonicalOrder &&
               IsAllowedRecordSchema(identity.PartitionId.Value, identity.RecordSchema);
    }

    public static void ValidateCanonicalContract()
    {
        if (!CanonicalEntries.SequenceEqual(
                CanonicalEntries.OrderBy(static entry => entry.PartitionId.Value, StringComparer.Ordinal)))
            throw new InvalidOperationException("domain.record-schema-migration-order");
        if (CanonicalEntries.Select(static entry => entry.PartitionId.Value).Distinct(StringComparer.Ordinal).Count() != CanonicalEntries.Length)
            throw new InvalidOperationException("domain.record-schema-migration-duplicate");

        foreach (var entry in CanonicalEntries)
        {
            var standard = StandardDomainPartitionRegistry.Get(entry.PartitionId.Value);
            if (entry.SourceRecordSchema != standard.RecordSchema)
                throw new InvalidOperationException($"domain.record-schema-migration-source:{entry.PartitionId.Value}");
            if (entry.TargetRecordSchema.SchemaId != standard.RecordSchema.SchemaId ||
                entry.TargetRecordSchema.Version.Major <= standard.RecordSchema.Version.Major ||
                entry.TargetRecordSchema.Version.Minor != 0)
                throw new InvalidOperationException($"domain.record-schema-migration-target:{entry.PartitionId.Value}");
        }

        RequireV1ToV2("infrastructure.network_topology", "domain.record-schema-migration-infrastructure-network-contract");
        RequireV1ToV2("physical.occupancy", "domain.record-schema-migration-physical-occupancy-contract");
        RequireV1ToV2("society.market_transaction", "domain.record-schema-migration-society-market-contract");
        RequireV1ToV2("spatial.terrain_geometry", "domain.record-schema-migration-terrain-contract");
    }

    private static void RequireV1ToV2(string partitionId, string failureCode)
    {
        var migration = Get(partitionId);
        if (migration.SourceRecordSchema.Version != new SchemaVersionV1(1, 0) ||
            migration.TargetRecordSchema.Version != new SchemaVersionV1(2, 0))
            throw new InvalidOperationException(failureCode);
    }

    private static DomainRecordSchemaMigrationRegistrationV1 Registration(
        string partitionId,
        ushort targetMajor,
        ushort targetMinor)
    {
        var standard = StandardDomainPartitionRegistry.Get(partitionId);
        return new DomainRecordSchemaMigrationRegistrationV1(
            standard.PartitionId,
            standard.RecordSchema,
            new SchemaRefV1(standard.RecordSchema.SchemaId, new SchemaVersionV1(targetMajor, targetMinor)));
    }
}
