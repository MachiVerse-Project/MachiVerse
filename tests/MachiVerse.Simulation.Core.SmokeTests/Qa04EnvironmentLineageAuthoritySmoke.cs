using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;

internal static class Qa04EnvironmentLineageAuthoritySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04EnvironmentLineageAuthorityV1.ValidateCanonicalContract();

        var d0Slice = Qa04EnvironmentReferenceDecompositionV1.Get(Qa04EnvironmentLineageAuthorityV1.LineagePartitionId);
        for (ulong local = 0; local < d0Slice.D0Count; local++)
        {
            var binding = Qa04EnvironmentReferenceDecompositionV1.BindD0(d0Slice.D0StartOrdinal + local);
            var subject = Qa04EnvironmentLineageAuthorityV1.ResolveD0Subject(binding);
            Require(subject.PartitionId.Value != Qa04EnvironmentLineageAuthorityV1.LineagePartitionId && !subject.RecordId.IsZero,
                "Environment D0 lineage subject must target a canonical non-lineage record.");
            var pool = Qa04EnvironmentLineageAuthorityV1.D0SubjectPool(binding.Descriptor.RegionalTileIndex);
            Require(pool.Contains(subject),
                "Environment D0 lineage subject must come from the descriptor tile's canonical pool.");
        }

        for (ulong local = 0; local < d0Slice.D1Count; local++)
        {
            var binding = Qa04EnvironmentReferenceDecompositionV1.BindD1(d0Slice.D1StartOrdinal + local);
            var subject = Qa04EnvironmentLineageAuthorityV1.ResolveD1Subject(binding);
            Require(subject.PartitionId.Value != Qa04EnvironmentLineageAuthorityV1.LineagePartitionId && !subject.RecordId.IsZero,
                "Environment D1 lineage subject must target a canonical non-lineage record.");
            var pool = Qa04EnvironmentLineageAuthorityV1.D1SubjectPool(binding.Descriptor.RegionalTileIndex);
            Require(pool.Contains(subject),
                "Environment D1 lineage subject must come from the descriptor tile's canonical pool.");

            var parents = Qa04EnvironmentLineageAuthorityV1.ResolveD1Parents(binding);
            Require(parents.Count == Qa04EnvironmentLineageAuthorityV1.D1ParentCount &&
                    parents.Distinct().Count() == Qa04EnvironmentLineageAuthorityV1.D1ParentCount &&
                    parents.All(static parent => parent.PartitionId.Value == Qa04EnvironmentLineageAuthorityV1.LineagePartitionId),
                "Environment D1 lineage parents must be the exact four D0 lineage sources.");
            var expected = binding.SourceD0GlobalOrdinals
                .Select(Qa04EnvironmentReferenceDecompositionV1.BindD0)
                .Select(static source => (source.PartitionId.Value, source.Descriptor.RecordId))
                .OrderBy(static value => value.Value, StringComparer.Ordinal)
                .ThenBy(static value => value.RecordId)
                .ToArray();
            Require(parents.Select(static parent => (parent.PartitionId.Value, parent.RecordId)).SequenceEqual(expected),
                "Environment D1 lineage parent closure drifted from decomposition sources.");
        }

        var firstD0 = Qa04EnvironmentReferenceDecompositionV1.BindD0(d0Slice.D0StartOrdinal);
        var firstD1 = Qa04EnvironmentReferenceDecompositionV1.BindD1(d0Slice.D1StartOrdinal);
        Require(Qa04EnvironmentLineageAuthorityV1.ResolveD0Subject(firstD0) ==
                Qa04EnvironmentLineageAuthorityV1.ResolveD0Subject(firstD0),
            "Environment D0 lineage subject selection must be deterministic.");
        Require(Qa04EnvironmentLineageAuthorityV1.ResolveD1Subject(firstD1) ==
                Qa04EnvironmentLineageAuthorityV1.ResolveD1Subject(firstD1),
            "Environment D1 lineage subject selection must be deterministic.");

        var foreignD0 = Qa04EnvironmentReferenceDecompositionV1.BindD0(0);
        RequireThrows<InvalidDataException>(
            () => Qa04EnvironmentLineageAuthorityV1.ResolveD0Subject(foreignD0),
            "Environment lineage authority must reject a non-lineage D0 binding.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
