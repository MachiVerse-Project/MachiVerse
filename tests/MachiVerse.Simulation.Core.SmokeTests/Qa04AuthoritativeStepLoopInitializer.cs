using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04ReducedTypedAuthorityInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var materialized = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(128);
        var typedAuthorities = Qa04ReducedWorldTypedAuthorityV1.BindAll97(materialized);
        Require(typedAuthorities.CanonicalAuthorities.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
            "Reduced QA-04 world must bind all 97 typed Domain roots.");
        Require(typedAuthorities.CanonicalAuthorities.Sum(static value => checked((long)value.ActualItemCount)) == 128,
            "Reduced QA-04 typed authority must contain only the 128 actual Resident records.");
        Require(typedAuthorities.CanonicalAuthorities.Count(static value => value.ActualItemCount != 0) == 1,
            "Reduced QA-04 typed authority fixture must have exactly one non-empty partition.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
