using System.Collections.ObjectModel;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.InfrastructureInformation;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Domains.SocietyEconomy;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

/// <summary>
/// Untyped actual-record source used to build the frozen Snapshot reference index. Implementations
/// must enumerate real DomainPartitionStateV1 records or structurally decoded Snapshot records;
/// header counts alone are not accepted as material.
/// </summary>
public interface IDomainPartitionSnapshotReferenceSourceV1
{
    StableToken PartitionId { get; }
    SchemaRefV1 RecordSchema { get; }
    ulong ActualItemCount { get; }
    IReadOnlyList<OpaqueId128> RecordIdsCanonical { get; }
}

public sealed class DomainPartitionSnapshotReferenceSourceV1<TPayload> : IDomainPartitionSnapshotReferenceSourceV1
{
    public DomainPartitionSnapshotReferenceSourceV1(DomainPartitionSnapshotAuthorityV1<TPayload> authority)
    {
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
        Authority.VerifyBoundAuthority();
        var ids = Authority.Partition.RecordsCanonical.Select(static record => record.RecordId).ToArray();
        if (checked((ulong)ids.Length) != Authority.ActualItemCount)
            throw new InvalidDataException($"persistence.snapshot.reference-source-count-mismatch:{Authority.PartitionId.Value}");
        for (var i = 1; i < ids.Length; i++)
        {
            if (ids[i - 1].CompareTo(ids[i]) >= 0)
                throw new InvalidDataException($"persistence.snapshot.reference-source-order:{Authority.PartitionId.Value}");
        }
        RecordIdsCanonical = Array.AsReadOnly(ids);
    }

    public DomainPartitionSnapshotAuthorityV1<TPayload> Authority { get; }
    public StableToken PartitionId => Authority.PartitionId;
    public SchemaRefV1 RecordSchema => Authority.Identity.RecordSchema;
    public ulong ActualItemCount => Authority.ActualItemCount;
    public IReadOnlyList<OpaqueId128> RecordIdsCanonical { get; }
}

/// <summary>
/// Structure-only recovered source for one standard-v1 Domain section. It validates fragment/header/range/order
/// and decodes record identities without requiring the cross-partition reference resolver yet.
/// Full payload semantic/reference validation runs in the second recovery phase.
/// </summary>
public sealed class DomainSnapshotRecoveredReferenceSourceV1 : IDomainPartitionSnapshotReferenceSourceV1
{
    public DomainSnapshotRecoveredReferenceSourceV1(
        string partitionId,
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments,
        DomainNestedSnapshotCodecRegistryV1? nestedCodecs = null)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        var identity = StandardDomainPartitionRegistry.Get(partitionId);
        if (fragments.Count == 0)
            throw new InvalidDataException($"persistence.snapshot.recovered-reference-fragment-missing:{partitionId}");

        var codecs = nestedCodecs ?? StandardDomainNestedSnapshotCodecRegistryV1.Default;
        var ids = new List<OpaqueId128>();
        PartitionStateHeaderV1? repeatedHeader = null;
        OpaqueId128? previous = null;
        ulong total = 0;

        for (var i = 0; i < fragments.Count; i++)
        {
            var fragment = fragments[i] ?? throw new InvalidDataException($"persistence.snapshot.recovered-reference-fragment-null:{partitionId}");
            if (!string.Equals(fragment.SectionId, partitionId, StringComparison.Ordinal) ||
                fragment.FragmentIndex != checked((uint)i) ||
                fragment.FragmentCount != checked((uint)fragments.Count))
                throw new InvalidDataException($"persistence.snapshot.recovered-reference-fragment-shape:{partitionId}");

            var decoded = DomainPartitionSnapshotWireCodecV1.DecodeFragment(
                partitionId,
                fragment.FragmentPayload,
                codecs);
            if (repeatedHeader is null)
                repeatedHeader = decoded.Header;
            else
                RequireSameHeader(repeatedHeader, decoded.Header, partitionId);

            if (decoded.Records.Count != checked((int)fragment.ItemCount))
                throw new InvalidDataException($"persistence.snapshot.recovered-reference-item-count:{partitionId}");

            if (decoded.Records.Count == 0)
            {
                if (fragment.FirstRecordId is not null || fragment.LastRecordId is not null)
                    throw new InvalidDataException($"persistence.snapshot.recovered-reference-range-empty:{partitionId}");
            }
            else
            {
                var first = decoded.Records[0].RecordId.ToBytes();
                var last = decoded.Records[^1].RecordId.ToBytes();
                if (fragment.FirstRecordId is null || fragment.LastRecordId is null ||
                    !first.AsSpan().SequenceEqual(fragment.FirstRecordId) ||
                    !last.AsSpan().SequenceEqual(fragment.LastRecordId))
                    throw new InvalidDataException($"persistence.snapshot.recovered-reference-range:{partitionId}");
            }

            foreach (var record in decoded.Records)
            {
                if (record.RecordSchema != identity.RecordSchema)
                    throw new InvalidDataException($"persistence.snapshot.recovered-reference-schema:{partitionId}");
                if (previous is { } prior && prior.CompareTo(record.RecordId) >= 0)
                    throw new InvalidDataException($"persistence.snapshot.recovered-reference-order:{partitionId}");
                previous = record.RecordId;
                ids.Add(record.RecordId);
            }
            total = checked(total + fragment.ItemCount);
        }

