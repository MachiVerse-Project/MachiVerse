using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Canonical perf.reference.v1 subject/parent authority for Environment lineage records.
/// Subject pools exclude environment.environment_lineage itself and are selected from the lineage
/// descriptor's own RegionalTileIndex in stable partition/RecordId order.
/// </summary>
public static class Qa04EnvironmentLineageAuthorityV1
{
    public const string LineagePartitionId = "environment.environment_lineage";
    public const uint D0Generation = 1;
    public const int D1ParentCount = 4;

    private static readonly IReadOnlyList<Qa04EnvironmentPartitionDecompositionV1> SubjectPartitions =
        Array.AsReadOnly(Qa04EnvironmentReferenceDecompositionV1.Partitions
            .Where(static slice => slice.PartitionId.Value != LineagePartitionId)
            .OrderBy(static slice => slice.PartitionId.Value, StringComparer.Ordinal)
            .ToArray());

    private static readonly Lazy<IReadOnlyDictionary<ushort, IReadOnlyList<PartitionRecordRefV1>>> D0Subjects =
        new(() => BuildSubjectPools(d1: false), LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<IReadOnlyDictionary<ushort, IReadOnlyList<PartitionRecordRefV1>>> D1Subjects =
        new(() => BuildSubjectPools(d1: true), LazyThreadSafetyMode.ExecutionAndPublication);

    public static void ValidateCanonicalContract()
    {
        Qa04EnvironmentReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04EnvironmentGenesisContractV1.ValidateCanonicalContract();
        if (SubjectPartitions.Count != 12 ||
            SubjectPartitions.Any(static slice => slice.PartitionId.Value == LineagePartitionId) ||
            D0Generation != 1 || D1ParentCount != Qa04EnvironmentReferenceDecompositionV1.D0SourcesPerD1)
            throw new InvalidDataException("qa04.environment.lineage-authority-contract-drift");
    }

    public static PartitionRecordRefV1 ResolveD0Subject(Qa04EnvironmentD0BindingV1 lineageBinding)
    {
        ArgumentNullException.ThrowIfNull(lineageBinding);
        ValidateLineageD0Binding(lineageBinding);
        var pool = RequirePool(D0Subjects.Value, lineageBinding.Descriptor.RegionalTileIndex, "d0");
        return pool[checked((int)(lineageBinding.PartitionLocalOrdinal % (ulong)pool.Count))];
    }

    public static PartitionRecordRefV1 ResolveD1Subject(Qa04EnvironmentD1BindingV1 lineageBinding)
    {
        ArgumentNullException.ThrowIfNull(lineageBinding);
        ValidateLineageD1Binding(lineageBinding);
        var pool = RequirePool(D1Subjects.Value, lineageBinding.Descriptor.RegionalTileIndex, "d1");
        return pool[checked((int)(lineageBinding.PartitionLocalOrdinal % (ulong)pool.Count))];
    }

    public static IReadOnlyList<PartitionRecordRefV1> ResolveD1Parents(Qa04EnvironmentD1BindingV1 lineageBinding)
    {
        ArgumentNullException.ThrowIfNull(lineageBinding);
        ValidateLineageD1Binding(lineageBinding);
        if (lineageBinding.SourceD0GlobalOrdinals.Count != D1ParentCount)
            throw new InvalidDataException("qa04.environment.lineage-d1-parent-cardinality");

        var parents = lineageBinding.SourceD0GlobalOrdinals
            .Select(Qa04EnvironmentReferenceDecompositionV1.BindD0)
            .Select(static source =>
            {
                if (source.PartitionId.Value != LineagePartitionId)
                    throw new InvalidDataException("qa04.environment.lineage-d1-parent-foreign-partition");
                return new PartitionRecordRefV1(source.PartitionId, source.Descriptor.RecordId);
            })
            .OrderBy(static reference => reference.PartitionId.Value, StringComparer.Ordinal)
            .ThenBy(static reference => reference.RecordId)
            .ToArray();
        if (parents.Length != D1ParentCount || parents.Distinct().Count() != D1ParentCount)
            throw new InvalidDataException("qa04.environment.lineage-d1-parent-duplicate");
        return Array.AsReadOnly(parents);
    }

    public static IReadOnlyList<PartitionRecordRefV1> D0SubjectPool(ushort tileIndex)
    {
        ValidateTile(tileIndex);
        return RequirePool(D0Subjects.Value, tileIndex, "d0");
    }

    public static IReadOnlyList<PartitionRecordRefV1> D1SubjectPool(ushort tileIndex)
    {
        ValidateTile(tileIndex);
        return RequirePool(D1Subjects.Value, tileIndex, "d1");
    }

    private static IReadOnlyDictionary<ushort, IReadOnlyList<PartitionRecordRefV1>> BuildSubjectPools(bool d1)
    {
        ValidateCanonicalContractShallow();
        var mutable = new Dictionary<ushort, List<PartitionRecordRefV1>>();
        foreach (var slice in SubjectPartitions)
        {
            var count = d1 ? slice.D1Count : slice.D0Count;
            var start = d1 ? slice.D1StartOrdinal : slice.D0StartOrdinal;
            for (ulong local = 0; local < count; local++)
            {
                var bindingOrdinal = checked(start + local);
                var descriptor = d1
                    ? Qa04EnvironmentReferenceDecompositionV1.BindD1(bindingOrdinal).Descriptor
                    : Qa04EnvironmentReferenceDecompositionV1.BindD0(bindingOrdinal).Descriptor;
                var tile = descriptor.RegionalTileIndex;
                if (!mutable.TryGetValue(tile, out var list))
                {
                    list = new List<PartitionRecordRefV1>();
                    mutable.Add(tile, list);
                }
                list.Add(new PartitionRecordRefV1(slice.PartitionId, descriptor.RecordId));
            }
        }

        var result = new Dictionary<ushort, IReadOnlyList<PartitionRecordRefV1>>();
        for (ushort tile = 0; tile < Qa04ReferenceLoadV1.RegionalTileCount; tile++)
        {
            if (!mutable.TryGetValue(tile, out var candidates) || candidates.Count == 0)
                throw new InvalidDataException($"qa04.environment.lineage-{(d1 ? "d1" : "d0")}-subject-pool-empty:{tile}");
            var ordered = candidates
                .OrderBy(static reference => reference.PartitionId.Value, StringComparer.Ordinal)
                .ThenBy(static reference => reference.RecordId)
                .ToArray();
            if (ordered.Any(static reference => reference.PartitionId.Value == LineagePartitionId) ||
                ordered.Any(static reference => reference.RecordId.IsZero) ||
                ordered.Distinct().Count() != ordered.Length)
                throw new InvalidDataException($"qa04.environment.lineage-{(d1 ? "d1" : "d0")}-subject-pool-invalid:{tile}");
            result.Add(tile, Array.AsReadOnly(ordered));
        }
        return result;
    }

    private static IReadOnlyList<PartitionRecordRefV1> RequirePool(
        IReadOnlyDictionary<ushort, IReadOnlyList<PartitionRecordRefV1>> pools,
        ushort tileIndex,
        string level)
    {
        ValidateTile(tileIndex);
        if (!pools.TryGetValue(tileIndex, out var pool) || pool.Count == 0)
            throw new InvalidDataException($"qa04.environment.lineage-{level}-subject-pool-missing:{tileIndex}");
        return pool;
    }

    private static void ValidateLineageD0Binding(Qa04EnvironmentD0BindingV1 binding)
    {
        if (binding.PartitionId.Value != LineagePartitionId || binding.Descriptor.DetailLevel != DetailLevelV1.D0Entity)
            throw new InvalidDataException("qa04.environment.lineage-d0-binding-invalid");
    }

    private static void ValidateLineageD1Binding(Qa04EnvironmentD1BindingV1 binding)
    {
        if (binding.PartitionId.Value != LineagePartitionId || binding.Descriptor.DetailLevel != DetailLevelV1.D1LocalAggregate)
            throw new InvalidDataException("qa04.environment.lineage-d1-binding-invalid");
    }

    private static void ValidateTile(ushort tileIndex)
    {
        if (tileIndex >= Qa04ReferenceLoadV1.RegionalTileCount)
            throw new ArgumentOutOfRangeException(nameof(tileIndex));
    }

    private static void ValidateCanonicalContractShallow()
    {
        if (SubjectPartitions.Count != 12 || SubjectPartitions.Any(static slice => slice.PartitionId.Value == LineagePartitionId))
            throw new InvalidDataException("qa04.environment.lineage-subject-partitions-drift");
    }
}
