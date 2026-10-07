using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04TerrainBrickDescriptorMaterializationInitializer
{
    private static readonly StableToken TerrainClass = new("spatial.hot-terrain-brick");

    [ModuleInitializer]
    internal static void Initialize()
    {
        Qa04TerrainBrickDescriptorMaterializerV1.ValidateCanonicalContract();
        Require(Qa04TerrainBrickDescriptorMaterializerV1.CanonicalTerrainBrickCount == 500_000,
            "QA-04 Terrain descriptor count must remain exactly 500,000.");
        Require(Qa04TerrainBrickDescriptorMaterializerV1.D0SampleSpacingMm == 250,
            "QA-04 D0 Terrain spacing must remain 250 mm.");
        Require(Qa04TerrainBrickDescriptorMaterializerV1.InitialRecordRevision == 1,
            "QA-04 Terrain genesis record revision must follow the common Domain initial revision contract.");

        // この source は境界検証専用の synthetic fixture。canonical benchmark Terrain ではない。
        var materialized = Qa04TerrainBrickDescriptorMaterializerV1.Materialize(
            new SyntheticContentSource(),
            recordCount: 2);
        Require(materialized.MaterializedBrickCount == 2 && !materialized.FullDescriptorCountMaterialized,
            "Reduced Terrain materialization must report descriptor count without claiming full population.");
        Require(materialized.Partition.State.ItemCount == 2,
            "Terrain descriptor materializer must emit one exact v2 record per supplied descriptor.");

        for (ulong ordinal = 0; ordinal < 2; ordinal++)
        {
            var descriptor = Qa04ReferenceLoadV1.Record(TerrainClass, ordinal);
            Require(materialized.Partition.RecordSet.TryGet(descriptor.RecordId, out var record) && record is not null,
                "Terrain descriptor record id must be preserved in v2 material.");
            Require(record!.Revision == Qa04TerrainBrickDescriptorMaterializerV1.InitialRecordRevision,
                "Terrain descriptor material must preserve canonical initial record revision.");
            Require(record.Payload is SpatialTerrainBrickPayloadV2 brick &&
                    brick.SampleSpacingMm == Qa04TerrainBrickDescriptorMaterializerV1.D0SampleSpacingMm,
                "Terrain descriptor material must preserve the exact D0 brick arm/spacing.");
        }

        RequireThrows(
            () => Qa04TerrainBrickDescriptorMaterializerV1.Materialize(
                new SyntheticContentSource(wrongRecordId: true),
                recordCount: 1),
            "qa04.materialization.terrain-brick-id-mismatch");
        RequireThrows(
            () => Qa04TerrainBrickDescriptorMaterializerV1.Materialize(
                new SyntheticContentSource(sampleSpacingMm: 1_000),
                recordCount: 1),
            "qa04.materialization.terrain-d0-spacing-mismatch");
        RequireThrows(
            () => Qa04TerrainBrickDescriptorMaterializerV1.Materialize(
                new SyntheticContentSource(revision: 2),
                recordCount: 1),
            "qa04.materialization.terrain-initial-revision-mismatch");
    }

    private sealed class SyntheticContentSource(
        bool wrongRecordId = false,
        uint sampleSpacingMm = Qa04TerrainBrickDescriptorMaterializerV1.D0SampleSpacingMm,
        ulong revision = Qa04TerrainBrickDescriptorMaterializerV1.InitialRecordRevision)
        : IQa04TerrainBrickContentSourceV1
    {
        public TerrainBrickV1 CreateBrick(Qa04ReferenceRecordV1 descriptor)
        {
            var recordId = descriptor.RecordId;
            if (wrongRecordId)
            {
                recordId = Qa04ReferenceLoadV1.Record(TerrainClass, descriptor.Ordinal + 1).RecordId;
            }

            return new TerrainBrickV1(
                recordId,
                level: 0,
                new SpatialCellKeyV1(
                    Level: 0,
                    X: descriptor.RegionalTileIndex,
                    Y: descriptor.DenseRegionIndex ?? 0,
                    Z: checked((int)descriptor.Ordinal)),
                sampleSpacingMm,
                Enumerable.Repeat(checked((int)descriptor.Ordinal + 1), TerrainBrickV1.SdfSampleCount),
                Enumerable.Repeat(checked((ushort)(descriptor.Ordinal + 1)), TerrainBrickV1.SurfaceMaterialCount),
                revision);
        }
    }

    private static void RequireThrows(Action action, string expectedMessage)
    {
        try
        {
            action();
        }
        catch (InvalidDataException exception) when (exception.Message == expectedMessage)
        {
            return;
        }

        throw new InvalidOperationException($"Expected InvalidDataException: {expectedMessage}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
