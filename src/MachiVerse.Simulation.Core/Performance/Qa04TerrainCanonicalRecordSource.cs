using System.Collections;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04TerrainCanonicalRecordKindV1 : byte
{
    HotBrick = 1,
    Root = 2,
    Anchor = 3,
}

/// <summary>
/// Lightweight locator for one canonical Terrain v2 record. It intentionally stores no SDF or
/// surface-material payload so the complete 508,192-record identity set can be sorted without
/// retaining the corresponding Terrain payloads.
/// </summary>
public readonly record struct Qa04TerrainCanonicalRecordLocatorV1(
    OpaqueId128 RecordId,
    Qa04TerrainCanonicalRecordKindV1 Kind,
    ulong SourceOrdinal);

/// <summary>
/// Canonical perf.reference.v1 Terrain record source for the production Snapshot path.
///
/// The source holds only a sorted locator array. A record's payload is materialized when the
/// enumerator reaches that locator and may be released before the next record is generated.
/// Existing Terrain v2 record/schema semantics and the existing canonical materializers remain the
/// authority for every generated payload; this type only supplies bounded-memory canonical order.
/// </summary>
public sealed class Qa04TerrainCanonicalRecordSourceV1
{
    public const ulong CanonicalHotBrickCount = Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount;
    public const ulong CanonicalRootCount = Qa04TerrainRootMaterializerV1.CanonicalRootCount;
    public const ulong CanonicalAnchorCount = Qa04TerrainRootMaterializerV1.CanonicalAnchorCount;
    public const ulong CanonicalRecordCount = CanonicalHotBrickCount + CanonicalRootCount + CanonicalAnchorCount;

    private static readonly StableToken TerrainReferenceClass = new("spatial.hot-terrain-brick");

    private readonly Qa04TerrainCanonicalContentSourceV1 _contentSource;
    private readonly Qa04TerrainCanonicalRecordLocatorV1[] _locators;
    private readonly IReadOnlyList<Qa04TerrainCanonicalRecordLocatorV1> _readOnlyLocators;
    private readonly IReadOnlyList<OpaqueId128> _recordIdsCanonical;

    private Qa04TerrainCanonicalRecordSourceV1(
        Qa04TerrainCanonicalContentSourceV1 contentSource,
        Qa04TerrainCanonicalRecordLocatorV1[] locators)
    {
        _contentSource = contentSource ?? throw new ArgumentNullException(nameof(contentSource));
        _locators = locators ?? throw new ArgumentNullException(nameof(locators));
        _readOnlyLocators = Array.AsReadOnly(_locators);
        _recordIdsCanonical = new LocatorRecordIdView(_locators);
        ValidateLocatorContract();
    }

    public ulong ItemCount => checked((ulong)_locators.Length);
    public IReadOnlyList<Qa04TerrainCanonicalRecordLocatorV1> LocatorsCanonical => _readOnlyLocators;
    public IReadOnlyList<OpaqueId128> RecordIdsCanonical => _recordIdsCanonical;

    public static Qa04TerrainCanonicalRecordSourceV1 CreateCanonical()
    {
        ValidateCanonicalContract();
        return new Qa04TerrainCanonicalRecordSourceV1(
            new Qa04TerrainCanonicalContentSourceV1(),
            BuildCanonicalLocators());
    }

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04TerrainBrickDescriptorMaterializerV1.ValidateCanonicalContract();
        Qa04TerrainCanonicalContentSourceV1.ValidateCanonicalContract();
        Qa04TerrainRootMaterializerV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();
        SpatialTerrainGeometryRecordSchemaV2.ValidateCanonicalContract();
        SpatialTerrainGeometryPartitionIdentityV2.ValidateCanonicalContract();

