using System.Runtime.CompilerServices;

internal static class Sim06SmokeRegistration
{
    [ModuleInitializer]
    internal static void Run()
    {
        Sim06StepCoordinatorSmoke.RunAsync().GetAwaiter().GetResult();
        Sim06DurableFinalizationSmoke.RunAsync().GetAwaiter().GetResult();
    }
}
