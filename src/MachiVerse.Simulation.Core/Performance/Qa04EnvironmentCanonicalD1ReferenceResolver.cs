using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Fail-closed resolver for canonical Environment D1 materialization. D1 payloads may retain
/// canonical D0 relation refs, while D1 lineage subjects resolve against the D1 material set.
/// Arbitrary IDs are never accepted merely because the partition name is registered.
/// </summary>
public sealed class Qa04EnvironmentCanonicalD1ReferenceResolverV1 : IDomainRecordSchemaResolverV1
{
    private readonly IReadOnlySet<OpaqueId128> _tileScopeIds;
    private readonly IReadOnlyDictionary<string, Lazy<ReferenceSets>> _environmentIds;

    public Qa04EnvironmentCanonicalD1ReferenceResolverV1()
    {
        Qa04EnvironmentReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();

        _tileScopeIds = Enumerable.Range(0, Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount)
            .Select(static tile => Qa04SpatialTileScopeAuthorityV1.ScopeId(checked((ushort)tile)))
            .ToHashSet();

        _environmentIds = Qa04EnvironmentReferenceDecompositionV1.Partitions
            .ToDictionary(
                static slice => slice.PartitionId.Value,
                static slice => new Lazy<ReferenceSets>(
                    () => BuildSets(slice),
                    LazyThreadSafetyMode.ExecutionAndPublication),
                StringComparer.Ordinal);
    }

    public bool Exists(PartitionRecordRefV1 reference)
    {
        if (reference.RecordId.IsZero) return false;
        if (reference.PartitionId.Value == SpatialScopeRegistryPayloadV1.PartitionId)
            return _tileScopeIds.Contains(reference.RecordId);
        if (!_environmentIds.TryGetValue(reference.PartitionId.Value, out var lazy))
            return false;
        var sets = lazy.Value;
        return sets.D0.Contains(reference.RecordId) || sets.D1.Contains(reference.RecordId);
    }

    public bool TryGetRecordSchema(PartitionRecordRefV1 reference, out SchemaRefV1 schema)
    {
        if (!Exists(reference))
        {
            schema = default;
            return false;
        }
        schema = StandardDomainPartitionRegistry.Get(reference.PartitionId.Value).RecordSchema;
        return true;
    }

    public void ValidateFullCoverage()
    {
        ulong d0 = 0;
        ulong d1 = 0;
        foreach (var slice in Qa04EnvironmentReferenceDecompositionV1.Partitions)
        {
            var sets = _environmentIds[slice.PartitionId.Value].Value;
            if ((ulong)sets.D0.Count != slice.D0Count)
                throw new InvalidDataException($"qa04.environment.d1-resolver-d0-count:{slice.PartitionId.Value}");
            if ((ulong)sets.D1.Count != slice.D1Count)
                throw new InvalidDataException($"qa04.environment.d1-resolver-d1-count:{slice.PartitionId.Value}");
            d0 = checked(d0 + (ulong)sets.D0.Count);
            d1 = checked(d1 + (ulong)sets.D1.Count);
        }
        if (d0 != Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count)
            throw new InvalidDataException("qa04.environment.d1-resolver-d0-total");
        if (d1 != Qa04EnvironmentReferenceDecompositionV1.CanonicalD1Count)
            throw new InvalidDataException("qa04.environment.d1-resolver-d1-total");
        if (_tileScopeIds.Count != Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount)
            throw new InvalidDataException("qa04.environment.d1-resolver-tile-scope-count");
    }

    private static ReferenceSets BuildSets(Qa04EnvironmentPartitionDecompositionV1 slice)
    {
        var d0 = new HashSet<OpaqueId128>(checked((int)slice.D0Count));
        for (ulong local = 0; local < slice.D0Count; local++)
        {
            var binding = Qa04EnvironmentReferenceDecompositionV1.BindD0(checked(slice.D0StartOrdinal + local));
            if (binding.PartitionId != slice.PartitionId || !d0.Add(binding.Descriptor.RecordId))
                throw new InvalidDataException($"qa04.environment.d1-resolver-d0-id-drift:{slice.PartitionId.Value}");
        }

        var d1 = new HashSet<OpaqueId128>(checked((int)slice.D1Count));
        for (ulong local = 0; local < slice.D1Count; local++)
        {
            var binding = Qa04EnvironmentReferenceDecompositionV1.BindD1(checked(slice.D1StartOrdinal + local));
            if (binding.PartitionId != slice.PartitionId || !d1.Add(binding.Descriptor.RecordId))
                throw new InvalidDataException($"qa04.environment.d1-resolver-d1-id-drift:{slice.PartitionId.Value}");
        }
        return new ReferenceSets(d0, d1);
    }

    private sealed record ReferenceSets(IReadOnlySet<OpaqueId128> D0, IReadOnlySet<OpaqueId128> D1);
}
