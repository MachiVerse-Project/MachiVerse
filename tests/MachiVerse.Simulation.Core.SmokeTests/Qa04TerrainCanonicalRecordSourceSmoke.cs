using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04TerrainCanonicalRecordSourceSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var source = Qa04TerrainCanonicalRecordSourceV1.CreateCanonical();
        Require(source.ItemCount == 508_192,
            "Canonical Terrain record source must expose exactly 508,192 records.");
        Require(typeof(Qa04TerrainCanonicalRecordLocatorV1).IsValueType,
            "Canonical Terrain locator must remain a value type so the identity index does not retain payload objects.");

        ulong hot = 0;
        ulong roots = 0;
        ulong anchors = 0;
        int hotIndex = -1;
        int rootIndex = -1;
        int anchorIndex = -1;
        for (var index = 0; index < source.LocatorsCanonical.Count; index++)
        {
            var locator = source.LocatorsCanonical[index];
            if (index > 0)
                Require(source.LocatorsCanonical[index - 1].RecordId.CompareTo(locator.RecordId) < 0,
                    "Canonical Terrain locator order must be strictly increasing by RecordId.");

            switch (locator.Kind)
            {
                case Qa04TerrainCanonicalRecordKindV1.HotBrick:
                    hot++;
                    if (hotIndex < 0) hotIndex = index;
                    break;
                case Qa04TerrainCanonicalRecordKindV1.Root:
                    roots++;
                    if (rootIndex < 0) rootIndex = index;
                    break;
                case Qa04TerrainCanonicalRecordKindV1.Anchor:
                    anchors++;
                    if (anchorIndex < 0) anchorIndex = index;
                    break;
                default:
                    throw new InvalidOperationException("Canonical Terrain source exposed an unknown locator kind.");
            }
        }

        Require(hot == 500_000 && roots == 4_096 && anchors == 4_096,
            "Canonical Terrain locator set must preserve the exact hot/root/anchor decomposition.");
        Require(hotIndex >= 0 && rootIndex >= 0 && anchorIndex >= 0,
            "Canonical Terrain locator set must contain all three record kinds.");

        var hotLocator = source.LocatorsCanonical[hotIndex];
        var hotRecord = source.MaterializeAt(hotIndex);
        Require(hotRecord.RecordId == hotLocator.RecordId &&
                hotRecord.Payload is SpatialTerrainBrickPayloadV2 hotPayload &&
                hotPayload.Level == 0 &&
                hotPayload.SdfMm.Count == TerrainBrickV1.SdfSampleCount &&
                hotPayload.SurfaceMaterialIds.Count == TerrainBrickV1.SurfaceMaterialCount,
            "On-demand hot Terrain materialization must preserve locator identity and canonical 729/512 payload shape.");

        var rootLocator = source.LocatorsCanonical[rootIndex];
        var rootRecord = source.MaterializeAt(rootIndex);
        Require(rootRecord.RecordId == rootLocator.RecordId &&
                rootRecord.Payload is SpatialTerrainRootPayloadV2 rootPayload &&
                rootPayload.ScopeRef == Qa04SpatialTileScopeAuthorityV1.ScopeRef(checked((ushort)rootLocator.SourceOrdinal)) &&
                rootPayload.RootBrickRef.RecordId == Qa04TerrainRootMaterializerV1.AnchorId(checked((ushort)rootLocator.SourceOrdinal)),
            "On-demand Terrain root materialization must preserve actual TileScope and D3 anchor closure.");

        var anchorLocator = source.LocatorsCanonical[anchorIndex];
        var anchorRecord = source.MaterializeAt(anchorIndex);
        Require(anchorRecord.RecordId == anchorLocator.RecordId &&
                anchorRecord.Payload is SpatialTerrainBrickPayloadV2 anchorPayload &&
                anchorPayload.Level == 3 &&
                anchorPayload.SdfMm.Count == TerrainBrickV1.SdfSampleCount &&
                anchorPayload.SurfaceMaterialIds.Count == TerrainBrickV1.SurfaceMaterialCount,
            "On-demand D3 anchor materialization must preserve locator identity and canonical payload shape.");

        var firstEnvelopes = source.EnumerateCanonicalEnvelopes().Take(4).ToArray();
        Require(firstEnvelopes.Length == 4 && firstEnvelopes.All(static envelope =>
                envelope.RecordSchema == SpatialTerrainGeometryRecordSchemaV2.RecordSchema),
            "Canonical Terrain envelope stream must preserve v2 record schema without materializing the whole partition.");
        for (var index = 1; index < firstEnvelopes.Length; index++)
            Require(firstEnvelopes[index - 1].RecordId.CompareTo(firstEnvelopes[index].RecordId) < 0,
                "Canonical Terrain envelope stream must preserve locator RecordId order.");

        Require(Qa04ReferenceWorldDependencyContractV1.Blockers.All(static blocker =>
                blocker.DependencyId.Value != "spatial.terrain-geometry.root-brick-target" &&
                blocker.FailureCode.Value != "qa04.material.terrain-brick-authority-undefined"),
            "Terrain parent world blocker must remain released after full production Snapshot/recovery evidence.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
