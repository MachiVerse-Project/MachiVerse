using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.Spatial;

internal static class Sim07EnvironmentProcessesSmoke
{
    internal static async Task RunAsync()
    {
        var cellA = new SpatialCellKeyV1(0, -1, 0, 0);
        var cellB = new SpatialCellKeyV1(0, 0, 0, 0);
        var cellC = new SpatialCellKeyV1(0, 1, 0, 0);

        await VerifyGroundwaterJacobiAsync(cellA, cellB);
        VerifySurfaceWaterAllocation(cellA, cellB, cellC);
        VerifyOceanConservation(cellA, cellB);
        VerifyEcologyOrderIndependence();
        VerifyContaminantMass(cellA, cellB, cellC);
    }

    private static async Task VerifyGroundwaterJacobiAsync(SpatialCellKeyV1 cellA, SpatialCellKeyV1 cellB)
    {
        var initial = new Dictionary<SpatialCellKeyV1, long>
        {
            [cellB] = 200,
            [cellA] = 100,
        };

        string? baseline = null;
        foreach (var workerCount in new[] { 1, 4, 8, 16 })
        {
            var result = await GroundwaterJacobiV1.RunAsync(
                initial,
                (cell, previous) => cell == cellA ? previous[cellB] : previous[cellA],
                workerCount);
            Require(result[cellA] == 100 && result[cellB] == 200,
                "domain.environment.groundwater.jacobi: exactly 16 previous-buffer iterations must return the two-cell swap fixture to its basis state.");
            var signature = string.Join('|', result.OrderBy(static pair => pair.Key).Select(static pair => $"{pair.Key}:{pair.Value}"));
            baseline ??= signature;
            Require(signature == baseline,
                $"domain.environment.groundwater.jacobi: worker-count={workerCount} changed the fixed-iteration result.");
        }
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
        var stocks = new Dictionary<SpatialCellKeyV1, OceanConservedStockV1>
        {
            [cellB] = new(50, -3, 2, 1, 500, 700),
            [cellA] = new(100, 10, -5, 4, 1000, 2000),
        };
        var transfer = new OceanConservedStockV1(20, 4, -2, 1, 100, 300);
        var result = OceanConservativeFluxBatchV1.Apply(
            stocks,
            [new OceanFluxV1(cellA, cellB, transfer)]);
        var reversedInput = OceanConservativeFluxBatchV1.Apply(
            new Dictionary<SpatialCellKeyV1, OceanConservedStockV1>
            {
                [cellA] = stocks[cellA],
                [cellB] = stocks[cellB],
            },
            [new OceanFluxV1(cellA, cellB, transfer)]);

        Require(result[cellA] == new OceanConservedStockV1(80, 6, -3, 3, 900, 1700),
            "domain.environment.ocean.flux: source conserved stock mismatch.");
        Require(result[cellB] == new OceanConservedStockV1(70, 1, 0, 2, 600, 1000),
            "domain.environment.ocean.flux: target conserved stock mismatch.");
        Require(result.OrderBy(static pair => pair.Key).SequenceEqual(reversedInput.OrderBy(static pair => pair.Key)),
            "domain.environment.ocean.flux: input map insertion order changed the result.");
    }

    private static void VerifyEcologyOrderIndependence()
    {
        var seed = new WorldSeed256(new byte[32]);
        var worldId = OpaqueId128.Parse("00000000000000000000000000000701");
        var cohortA = OpaqueId128.Parse("00000000000000000000000000000711");
        var cohortB = OpaqueId128.Parse("00000000000000000000000000000712");
        var cohorts = new[]
        {
            (Id: cohortA, Population: 101UL, BirthRate: 123_456U, DeathRate: 23_456U),
            (Id: cohortB, Population: 203UL, BirthRate: 87_654U, DeathRate: 12_345U),
        };

        var forward = cohorts.ToDictionary(
            static cohort => cohort.Id,
            cohort => EcologyPopulationV1.Transition(
                seed, worldId, 77, cohort.Id, cohort.Population, cohort.BirthRate, cohort.DeathRate));
        var reverse = cohorts.Reverse().ToDictionary(
            static cohort => cohort.Id,
            cohort => EcologyPopulationV1.Transition(
                seed, worldId, 77, cohort.Id, cohort.Population, cohort.BirthRate, cohort.DeathRate));

        Require(forward.OrderBy(static pair => pair.Key).SequenceEqual(reverse.OrderBy(static pair => pair.Key)),
            "domain.environment.ecology.random: cohort iteration order changed addressable stochastic realization.");
        Require(EcologyPopulationV1.Transition(seed, worldId, 77, cohortA, 10, 0, 1_000_000).CandidatePopulation == 0,
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
