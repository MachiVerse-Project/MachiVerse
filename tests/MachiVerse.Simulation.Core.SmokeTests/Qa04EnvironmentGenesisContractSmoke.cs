using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04EnvironmentGenesisContractSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04EnvironmentGenesisContractV1.ValidateCanonicalContract();

        Require(Qa04EnvironmentGenesisContractV1.MaterialClass.Value == "perf.rock", "Environment material token drifted.");
        Require(Qa04EnvironmentGenesisContractV1.SoilClass.Value == "perf.soil", "Environment soil token drifted.");
        Require(Qa04EnvironmentGenesisContractV1.ResourceKind.Value == "perf.resource", "Environment resource token drifted.");
        Require(Qa04EnvironmentGenesisContractV1.ClimateRegime.Value == "perf.climate", "Environment climate token drifted.");
        Require(Qa04EnvironmentGenesisContractV1.WeatherClass.Value == "perf.clear", "Environment weather token drifted.");
        Require(Qa04EnvironmentGenesisContractV1.WaterBodyClass.Value == "perf.channel", "Environment water-body token drifted.");
        Require(Qa04EnvironmentGenesisContractV1.SpeciesOrCohort.Value == "perf.cohort", "Environment cohort token drifted.");
        Require(Qa04EnvironmentGenesisContractV1.ContaminantKind.Value == "perf.marker", "Environment contaminant token drifted.");
        Require(Qa04EnvironmentGenesisContractV1.HazardKind.Value == "perf.synthetic-hazard", "Environment hazard token drifted.");
        Require(Qa04EnvironmentGenesisContractV1.MaterializationKind.Value == "perf.genesis", "Environment genesis lineage token drifted.");
        Require(Qa04EnvironmentGenesisContractV1.D1MaterializationKind.Value == "perf.aggregate-d1", "Environment D1 lineage token drifted.");
        var gasTotal = Qa04EnvironmentGenesisContractV1.AtmosphereGasPpb.Aggregate(
            0UL,
            static (total, pair) => checked(total + pair.Value));
        Require(gasTotal == 1_000_000_000UL,
            "Environment atmosphere gas composition must total one billion ppb.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
