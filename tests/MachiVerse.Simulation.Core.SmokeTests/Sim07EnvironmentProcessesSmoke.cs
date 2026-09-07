using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.Spatial;

internal static class Sim07EnvironmentProcessesSmoke
{
    internal static Task RunAsync()
    {
        var cellA = new SpatialCellKeyV1(0, -1, 0, 0);
        var cellB = new SpatialCellKeyV1(0, 0, 0, 0);
        var cellC = new SpatialCellKeyV1(0, 1, 0, 0);

        VerifyGroundwaterJacobi(cellA, cellB);
        VerifySurfaceWaterAllocation(cellA, cellB, cellC);
        VerifyOceanConservation(cellA, cellB);
        VerifyErosionMaterialBoundary(cellA, cellB);
        VerifyEcologyOrderIndependence();
        VerifyContaminantMass(cellA, cellB, cellC);
        return Task.CompletedTask;
    }

    private static void VerifyGroundwaterJacobi(SpatialCellKeyV1 cellA, SpatialCellKeyV1 cellB)
    {
        var initial = new Dictionary<SpatialCellKeyV1, long>
        {
            [cellB] = 200,
            [cellA] = 100,
        };
        var edge = new GroundwaterConductanceEdgeV1(cellA, cellB, 250_000);
        var result = GroundwaterJacobiV1.Solve(initial, [edge]);
        var permuted = GroundwaterJacobiV1.Solve(
            new Dictionary<SpatialCellKeyV1, long>
            {
                [cellA] = 100,
                [cellB] = 200,
            },
            [new GroundwaterConductanceEdgeV1(cellB, cellA, 250_000)]);

        Require(GroundwaterJacobiV1.StandardIterations == 16 && result[cellA] == 149 && result[cellB] == 151,
            "domain.environment.groundwater.jacobi: fixed 16-iteration golden fixture mismatch.");
        Require(result.OrderBy(static pair => pair.Key).SequenceEqual(permuted.OrderBy(static pair => pair.Key)),
            "domain.environment.groundwater.jacobi: cell/edge input permutation changed the result.");
    }

    private static void VerifySurfaceWaterAllocation(
        SpatialCellKeyV1 cellA,
        SpatialCellKeyV1 cellB,
        SpatialCellKeyV1 cellC)
    {
        var result = SurfaceWaterOverflowAllocatorV1.Allocate(
            12,
            [
                new SurfaceWaterCapacityV1(cellC, 0, 100),
                new SurfaceWaterCapacityV1(cellB, 10, 20),
                new SurfaceWaterCapacityV1(cellA, 5, 10),
            ]);
        Require(result.UnallocatedVolumeMl == 0 &&
                result.Allocations.SequenceEqual([
                    new SurfaceWaterAllocationV1(cellA, 5),
                    new SurfaceWaterAllocationV1(cellB, 7),
                ]),
            "domain.environment.hydrology.mass: overflow must allocate in canonical SpatialCellKey order without creating or losing water.");
    }

    private static void VerifyOceanConservation(SpatialCellKeyV1 cellA, SpatialCellKeyV1 cellB)
    {
        var stocks = new Dictionary<SpatialCellKeyV1, OceanCellStockV1>
        {
            [cellB] = new(50, -3, 500, 700),
            [cellA] = new(100, 10, 1000, 2000),
        };
        var flux = new OceanFluxEdgeV1(cellA, cellB, 20, 4, 100, 300);
        var result = OceanConservativeFluxV1.Apply(stocks, [flux]);
        var permuted = OceanConservativeFluxV1.Apply(
            new Dictionary<SpatialCellKeyV1, OceanCellStockV1>
            {
                [cellA] = stocks[cellA],
                [cellB] = stocks[cellB],
            },
            [flux]);

        Require(result[cellA] == new OceanCellStockV1(80, 6, 900, 1700),
            "domain.environment.ocean.flux: source conserved stock mismatch.");
        Require(result[cellB] == new OceanCellStockV1(70, 1, 600, 1000),
            "domain.environment.ocean.flux: target conserved stock mismatch.");
        Require(result.OrderBy(static pair => pair.Key).SequenceEqual(permuted.OrderBy(static pair => pair.Key)),
            "domain.environment.ocean.flux: input map insertion order changed the result.");
    }

    private static void VerifyErosionMaterialBoundary(SpatialCellKeyV1 cellA, SpatialCellKeyV1 cellB)
    {
        var geometryIntentId = OpaqueId128.Parse("00000000000000000000000000000721");
        var result = ErosionMaterialBoundaryV1.ApplyMaterialTransfer(
            new Dictionary<SpatialCellKeyV1, long>
            {
                [cellA] = 100,
                [cellB] = 0,
            },
            [new ErosionMaterialTransferV1(cellA, cellB, 25, geometryIntentId)]);
        Require(result[cellA] == 75 && result[cellB] == 25 && result.Values.Sum() == 100,
            "domain.environment.erosion.material: material transfer must conserve stock and require Spatial geometry causality.");
    }

    private static void VerifyEcologyOrderIndependence()
    {
        var seed = new WorldSeed256(new byte[32]);
        var worldId = OpaqueId128.Parse("00000000000000000000000000000701");
        var cohortA = new EcologyCohortStateV1(
            OpaqueId128.Parse("00000000000000000000000000000711"), 101, 1000);
        var cohortB = new EcologyCohortStateV1(
            OpaqueId128.Parse("00000000000000000000000000000712"), 203, 2000);
        var cohorts = new[] { cohortA, cohortB };

        var forward = cohorts.ToDictionary(
            static cohort => cohort.CohortId,
            cohort => EcologyCohortTransitionV1.Advance(
                cohort, 123_456, 23_456, 12_345, seed, worldId, 77));
        var reverse = cohorts.Reverse().ToDictionary(
            static cohort => cohort.CohortId,
            cohort => EcologyCohortTransitionV1.Advance(
                cohort, 123_456, 23_456, 12_345, seed, worldId, 77));

        Require(forward.OrderBy(static pair => pair.Key).SequenceEqual(reverse.OrderBy(static pair => pair.Key)),
            "domain.environment.ecology.random: cohort iteration order changed addressable stochastic realization.");
        var extinct = EcologyCohortTransitionV1.Advance(
            new EcologyCohortStateV1(cohortA.CohortId, 10, 0),
            0, 1_000_000, 0, seed, worldId, 77);
        Require(extinct.PopulationCount == 0,
            "domain.environment.ecology.random: 100% death rate fixture mismatch.");
    }

    private static void VerifyContaminantMass(
        SpatialCellKeyV1 cellA,
        SpatialCellKeyV1 cellB,
        SpatialCellKeyV1 cellC)
    {
        var stocks = new Dictionary<SpatialCellKeyV1, long>
        {
            [cellA] = 100,
            [cellB] = 50,
            [cellC] = 0,
        };
        var result = ContaminantTransportV1.ApplyMassFlux(
            stocks,
            [
                new EnvironmentFluxEdgeV1(cellA, cellB, 20),
                new EnvironmentFluxEdgeV1(cellB, cellC, 10),
            ]);
        Require(result[cellA] == 80 && result[cellB] == 60 && result[cellC] == 10,
            "domain.environment.contaminant.mass: contaminant transport stock mismatch.");
        Require(result.Values.Sum() == stocks.Values.Sum(),
            "domain.environment.contaminant.mass: contaminant mass must be conserved without explicit source/sink.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
