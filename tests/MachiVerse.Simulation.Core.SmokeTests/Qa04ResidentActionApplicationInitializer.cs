using System.Runtime.CompilerServices;

internal static class Qa04ResidentActionApplicationInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
        => Qa04ResidentActionApplicationSmoke.Run();
}