        Header = repeatedHeader
            ?? throw new InvalidDataException($"persistence.snapshot.recovered-reference-header-missing:{partitionId}");
        if (Header.PartitionId != identity.PartitionId ||
            Header.OwnerDomain != identity.OwnerDomain ||
            Header.Schema != identity.PartitionSchema)
            throw new InvalidDataException($"persistence.snapshot.recovered-reference-header-identity:{partitionId}");
        if (total != Header.ItemCount || total != checked((ulong)ids.Count))
            throw new InvalidDataException($"persistence.snapshot.recovered-reference-total-count:{partitionId}");
        if (Header.ItemCount == 0 && fragments.Count != 1)
            throw new InvalidDataException($"persistence.snapshot.recovered-reference-empty-fragment-count:{partitionId}");

        PartitionId = identity.PartitionId;
        RecordSchema = identity.RecordSchema;
        ActualItemCount = total;
        RecordIdsCanonical = Array.AsReadOnly(ids.ToArray());
    }

    public StableToken PartitionId { get; }
    public SchemaRefV1 RecordSchema { get; }
    public ulong ActualItemCount { get; }
    public IReadOnlyList<OpaqueId128> RecordIdsCanonical { get; }
    public PartitionStateHeaderV1 Header { get; }

    private static void RequireSameHeader(
        PartitionStateHeaderV1 expected,
        PartitionStateHeaderV1 actual,
        string partitionId)
    {
        if (expected.PartitionId != actual.PartitionId ||
            expected.OwnerDomain != actual.OwnerDomain ||
            expected.Schema != actual.Schema ||
            expected.Revision != actual.Revision ||
            expected.BasisStep != actual.BasisStep ||
            expected.DetailLevel != actual.DetailLevel ||
            expected.ItemCount != actual.ItemCount ||
            !CryptographicOperations.FixedTimeEquals(expected.CanonicalDigest, actual.CanonicalDigest))
            throw new InvalidDataException($"persistence.snapshot.recovered-reference-header-mismatch:{partitionId}");
    }
}

/// <summary>
/// Frozen all-97 actual-record resolver used by Snapshot production/recovery semantic validation.
/// Construction fails unless every standard partition contributes exactly one actual record source.
/// Registered record-schema migrations are preserved per source; unregistered versions fail closed.
/// </summary>
public sealed class DomainSnapshotReferenceResolverV1 : IDomainRecordSchemaResolverV1
{
    private readonly IReadOnlyDictionary<(string PartitionId, OpaqueId128 RecordId), SchemaRefV1> _records;

