using System.Runtime.CompilerServices;

internal static class Sim15SmokeBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Sim15ObservabilitySmoke.RunAsync().GetAwaiter().GetResult();
        Console.WriteLine("SIM-15 Core observability / telemetry smoke tests passed.");
    }
}
