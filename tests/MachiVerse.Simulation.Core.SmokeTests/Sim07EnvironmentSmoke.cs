using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.Spatial;

internal static class Sim07EnvironmentSmoke
{
    internal static void Run()
    {
        var cellA = new SpatialCellKeyV1(0, -1, 0, 0);
        var cellB = new SpatialCellKeyV1(0, 0, 0, 0);
        var cellC = new SpatialCellKeyV1(0, 1, 0, 0);
        var stocks = new Dictionary<SpatialCellKeyV1, long>
        {
            [cellC] = 0,
            [cellA] = 100,
            [cellB] = 20,
        };
        var fluxes = new[]
        {
            new EnvironmentFluxEdgeV1(cellB, cellC, 10),
            new EnvironmentFluxEdgeV1(cellA, cellB, 30),
        };

        var forward = ConservativeFluxBatchV1.Apply(stocks, fluxes);
        var reversed = ConservativeFluxBatchV1.Apply(stocks, fluxes.Reverse());
        Require(forward[cellA] == 70 && forward[cellB] == 40 && forward[cellC] == 10,
            "domain.environment.flux.conservation: shared transfers produced the wrong stocks.");
        Require(forward.OrderBy(static pair => pair.Key).SequenceEqual(reversed.OrderBy(static pair => pair.Key)),
            "domain.environment.atmosphere.permutation: flux input order changed the result.");
        Require(forward.Values.Aggregate(0L, static (sum, value) => checked(sum + value)) == 120,
            "domain.environment.flux.conservation: total stock changed without source/sink.");

        RequireReject(
            () => ConservativeFluxBatchV1.Apply(
                stocks,
                [new EnvironmentFluxEdgeV1(cellA, cellB, 101)]),
            "environment.flux-insufficient-source-stock");
        RequireReject(
            () => ConservativeFluxBatchV1.Apply(
                stocks,
                [new EnvironmentFluxEdgeV1(cellA, cellB, -1)]),
            "environment.flux-negative-amount");

        Require(ClimateRecurrenceV1.Update(0, 5, 1, 2) == 2,
            "domain.environment.climate.recurrence: 2.5 must round ties-to-even to 2.");
        Require(ClimateRecurrenceV1.Update(0, 7, 1, 2) == 4,
            "domain.environment.climate.recurrence: 3.5 must round ties-to-even to 4.");
        Require(ClimateRecurrenceV1.Update(1000, 900, 1, 4) == 975,
            "domain.environment.climate.recurrence: integer recurrence golden vector mismatch.");

        var hydrologyChoice = HydrologySelectionV1.SelectMinimumHead([
            new HydraulicHeadCandidateV1(cellC, 90),
            new HydraulicHeadCandidateV1(cellB, 80),
            new HydraulicHeadCandidateV1(cellA, 80),
        ]);
        Require(hydrologyChoice.Cell == cellA && hydrologyChoice.HydraulicHeadMm == 80,
            "domain.environment.hydrology.mass: equal-head tie must use SpatialCellKey canonical order.");

        var reverseChoice = HydrologySelectionV1.SelectMinimumHead([
            new HydraulicHeadCandidateV1(cellA, 80),
            new HydraulicHeadCandidateV1(cellB, 80),
            new HydraulicHeadCandidateV1(cellC, 90),
        ]);
        Require(reverseChoice == hydrologyChoice,
            "Hydrology minimum-head selection must be input-permutation independent.");

        Sim07EnvironmentProcessesSmoke.RunAsync().GetAwaiter().GetResult();
        Sim07AdvancedSmoke.RunAsync().GetAwaiter().GetResult();
    }

    private static void RequireReject(Func<IReadOnlyDictionary<SpatialCellKeyV1, long>> action, string expectedMessage)
    {
        try
        {
            _ = action();
        }
        catch (InvalidDataException ex) when (ex.Message == expectedMessage)
        {
            return;
        }
        throw new InvalidOperationException($"Expected SIM-07 Environment rejection: {expectedMessage}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
