using System.Runtime.CompilerServices;

internal static class SpatialTerrainGeometryStagedRecoveryInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
        => SpatialTerrainGeometryStagedRecoverySmoke.RunAsync().GetAwaiter().GetResult();
}
