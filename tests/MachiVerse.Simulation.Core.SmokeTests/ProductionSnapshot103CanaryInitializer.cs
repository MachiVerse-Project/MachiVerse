using System.Runtime.CompilerServices;

internal static class ProductionSnapshot103CanaryInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
        => ProductionSnapshot103CanarySmoke.VerifyComposition();
}
