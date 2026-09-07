using System.Runtime.CompilerServices;

internal static class Sim06SmokeEntryPoint
{
    [ModuleInitializer]
    internal static void Initialize()
        => Sim06StepCoordinatorSmoke.RunAsync().GetAwaiter().GetResult();
}
