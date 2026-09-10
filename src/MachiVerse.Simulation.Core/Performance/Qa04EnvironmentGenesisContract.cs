using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Fixed perf.reference.v1 Environment genesis vocabulary and atmosphere gas composition from the
/// Alpha 1.1 decomposition contract. These values are benchmark-only and are not gameplay defaults.
/// </summary>
public static class Qa04EnvironmentGenesisContractV1
{
    public static readonly StableToken MaterialClass = new("perf.rock");
    public static readonly StableToken SoilClass = new("perf.soil");
    public static readonly StableToken ResourceKind = new("perf.resource");
    public static readonly StableToken ClimateRegime = new("perf.climate");
    public static readonly StableToken WeatherClass = new("perf.clear");
    public static readonly StableToken WaterBodyClass = new("perf.channel");
    public static readonly StableToken SpeciesOrCohort = new("perf.cohort");
    public static readonly StableToken ContaminantKind = new("perf.marker");
    public static readonly StableToken HazardKind = new("perf.synthetic-hazard");
    public static readonly StableToken MaterializationKind = new("perf.genesis");
    public static readonly StableToken D1MaterializationKind = new("perf.aggregate-d1");

    private static readonly IReadOnlyList<KeyValuePair<string, uint>> AtmosphereGasPpbValue = Array.AsReadOnly(new[]
    {
        new KeyValuePair<string, uint>("perf.gas-a", 780_000_000u),
        new KeyValuePair<string, uint>("perf.gas-b", 210_000_000u),
        new KeyValuePair<string, uint>("perf.gas-c", 10_000_000u),
    });

    public static IReadOnlyList<KeyValuePair<string, uint>> AtmosphereGasPpb => AtmosphereGasPpbValue;

    public static void ValidateCanonicalContract()
    {
        var tokens = new[]
        {
            MaterialClass,
            SoilClass,
            ResourceKind,
            ClimateRegime,
            WeatherClass,
            WaterBodyClass,
            SpeciesOrCohort,
            ContaminantKind,
            HazardKind,
            MaterializationKind,
            D1MaterializationKind,
        };
        if (tokens.Select(static token => token.Value).Distinct(StringComparer.Ordinal).Count() != tokens.Length)
            throw new InvalidDataException("qa04.environment.genesis-token-duplicate");
        if (AtmosphereGasPpbValue.Count != 3 ||
            AtmosphereGasPpbValue[0].Key != "perf.gas-a" || AtmosphereGasPpbValue[0].Value != 780_000_000u ||
            AtmosphereGasPpbValue[1].Key != "perf.gas-b" || AtmosphereGasPpbValue[1].Value != 210_000_000u ||
            AtmosphereGasPpbValue[2].Key != "perf.gas-c" || AtmosphereGasPpbValue[2].Value != 10_000_000u)
            throw new InvalidDataException("qa04.environment.genesis-gas-map-drift");
        if (AtmosphereGasPpbValue.Sum(static pair => (ulong)pair.Value) != 1_000_000_000UL)
            throw new InvalidDataException("qa04.environment.genesis-gas-map-total");
    }
}