    public DomainSnapshotReferenceResolverV1(IEnumerable<IDomainPartitionSnapshotReferenceSourceV1> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var materialized = sources.ToArray();
        if (materialized.Length != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("persistence.snapshot.reference-source-count-not-97");

        var sourceByPartition = new Dictionary<string, IDomainPartitionSnapshotReferenceSourceV1>(StringComparer.Ordinal);
        foreach (var source in materialized)
        {
            ArgumentNullException.ThrowIfNull(source);
            var identity = StandardDomainPartitionRegistry.Get(source.PartitionId.Value);
            if (!StandardDomainRecordSchemaMigrationRegistryV1.IsAllowedRecordSchema(
                    identity.PartitionId.Value,
                    source.RecordSchema))
            {
                throw new InvalidDataException($"persistence.snapshot.reference-source-schema-mismatch:{identity.PartitionId.Value}");
            }
            if (source.ActualItemCount != checked((ulong)source.RecordIdsCanonical.Count))
                throw new InvalidDataException($"persistence.snapshot.reference-source-count-mismatch:{identity.PartitionId.Value}");
            if (!sourceByPartition.TryAdd(identity.PartitionId.Value, source))
                throw new InvalidDataException($"persistence.snapshot.reference-source-duplicate:{identity.PartitionId.Value}");
        }

        var records = new Dictionary<(string PartitionId, OpaqueId128 RecordId), SchemaRefV1>();
        foreach (var identity in StandardDomainPartitionRegistry.Entries)
        {
            if (!sourceByPartition.TryGetValue(identity.PartitionId.Value, out var source))
                throw new InvalidDataException($"persistence.snapshot.reference-source-missing:{identity.PartitionId.Value}");

            OpaqueId128? previous = null;
            foreach (var recordId in source.RecordIdsCanonical)
            {
                if (recordId.IsZero)
                    throw new InvalidDataException($"persistence.snapshot.reference-source-zero-id:{identity.PartitionId.Value}");
                if (previous is { } prior && prior.CompareTo(recordId) >= 0)
                    throw new InvalidDataException($"persistence.snapshot.reference-source-order:{identity.PartitionId.Value}");
                previous = recordId;
                if (!records.TryAdd((identity.PartitionId.Value, recordId), source.RecordSchema))
                    throw new InvalidDataException($"persistence.snapshot.reference-source-record-duplicate:{identity.PartitionId.Value}");
            }
        }

        _records = new ReadOnlyDictionary<(string PartitionId, OpaqueId128 RecordId), SchemaRefV1>(records);
    }

    public ulong RecordCount => checked((ulong)_records.Count);

    public bool Exists(PartitionRecordRefV1 reference)
        => _records.ContainsKey((reference.PartitionId.Value, reference.RecordId));

    public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        => _records.TryGetValue((reference.PartitionId.Value, reference.RecordId), out schema);

    public static DomainSnapshotReferenceResolverV1 FromRecoveredSections(
        IEnumerable<CanonicalSnapshotSectionMaterialV1> sections,
        DomainNestedSnapshotCodecRegistryV1? nestedCodecs = null)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var byId = new Dictionary<string, CanonicalSnapshotSectionMaterialV1>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            ArgumentNullException.ThrowIfNull(section);
            if (!byId.TryAdd(section.SectionId, section))
                throw new InvalidDataException($"persistence.snapshot.recovered-reference-section-duplicate:{section.SectionId}");
        }

        var sources = new List<IDomainPartitionSnapshotReferenceSourceV1>(StandardDomainPartitionRegistry.StandardPartitionCount);
        foreach (var identity in StandardDomainPartitionRegistry.Entries)
        {
            if (!byId.TryGetValue(identity.PartitionId.Value, out var section))
                throw new InvalidDataException($"persistence.snapshot.recovered-reference-section-missing:{identity.PartitionId.Value}");
            if (section.SectionSchema != identity.PartitionSchema)
                throw new InvalidDataException($"persistence.snapshot.recovered-reference-section-schema:{identity.PartitionId.Value}");

            var source = CreateRecoveredSource(identity, section, nestedCodecs);
            var sourceHeader = source switch
            {
                DomainSnapshotRecoveredReferenceSourceV1 v1 => v1.Header,
                InfrastructureNetworkTopologyRecoveredReferenceSourceV2 infrastructureV2 => infrastructureV2.Header,
                PhysicalOccupancyRecoveredReferenceSourceV2 physicalV2 => physicalV2.Header,
                SocietyMarketTransactionRecoveredReferenceSourceV2 marketV2 => marketV2.Header,
                SpatialTerrainGeometryRecoveredReferenceSourceV2 terrainV2 => terrainV2.Header,
                _ => throw new InvalidDataException($"persistence.snapshot.recovered-reference-source-type:{identity.PartitionId.Value}"),
            };
            if (source.ActualItemCount != section.LogicalItemCount ||
                !CryptographicOperations.FixedTimeEquals(sourceHeader.CanonicalDigest, section.LogicalContentDigest))
                throw new InvalidDataException($"persistence.snapshot.recovered-reference-section-authority:{identity.PartitionId.Value}");
            sources.Add(source);
        }

