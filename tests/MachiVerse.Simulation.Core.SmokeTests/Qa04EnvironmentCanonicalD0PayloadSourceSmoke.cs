using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04EnvironmentCanonicalD0PayloadSourceSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var source = new Qa04EnvironmentCanonicalD0PayloadSourceV1();
        var materialized = Qa04EnvironmentD0DomainMaterializerV1.MaterializeReduced(
            source,
            new CanonicalSchemaResolver(),
            recordsPerPartition: 1);

        Require(materialized.MaterializedRecordCount == 13,
            "Canonical Environment D0 payload source reduced proof must materialize all 13 partitions.");
        Require(materialized.CountsByPartition.Count == 13 &&
                materialized.CountsByPartition.Values.All(static count => count == 1),
            "Canonical Environment D0 reduced partition coverage drifted.");
        Require(!materialized.FullCanonicalD0Materialized,
            "Reduced Environment D0 proof must never report full canonical materialization.");

        var groundwater = materialized.State.Groundwater.RecordsCanonical.Single().Payload;
        Require(groundwater.NeighborRefs.Count == 1 &&
                groundwater.NeighborRefs[0].PartitionId.Value == EnvironmentGroundwaterPayloadV1.PartitionId,
            "Canonical groundwater genesis must use same-partition successor topology.");
        var surface = materialized.State.SurfaceWater.RecordsCanonical.Single().Payload;
        Require(surface.DownstreamRefs.Count == 1 &&
                surface.DownstreamRefs[0].PartitionId.Value == EnvironmentSurfaceWaterPayloadV1.PartitionId,
            "Canonical surface-water genesis must use same-partition successor topology.");
        var ocean = materialized.State.Ocean.RecordsCanonical.Single().Payload;
        Require(ocean.NeighborRefs.Count == 1 &&
                ocean.NeighborRefs[0].PartitionId.Value == EnvironmentOceanPayloadV1.PartitionId,
            "Canonical ocean genesis must use same-partition successor topology.");

        foreach (var scope in new[]
                 {
                     materialized.State.Geology.RecordsCanonical.Single().Payload.SpatialScope,
                     materialized.State.Soil.RecordsCanonical.Single().Payload.SpatialScope,
                     materialized.State.Atmosphere.RecordsCanonical.Single().Payload.SpatialScope,
                     materialized.State.Hazard.RecordsCanonical.Single().Payload.SpatialScope,
                 })
        {
            Require(scope.PartitionId.Value == "spatial.scope_registry" && !scope.RecordId.IsZero,
                "Canonical Environment D0 payloads must use the Spatial TileScope authority.");
        }

        var lineage = materialized.State.Lineage.RecordsCanonical.Single().Payload;
        Require(lineage.SubjectRef.PartitionId.Value != EnvironmentLineagePayloadV1.PartitionId &&
                !lineage.SubjectRef.RecordId.IsZero && lineage.ParentRefs.Count == 0 &&
                lineage.Generation == 1 && lineage.MaterializationKind.Value == "perf.genesis" &&
                lineage.SourceDigest.Length == 32,
            "Canonical Environment D0 lineage genesis drifted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CanonicalSchemaResolver : IDomainRecordSchemaResolverV1
    {
        public bool Exists(PartitionRecordRefV1 reference)
        {
            if (reference.RecordId.IsZero) return false;
            try
            {
                _ = StandardDomainPartitionRegistry.Get(reference.PartitionId.Value);
                return true;
            }
            catch (KeyNotFoundException)
            {
                return false;
            }
        }

        public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
        {
            if (!Exists(reference))
            {
                schema = default;
                return false;
            }
            schema = StandardDomainPartitionRegistry.Get(reference.PartitionId.Value).RecordSchema;
            return true;
        }
    }
}
