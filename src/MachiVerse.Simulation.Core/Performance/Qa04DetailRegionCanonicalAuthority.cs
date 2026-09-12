using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed class Qa04DetailRegionCanonicalMaterializationV1
{
    internal Qa04DetailRegionCanonicalMaterializationV1(
        DomainPartitionStateV1<SpatialDetailRegionsPayloadV1> partition,
        IReadOnlyList<DetailRegionStateV1> regionsByTile,
        IDomainRecordSchemaResolverV1 references)
    {
        Partition = partition;
        RegionsByTile = regionsByTile;
        References = references;
    }

    public DomainPartitionStateV1<SpatialDetailRegionsPayloadV1> Partition { get; }
    public IReadOnlyList<DetailRegionStateV1> RegionsByTile { get; }
    public IDomainRecordSchemaResolverV1 References { get; }
    public ulong MaterializedRecordCount => Partition.ItemCount;

    public DetailRegionStateV1 RegionForTile(ushort tileIndex)
    {
        if (tileIndex >= RegionsByTile.Count) throw new ArgumentOutOfRangeException(nameof(tileIndex));
        return RegionsByTile[tileIndex];
    }
}

/// <summary>
/// Production-path materialization for the benchmark-only spatial.detail_regions authority fixed by
/// phase4-alpha11-detail-region-reference-authority.md. Every canonical TileScope receives exactly
/// one DetailRegion record. All eight standard domains start at D2 except the exact 1,424 canonical
/// workload targets, whose requested domain starts at that requirement's CurrentLevel.
/// </summary>
public static class Qa04DetailRegionCanonicalAuthorityV1
{
    public const int CanonicalRegionCount = 4_096;
    public const int CanonicalDomainCount = 8;
    public const int CanonicalOverrideCount = 1_424;
    public const int CanonicalD0EntryCount = 890;
    public const int CanonicalD1EntryCount = 534;
    public const int CanonicalD2EntryCount = 31_344;
    public const uint InitialLineageGeneration = 0;
    public const ulong InitialLastTransitionStep = 0;

    private static readonly StableToken SpatialDomain = new("spatial");
    private static readonly StableToken CreationKind = new("perf.detail-region");

    public static void ValidateCanonicalContract()
    {
        Qa04CanonicalDetailTransitionBindingV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();

        var identity = StandardDomainPartitionRegistry.Get(SpatialDetailRegionsPayloadV1.PartitionId);
        if (identity.OwnerDomain.Value != "spatial" ||
            CanonicalRegionCount != Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount ||
            CanonicalRegionCount != Qa04ReferenceLoadV1.RegionalTileCount ||
            CanonicalOverrideCount != checked((int)Qa04CanonicalDetailTransitionBindingV1.CanonicalRequestCount))
            throw new InvalidDataException("qa04.detail-region.canonical-contract-drift");

        var domains = StandardDomains();
        if (domains.Length != CanonicalDomainCount ||
            domains.Select(static domain => domain.Value).Distinct(StringComparer.Ordinal).Count() != CanonicalDomainCount)
            throw new InvalidDataException("qa04.detail-region.domain-count-drift");

        var requirements = Qa04CanonicalDetailTransitionBindingV1.CanonicalRequirements().ToArray();
        if (requirements.Length != CanonicalOverrideCount ||
            requirements.Select(static requirement => requirement.TileIndex).Distinct().Count() != CanonicalOverrideCount ||
            requirements.Count(static requirement => requirement.CurrentLevel == DetailLevelV1.D0Entity) != CanonicalD0EntryCount ||
            requirements.Count(static requirement => requirement.CurrentLevel == DetailLevelV1.D1LocalAggregate) != CanonicalD1EntryCount)
            throw new InvalidDataException("qa04.detail-region.override-contract-drift");

        if (CanonicalD0EntryCount + CanonicalD1EntryCount + CanonicalD2EntryCount !=
            checked(CanonicalRegionCount * CanonicalDomainCount))
            throw new InvalidDataException("qa04.detail-region.level-entry-total-drift");
    }

    public static OpaqueId128 RegionId(ushort tileIndex)
    {
        ValidateTileIndex(tileIndex);
        return DerivedIdentity.DeriveEntityId(
            Qa04ReferenceLoadV1.WorldId,
            creationStep: 0,
            SpatialDomain,
            OpaqueId128.Zero,
            CreationKind,
            tileIndex);
    }

