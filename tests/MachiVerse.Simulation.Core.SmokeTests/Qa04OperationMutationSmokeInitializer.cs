using System.Runtime.CompilerServices;

internal static class Qa04OperationMutationSmokeInitializer
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04ResidentActionApplicationSmoke.Run();
        Qa04PhysicalMoveApplicationSmoke.Run();
    }
}
