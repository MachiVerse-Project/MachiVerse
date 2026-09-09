using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record DomainSnapshotRecordSchemaMigrationProviderRegistrationV1(
    string PartitionId,
    SchemaRefV1 RecordSchema,
    Func<IDomainPartitionSnapshotSectionProviderV1> CreateProvider);

/// <summary>
/// Persistence-side provider registry for explicitly registered record-schema migrations.
/// This registry is deliberately separate from the WorldState migration catalog so the latter
/// remains free of persistence dependencies. Every entry must correspond exactly to a registered
/// StandardDomainRecordSchemaMigrationRegistryV1 target.
/// </summary>
public static class DomainSnapshotRecordSchemaMigrationProviderRegistryV1
{
    private static readonly DomainSnapshotRecordSchemaMigrationProviderRegistrationV1[] CanonicalEntries =
    [
        Registration(
            SpatialTerrainGeometryRecordSchemaV2.PartitionId,
            SpatialTerrainGeometryRecordSchemaV2.RecordSchema,
            static () => new SpatialTerrainGeometrySnapshotSectionProviderV2()),
    ];

    private static readonly IReadOnlyDictionary<(string PartitionId, SchemaRefV1 RecordSchema), DomainSnapshotRecordSchemaMigrationProviderRegistrationV1> ByKey =
        CanonicalEntries.ToDictionary(
            static entry => (entry.PartitionId, entry.RecordSchema));

    public static IReadOnlyList<DomainSnapshotRecordSchemaMigrationProviderRegistrationV1> Entries { get; } =
        Array.AsReadOnly(CanonicalEntries);

    static DomainSnapshotRecordSchemaMigrationProviderRegistryV1()
    {
        ValidateCanonicalContract();
    }

    public static IDomainPartitionSnapshotSectionProviderV1 Resolve(
        IDomainPartitionSnapshotAuthorityV1 authority,
        IDomainPartitionSnapshotSectionProviderV1 standardProvider)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(standardProvider);
        var standard = StandardDomainPartitionRegistry.Get(authority.PartitionId.Value);
        if (!string.Equals(standardProvider.SectionId, standard.PartitionId.Value, StringComparison.Ordinal) ||
            standardProvider.SectionSchema != standard.PartitionSchema)
        {
            throw new InvalidDataException($"persistence.snapshot.partition-provider-schema-mismatch:{standard.PartitionId.Value}");
        }

        if (authority.RecordSchema == standard.RecordSchema)
            return standardProvider;

        if (!StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedRecordSchema(
                standard.PartitionId.Value,
                authority.RecordSchema) ||
            !ByKey.TryGetValue((standard.PartitionId.Value, authority.RecordSchema), out var registration))
        {
            throw new InvalidDataException($"persistence.snapshot.migrated-provider-unavailable:{standard.PartitionId.Value}");
        }

        var provider = registration.CreateProvider()
            ?? throw new InvalidDataException($"persistence.snapshot.migrated-provider-null:{standard.PartitionId.Value}");
        if (!string.Equals(provider.SectionId, standard.PartitionId.Value, StringComparison.Ordinal) ||
            provider.SectionSchema != standard.PartitionSchema)
        {
            throw new InvalidDataException($"persistence.snapshot.migrated-provider-schema:{standard.PartitionId.Value}");
        }
        return provider;
    }

    public static void ValidateCanonicalContract()
    {
        if (!CanonicalEntries.SequenceEqual(CanonicalEntries
                .OrderBy(static entry => entry.PartitionId, StringComparer.Ordinal)
                .ThenBy(static entry => entry.RecordSchema)))
            throw new InvalidOperationException("persistence.snapshot.migrated-provider-order");
        if (CanonicalEntries.Select(static entry => (entry.PartitionId, entry.RecordSchema)).Distinct().Count() != CanonicalEntries.Length)
            throw new InvalidOperationException("persistence.snapshot.migrated-provider-duplicate");

        foreach (var entry in CanonicalEntries)
        {
            var migration = StandardDomainRecordSchemaMigrationRegistryV1.Get(entry.PartitionId);
            if (entry.RecordSchema != migration.TargetRecordSchema)
                throw new InvalidOperationException($"persistence.snapshot.migrated-provider-registration:{entry.PartitionId}");
            var provider = entry.CreateProvider()
                ?? throw new InvalidOperationException($"persistence.snapshot.migrated-provider-null:{entry.PartitionId}");
            var standard = StandardDomainPartitionRegistry.Get(entry.PartitionId);
            if (!string.Equals(provider.SectionId, entry.PartitionId, StringComparison.Ordinal) ||
                provider.SectionSchema != standard.PartitionSchema)
                throw new InvalidOperationException($"persistence.snapshot.migrated-provider-contract:{entry.PartitionId}");
        }
    }

    private static DomainSnapshotRecordSchemaMigrationProviderRegistrationV1 Registration(
        string partitionId,
        SchemaRefV1 recordSchema,
        Func<IDomainPartitionSnapshotSectionProviderV1> createProvider)
        => new(partitionId, recordSchema, createProvider);
}
