using System.Runtime.CompilerServices;

internal static class Qa04CanonicalOperationBindingInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
        => Qa04CanonicalOperationBindingSmoke.Run();
}
