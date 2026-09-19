using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04SpatialTileScopeAuthoritySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();

        var scopes = Qa04SpatialTileScopeAuthorityV1.MaterializeCanonical();
        Require(scopes.ItemCount == Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount,
            "QA-04 TileScope authority must materialize exactly 4096 records.");

        var records = scopes.RecordsCanonical.ToArray();
        Require(records.Length == Qa04ReferenceLoadV1.RegionalTileCount,
            "QA-04 TileScope record count drifted.");
        Require(records.Select(static record => record.RecordId).Distinct().Count() == records.Length,
            "QA-04 TileScope RecordIds must be unique.");

        for (ushort tile = 0; tile < Qa04ReferenceLoadV1.RegionalTileCount; tile++)
        {
            var expectedId = Qa04SpatialTileScopeAuthorityV1.ScopeId(tile);
            Require(scopes.TryGet(expectedId, out var record) && record is not null,
                "QA-04 TileScope authority is missing a canonical tile.");
            Require(record.RecordId == expectedId && record.Revision == 1 && record.CreatedStep == 0 &&
                    record.RetiredStep is null && record.DetailLevel == DetailLevelV1.D2RegionalAggregate &&
                    record.LineageRef is null,
                "QA-04 TileScope envelope drifted.");
            Require(record.Payload.ScopeClass.Value == "perf.regional-tile" &&
                    record.Payload.ParentScope is null && record.Payload.ActiveFrom == 0 &&
                    record.Payload.RetiredAt is null && record.Payload.ScopeFlags == 0,
                "QA-04 TileScope payload drifted.");
            Require(record.Payload.GeometryRef.PartitionId.Value == SpatialTerrainGeometryRecordSchemaV2.PartitionId &&
                    record.Payload.GeometryRef.RecordId == Qa04TerrainRootMaterializerV1.RootId(tile),
                "QA-04 TileScope geometry_ref must target the same-tile TerrainRoot.");
            Require(Qa04SpatialTileScopeAuthorityV1.ScopeRef(tile).RecordId == expectedId,
                "QA-04 TileScope Ref helper drifted.");
        }

        var terrain = Qa04TerrainRootMaterializerV1
            .MaterializeCanonical(Qa04SpatialTileScopeAuthorityV1.ScopeRef)
            .ToArray();
        Qa04SpatialTileScopeAuthorityV1.ValidateTerrainReciprocalClosure(terrain);

        RequireThrows<ArgumentOutOfRangeException>(
            static () => Qa04SpatialTileScopeAuthorityV1.ScopeId(checked((ushort)Qa04ReferenceLoadV1.RegionalTileCount)),
            "QA-04 TileScope authority must reject tile indices outside the canonical lattice.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
