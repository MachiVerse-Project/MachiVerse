using System.Runtime.CompilerServices;

internal static class Admin04SmokeBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
        => Admin04OperationSmoke.Run();
}