        if (CanonicalHotBrickCount != 500_000 ||
            CanonicalRootCount != 4_096 ||
            CanonicalAnchorCount != 4_096 ||
            CanonicalRecordCount != 508_192)
            throw new InvalidDataException("qa04.terrain.canonical-record-count-drift");
    }

    public IEnumerable<SpatialTerrainGeometryRecordMaterialV2> EnumerateCanonicalRecords()
    {
        foreach (var locator in _locators)
            yield return Materialize(locator);
    }

    public IEnumerable<DomainRecordEnvelopeV1<SpatialTerrainGeometryPayloadV2>> EnumerateCanonicalEnvelopes()
    {
        foreach (var record in EnumerateCanonicalRecords())
        {
            yield return new DomainRecordEnvelopeV1<SpatialTerrainGeometryPayloadV2>(
                record.RecordId,
                SpatialTerrainGeometryRecordSchemaV2.RecordSchema,
                record.Revision,
                record.CreatedStep,
                record.RetiredStep,
                record.DetailLevel,
                record.LineageRef,
                record.Payload);
        }
    }

    public SpatialTerrainGeometryRecordMaterialV2 MaterializeAt(int canonicalIndex)
    {
        if ((uint)canonicalIndex >= (uint)_locators.Length)
            throw new ArgumentOutOfRangeException(nameof(canonicalIndex));
        return Materialize(_locators[canonicalIndex]);
    }

    private SpatialTerrainGeometryRecordMaterialV2 Materialize(Qa04TerrainCanonicalRecordLocatorV1 locator)
    {
        SpatialTerrainGeometryRecordMaterialV2 record = locator.Kind switch
        {
            Qa04TerrainCanonicalRecordKindV1.HotBrick =>
                Qa04TerrainBrickDescriptorMaterializerV1.MaterializeRecordValidated(
                    _contentSource,
                    locator.SourceOrdinal),
            Qa04TerrainCanonicalRecordKindV1.Root =>
                Qa04TerrainRootMaterializerV1.CreateRoot(
                    checked((ushort)locator.SourceOrdinal),
                    Qa04SpatialTileScopeAuthorityV1.ScopeRef(checked((ushort)locator.SourceOrdinal))),
            Qa04TerrainCanonicalRecordKindV1.Anchor =>
                Qa04TerrainRootMaterializerV1.CreateAnchor(checked((ushort)locator.SourceOrdinal)),
            _ => throw new InvalidDataException("qa04.terrain.canonical-record-kind"),
        };

        if (record.RecordId != locator.RecordId)
            throw new InvalidDataException("qa04.terrain.canonical-record-id-mismatch");
        if (locator.Kind == Qa04TerrainCanonicalRecordKindV1.Root &&
            record.Payload is not SpatialTerrainRootPayloadV2)
            throw new InvalidDataException("qa04.terrain.canonical-root-kind-mismatch");
        if (locator.Kind is Qa04TerrainCanonicalRecordKindV1.HotBrick or Qa04TerrainCanonicalRecordKindV1.Anchor &&
            record.Payload is not SpatialTerrainBrickPayloadV2)
            throw new InvalidDataException("qa04.terrain.canonical-brick-kind-mismatch");
        return record;
    }

    private static Qa04TerrainCanonicalRecordLocatorV1[] BuildCanonicalLocators()
    {
        var locators = new Qa04TerrainCanonicalRecordLocatorV1[checked((int)CanonicalRecordCount)];
        var write = 0;

        for (ulong ordinal = 0; ordinal < CanonicalHotBrickCount; ordinal++)
        {
            var descriptor = Qa04ReferenceLoadV1.Record(TerrainReferenceClass, ordinal);
            locators[write++] = new Qa04TerrainCanonicalRecordLocatorV1(
                descriptor.RecordId,
                Qa04TerrainCanonicalRecordKindV1.HotBrick,
                ordinal);
        }

        for (ushort tile = 0; tile < Qa04ReferenceLoadV1.RegionalTileCount; tile++)
        {
            locators[write++] = new Qa04TerrainCanonicalRecordLocatorV1(
                Qa04TerrainRootMaterializerV1.RootId(tile),
                Qa04TerrainCanonicalRecordKindV1.Root,
                tile);
            locators[write++] = new Qa04TerrainCanonicalRecordLocatorV1(
                Qa04TerrainRootMaterializerV1.AnchorId(tile),
                Qa04TerrainCanonicalRecordKindV1.Anchor,
                tile);
        }

        if (write != locators.Length)
            throw new InvalidDataException("qa04.terrain.canonical-locator-count-mismatch");

        Array.Sort(locators, static (left, right) => left.RecordId.CompareTo(right.RecordId));
        return locators;
    }

    private void ValidateLocatorContract()
    {
        if ((ulong)_locators.Length != CanonicalRecordCount)
            throw new InvalidDataException("qa04.terrain.canonical-locator-count");

        ulong hot = 0;
        ulong roots = 0;
        ulong anchors = 0;
        OpaqueId128? previous = null;
        foreach (var locator in _locators)
        {
            if (locator.RecordId.IsZero)
                throw new InvalidDataException("qa04.terrain.canonical-locator-zero");
            if (previous is { } prior && prior.CompareTo(locator.RecordId) >= 0)
                throw new InvalidDataException("qa04.terrain.canonical-locator-order");
            previous = locator.RecordId;

            switch (locator.Kind)
            {
                case Qa04TerrainCanonicalRecordKindV1.HotBrick:
                    if (locator.SourceOrdinal >= CanonicalHotBrickCount)
                        throw new InvalidDataException("qa04.terrain.canonical-hot-ordinal");
                    hot++;
                    break;
                case Qa04TerrainCanonicalRecordKindV1.Root:
                    if (locator.SourceOrdinal >= CanonicalRootCount)
                        throw new InvalidDataException("qa04.terrain.canonical-root-tile");
                    roots++;
                    break;
                case Qa04TerrainCanonicalRecordKindV1.Anchor:
                    if (locator.SourceOrdinal >= CanonicalAnchorCount)
                        throw new InvalidDataException("qa04.terrain.canonical-anchor-tile");
                    anchors++;
                    break;
                default:
                    throw new InvalidDataException("qa04.terrain.canonical-locator-kind");
            }
        }

        if (hot != CanonicalHotBrickCount || roots != CanonicalRootCount || anchors != CanonicalAnchorCount)
            throw new InvalidDataException("qa04.terrain.canonical-locator-kind-count");
    }

    private sealed class LocatorRecordIdView : IReadOnlyList<OpaqueId128>
    {
        private readonly Qa04TerrainCanonicalRecordLocatorV1[] _source;

        public LocatorRecordIdView(Qa04TerrainCanonicalRecordLocatorV1[] source)
            => _source = source ?? throw new ArgumentNullException(nameof(source));

        public int Count => _source.Length;
        public OpaqueId128 this[int index] => _source[index].RecordId;

        public IEnumerator<OpaqueId128> GetEnumerator()
        {
            for (var index = 0; index < _source.Length; index++)
                yield return _source[index].RecordId;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
