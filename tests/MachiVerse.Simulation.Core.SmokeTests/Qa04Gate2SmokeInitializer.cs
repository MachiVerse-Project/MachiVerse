using System.Runtime.CompilerServices;

internal static class Qa04Gate2SmokeInitializer
{
    [ModuleInitializer]
    internal static void Register()
    {
        Qa04CanonicalOperationMutationBatchSmoke.Run();
        Qa04CanonicalOperationPartitionCandidatesSmoke.Run();
    }
}
