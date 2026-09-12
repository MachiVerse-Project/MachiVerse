using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.WorldState;

internal static class SpatialTerrainGeometryStreamingFragmentSourceSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var records = new[]
        {
            Brick("00000000000000000000000000000003", 16),
            Brick("00000000000000000000000000000001", 0),
            Brick("00000000000000000000000000000002", 8),
        };
        var partition = new SpatialTerrainGeometryPartitionStateV2(records);
        var authority = SpatialTerrainGeometrySnapshotAuthorityV2.CreateCanonical(
            partition,
            revision: 1,
            basisStep: 0,
            detailLevel: DetailLevelV1.D0Entity);
        var expected = new SpatialTerrainGeometrySnapshotSectionProviderV2().Create(authority);

        var factoryCalls = 0;
        var source = new SpatialTerrainGeometryStreamingFragmentSourceV2(
            authority.Header,
            authority.ActualItemCount,
            () =>
            {
                factoryCalls++;
                return partition.RecordSet.RecordsCanonical;
            });
        var actual = source.EnumerateFragments().ToArray();

        Require(factoryCalls == 2,
            "Terrain streaming fragment source must use one planning pass and one emission pass.");
        Require(actual.Length == expected.Fragments.Count,
            "Terrain streaming source must preserve provider fragment count.");
        for (var index = 0; index < actual.Length; index++)
            RequireSameFragment(expected.Fragments[index], actual[index]);
    }

    private static SpatialTerrainGeometryRecordMaterialV2 Brick(string id, int originX)
    {
        var brick = new TerrainBrickV1(
            OpaqueId128.Parse(id),
            level: 0,
            new SpatialCellKeyV1(0, originX, 0, 0),
            sampleSpacingMm: 250,
            Enumerable.Repeat(0, TerrainBrickV1.SdfSampleCount),
            Enumerable.Repeat((ushort)1, TerrainBrickV1.SurfaceMaterialCount),
            revision: 1);
        return SpatialTerrainGeometryRecordMaterialV2.FromTerrainBrick(
            brick,
            createdStep: 0,
            detailLevel: DetailLevelV1.D0Entity);
    }

    private static void RequireSameFragment(
        SnapshotSectionFragmentMaterialV1 expected,
        SnapshotSectionFragmentMaterialV1 actual)
    {
        Require(string.Equals(expected.SectionId, actual.SectionId, StringComparison.Ordinal),
            "Terrain streaming fragment section id must match the existing provider.");
        Require(expected.FragmentIndex == actual.FragmentIndex &&
                expected.FragmentCount == actual.FragmentCount &&
                expected.ItemCount == actual.ItemCount,
            "Terrain streaming fragment shape must match the existing provider.");
        Require(OptionalBytesEqual(expected.FirstRecordId, actual.FirstRecordId) &&
                OptionalBytesEqual(expected.LastRecordId, actual.LastRecordId),
            "Terrain streaming fragment record range must match the existing provider.");
        Require(expected.FragmentPayload.AsSpan().SequenceEqual(actual.FragmentPayload),
            "Terrain streaming fragment payload bytes must match the existing canonical wire exactly.");
    }

    private static bool OptionalBytesEqual(byte[]? left, byte[]? right)
        => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
