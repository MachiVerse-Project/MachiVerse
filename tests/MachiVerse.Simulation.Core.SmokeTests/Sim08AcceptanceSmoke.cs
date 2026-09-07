using System.Runtime.CompilerServices;

internal static class Sim08AcceptanceSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Sim08CollisionSmoke.Run();
        Sim08CrossOwnerIntentSmoke.Run();
        Sim08HandoffSmoke.Run();
        Sim08PrimitiveCollisionSmoke.Run();
        Sim08SequentialImpulseSmoke.Run();
        Sim08SpatialIntentSmoke.Run();
        Sim08StateRuntimeSmoke.RunAsync().GetAwaiter().GetResult();
    }
}