    public static Qa04DetailRegionCanonicalMaterializationV1 MaterializeCanonical()
    {
        ValidateCanonicalContract();

        var references = new CanonicalReferenceResolver();
        SeedTileScopeAuthority(references);
        var validator = new StandardDomainPayloadCodecValidatorV1();
        var identity = StandardDomainPartitionRegistry.Get(SpatialDetailRegionsPayloadV1.PartitionId);
        var domains = StandardDomains();
        var overridesByTile = Qa04CanonicalDetailTransitionBindingV1.CanonicalRequirements()
            .ToDictionary(static requirement => requirement.TileIndex);

        var records = new DomainRecordEnvelopeV1<SpatialDetailRegionsPayloadV1>[CanonicalRegionCount];
        var regionsByTile = new DetailRegionStateV1[CanonicalRegionCount];
        var ids = new HashSet<OpaqueId128>();
        var d0 = 0;
        var d1 = 0;
        var d2 = 0;

        for (ushort tile = 0; tile < CanonicalRegionCount; tile++)
        {
            var scopeRef = Qa04SpatialTileScopeAuthorityV1.ScopeRef(tile);
            if (!references.Exists(scopeRef))
                throw new InvalidDataException("qa04.detail-region.scope-authority-missing");

            var levels = domains.ToDictionary(
                static domain => domain,
                static _ => DetailLevelV1.D2RegionalAggregate);
            if (overridesByTile.TryGetValue(tile, out var requirement))
            {
                if (requirement.SpatialScopeId != scopeRef.RecordId)
                    throw new InvalidDataException("qa04.detail-region.override-scope-mismatch");
                levels[requirement.DomainToken] = requirement.CurrentLevel;
            }

            var canonicalLevels = levels
                .OrderBy(static pair => pair.Key.Value, StringComparer.Ordinal)
                .ToArray();
            foreach (var level in canonicalLevels)
            {
                switch (level.Value)
                {
                    case DetailLevelV1.D0Entity: d0++; break;
                    case DetailLevelV1.D1LocalAggregate: d1++; break;
                    case DetailLevelV1.D2RegionalAggregate: d2++; break;
                    default: throw new InvalidDataException("qa04.detail-region.unexpected-genesis-level");
                }
            }

            var payload = new SpatialDetailRegionsPayloadV1(
                scopeRef,
                Array.AsReadOnly(canonicalLevels
                    .Select(static pair => new KeyValuePair<string, byte>(pair.Key.Value, (byte)pair.Value))
                    .ToArray()),
                InitialLineageGeneration,
                InitialLastTransitionStep,
                Array.Empty<StableToken>());
            validator.Validate(SpatialDetailRegionsPayloadV1.PartitionId, payload.ToStandardPayload(), references);

            var recordId = RegionId(tile);
            if (!ids.Add(recordId))
                throw new InvalidDataException("qa04.detail-region.record-id-duplicate");
            var record = new DomainRecordEnvelopeV1<SpatialDetailRegionsPayloadV1>(
                recordId,
                identity.RecordSchema,
                revision: 1,
                createdStep: 0,
                retiredStep: null,
                DetailLevelV1.D2RegionalAggregate,
                lineageRef: null,
                payload);
            records[tile] = record;
            references.Add(new PartitionRecordRefV1(SpatialDetailRegionsPayloadV1.PartitionId, recordId), record.RecordSchema);

            regionsByTile[tile] = new DetailRegionStateV1(
                recordId,
                scopeRef.RecordId,
                canonicalLevels,
                InitialLineageGeneration,
                InitialLastTransitionStep,
                Array.Empty<StableToken>());
        }

        if (d0 != CanonicalD0EntryCount || d1 != CanonicalD1EntryCount || d2 != CanonicalD2EntryCount)
            throw new InvalidDataException("qa04.detail-region.genesis-level-cardinality-drift");

        var partition = new DomainPartitionStateV1<SpatialDetailRegionsPayloadV1>(identity, records);
        if (partition.ItemCount != CanonicalRegionCount)
            throw new InvalidDataException("qa04.detail-region.partition-count-drift");

        foreach (var requirement in Qa04CanonicalDetailTransitionBindingV1.CanonicalRequirements())
        {
            var region = regionsByTile[requirement.TileIndex];
            if (region.DetailRegionId != RegionId(requirement.TileIndex) ||
                region.SpatialScopeRef != requirement.SpatialScopeId ||
                region.GetLevel(requirement.DomainToken) != requirement.CurrentLevel)
                throw new InvalidDataException("qa04.detail-region.workload-binding-drift");
        }

        return new Qa04DetailRegionCanonicalMaterializationV1(
            partition,
            Array.AsReadOnly(regionsByTile),
            references);
    }

    private static StableToken[] StandardDomains()
        => StandardDomainExecutionPlanV1.Create().Entries
            .Select(static entry => entry.DomainToken)
            .OrderBy(static token => token.Value, StringComparer.Ordinal)
            .ToArray();

    private static void SeedTileScopeAuthority(CanonicalReferenceResolver references)
    {
        foreach (var record in Qa04SpatialTileScopeAuthorityV1.MaterializeCanonical().RecordsCanonical)
        {
            references.Add(
                new PartitionRecordRefV1(SpatialScopeRegistryPayloadV1.PartitionId, record.RecordId),
                record.RecordSchema);
        }
    }

    private static void ValidateTileIndex(ushort tileIndex)
    {
        if (tileIndex >= CanonicalRegionCount)
            throw new ArgumentOutOfRangeException(nameof(tileIndex));
    }

    private sealed class CanonicalReferenceResolver : IDomainRecordSchemaResolverV1
    {
        private readonly Dictionary<PartitionRecordRefV1, SchemaRefV1> _records = new();

        public void Add(PartitionRecordRefV1 reference, SchemaRefV1 schema)
        {
            if (reference.RecordId.IsZero)
                throw new InvalidDataException("qa04.detail-region.canonical-reference-zero");
            if (!_records.TryAdd(reference, schema) && _records[reference] != schema)
                throw new InvalidDataException("qa04.detail-region.canonical-reference-schema-conflict");
        }

        public bool Exists(PartitionRecordRefV1 reference) => _records.ContainsKey(reference);

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
            => _records.TryGetValue(reference, out schema);
    }
}
