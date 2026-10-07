using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04TerrainGeometryPartitionStateV2Initializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SpatialTerrainGeometryPartitionIdentityV2.ValidateCanonicalContract();

        var brickId = Id("00000000000000000000000000024600");
        var rootId = Id("00000000000000000000000000024601");
        var brick = SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            new TerrainBrickV1(
                brickId,
                0,
                new SpatialCellKeyV1(0, 1, 2, 3),
                250,
                Enumerable.Repeat(-1, TerrainBrickV1.SdfSampleCount),
                Enumerable.Repeat((ushort)7, TerrainBrickV1.SurfaceMaterialCount),
                2),
            1,
            DetailLevelV1.D0Entity);
        var root = new SpatialTerrainGeometryRecordMaterialV2(
            rootId,
            4,
            1,
            null,
            DetailLevelV1.D0Entity,
            null,
            new SpatialTerrainRootPayloadV2(
                new PartitionRecordRefV1("spatial.scope_registry", Id("00000000000000000000000000024610")),
                new PartitionRecordRefV1(SpatialTerrainGeometryRecordSchemaV2.PartitionId, brickId),
                5,
                [new StableToken("rock")],
                Array.Empty<PartitionRecordRefV1>(),
                null));

        var partition = new SpatialTerrainGeometryPartitionStateV2([root, brick]);
        var current = StandardDomainPartitionRegistry.Get(SpatialTerrainGeometryRecordSchemaV2.PartitionId);
        Require(partition.State.Identity.PartitionSchema == current.PartitionSchema,
            "terrain v2 foundation must retain the existing partition schema/container contract.");
        Require(partition.State.Identity.RecordSchema == SpatialTerrainGeometryRecordSchemaV2.RecordSchema,
            "terrain v2 foundation must bind the mixed record set to record schema 2.0.");
        Require(current.RecordSchema.Version == new SchemaVersionV1(1, 0),
            "terrain v2 foundation must not mutate the production standard registry.");
        Require(partition.State.ItemCount == 2,
            "terrain v2 foundation must contain both root and brick records.");

        var payloadKinds = partition.State.RecordsCanonical.Select(static record => record.Payload.RecordKind).ToHashSet(StringComparer.Ordinal);
        Require(payloadKinds.SetEquals([
                SpatialTerrainGeometryRecordSchemaV2.TerrainRootKind,
                SpatialTerrainGeometryRecordSchemaV2.TerrainBrickKind]),
            "terrain v2 foundation must preserve both mixed payload arms under one record schema.");
        Require(partition.State.RecordsCanonical.All(static record =>
                record.RecordSchema == SpatialTerrainGeometryRecordSchemaV2.RecordSchema),
            "every mixed terrain v2 record must carry the same 2.0 record schema identity.");
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
