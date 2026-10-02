using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Configuration;

internal static class Alpha11StepRateDefaultSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var coordinator = new CoreConfigCoordinator();
        var config = coordinator.LoadStartup("""
[meta]
format = "machiverse-config"
schema_version = "1.0"
component = "simulation-core"
""");

        Require(
            config.Get<long>("simulation.step-rate.numerator") == 10 &&
            config.Get<long>("simulation.step-rate.denominator") == 1,
            "Alpha 1.1 default Simulation StepRate must be 10/1 steps per second.");

        Require(
            CoreConfigSchema.Fields["simulation.step-rate.numerator"].Impact == ConfigImpact.Simulation &&
            CoreConfigSchema.Fields["simulation.step-rate.numerator"].Mutability == ConfigMutability.RuntimeSafe,
            "Alpha 1.1 StepRate must remain a runtime-safe simulation-affecting Config field.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