        return new DomainSnapshotReferenceResolverV1(sources);
    }

    private static IDomainPartitionSnapshotReferenceSourceV1 CreateRecoveredSource(
        DomainPartitionIdentityV1 identity,
        CanonicalSnapshotSectionMaterialV1 section,
        DomainNestedSnapshotCodecRegistryV1? nestedCodecs)
    {
        if (section.LogicalItemCount == 0)
        {
            return new DomainSnapshotRecoveredReferenceSourceV1(
                identity.PartitionId.Value,
                section.Fragments,
                nestedCodecs);
        }

        if (string.Equals(identity.PartitionId.Value, InfrastructureNetworkTopologyRecordSchemaV2.PartitionId, StringComparison.Ordinal))
            return CreateInfrastructureNetworkRecoveredSource(identity, section, nestedCodecs);

        if (string.Equals(identity.PartitionId.Value, PhysicalOccupancyRecordSchemaV2.PartitionId, StringComparison.Ordinal))
            return CreatePhysicalOccupancyRecoveredSource(identity, section, nestedCodecs);

        if (string.Equals(identity.PartitionId.Value, SocietyMarketTransactionRecordSchemaV2.PartitionId, StringComparison.Ordinal))
            return CreateMarketRecoveredSource(identity, section, nestedCodecs);

        if (string.Equals(identity.PartitionId.Value, SpatialTerrainGeometryRecordSchemaV2.PartitionId, StringComparison.Ordinal))
            return CreateTerrainRecoveredSource(identity, section, nestedCodecs);

        return new DomainSnapshotRecoveredReferenceSourceV1(
            identity.PartitionId.Value,
            section.Fragments,
            nestedCodecs);
    }

    private static IDomainPartitionSnapshotReferenceSourceV1 CreateInfrastructureNetworkRecoveredSource(
        DomainPartitionIdentityV1 identity,
        CanonicalSnapshotSectionMaterialV1 section,
        DomainNestedSnapshotCodecRegistryV1? nestedCodecs)
    {
        InvalidDataException? v2Failure = null;
        try
        {
            return new InfrastructureNetworkTopologyRecoveredReferenceSourceV2(section.Fragments);
        }
        catch (InvalidDataException ex)
        {
            v2Failure = ex;
        }

        try
        {
            return new DomainSnapshotRecoveredReferenceSourceV1(
                identity.PartitionId.Value,
                section.Fragments,
                nestedCodecs);
        }
        catch (InvalidDataException v1Failure)
        {
            throw new InvalidDataException(
                "persistence.snapshot.recovered-reference-infrastructure-network-schema-unrecognized",
                new AggregateException(v2Failure, v1Failure));
        }
    }

    private static IDomainPartitionSnapshotReferenceSourceV1 CreatePhysicalOccupancyRecoveredSource(
        DomainPartitionIdentityV1 identity,
        CanonicalSnapshotSectionMaterialV1 section,
        DomainNestedSnapshotCodecRegistryV1? nestedCodecs)
    {
        InvalidDataException? v2Failure = null;
        try
        {
            return new PhysicalOccupancyRecoveredReferenceSourceV2(section.Fragments);
        }
        catch (InvalidDataException ex)
        {
            v2Failure = ex;
        }

        try
        {
            return new DomainSnapshotRecoveredReferenceSourceV1(
                identity.PartitionId.Value,
                section.Fragments,
                nestedCodecs);
        }
        catch (InvalidDataException v1Failure)
        {
            throw new InvalidDataException(
                "persistence.snapshot.recovered-reference-physical-occupancy-schema-unrecognized",
                new AggregateException(v2Failure, v1Failure));
        }
    }

    private static IDomainPartitionSnapshotReferenceSourceV1 CreateMarketRecoveredSource(
        DomainPartitionIdentityV1 identity,
        CanonicalSnapshotSectionMaterialV1 section,
        DomainNestedSnapshotCodecRegistryV1? nestedCodecs)
    {
        InvalidDataException? v2Failure = null;
        try
        {
            return new SocietyMarketTransactionRecoveredReferenceSourceV2(section.Fragments);
        }
        catch (InvalidDataException ex)
        {
            v2Failure = ex;
        }

        try
        {
            return new DomainSnapshotRecoveredReferenceSourceV1(
                identity.PartitionId.Value,
                section.Fragments,
                nestedCodecs);
        }
        catch (InvalidDataException v1Failure)
        {
            throw new InvalidDataException(
                "persistence.snapshot.recovered-reference-market-schema-unrecognized",
                new AggregateException(v2Failure, v1Failure));
        }
    }

    private static IDomainPartitionSnapshotReferenceSourceV1 CreateTerrainRecoveredSource(
        DomainPartitionIdentityV1 identity,
        CanonicalSnapshotSectionMaterialV1 section,
        DomainNestedSnapshotCodecRegistryV1? nestedCodecs)
    {
        InvalidDataException? v2Failure = null;
        try
        {
            return new SpatialTerrainGeometryRecoveredReferenceSourceV2(section.Fragments);
        }
        catch (InvalidDataException ex)
        {
            v2Failure = ex;
        }

        try
        {
            return new DomainSnapshotRecoveredReferenceSourceV1(
                identity.PartitionId.Value,
                section.Fragments,
                nestedCodecs);
        }
        catch (InvalidDataException v1Failure)
        {
            throw new InvalidDataException(
                "persistence.snapshot.recovered-reference-terrain-schema-unrecognized",
                new AggregateException(v2Failure, v1Failure));
        }
    }
}
