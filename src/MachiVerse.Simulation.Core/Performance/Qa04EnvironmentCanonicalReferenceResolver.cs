using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Fail-closed resolver for the canonical Environment D0 material set and its shared TileScope
/// authority. It accepts only IDs that are members of the reference-world decomposition; a valid
/// standard partition name alone never makes an arbitrary RecordId authoritative.
/// </summary>
public sealed class Qa04EnvironmentCanonicalD0ReferenceResolverV1 : IDomainRecordSchemaResolverV1
{
    private readonly IReadOnlySet<OpaqueId128> _tileScopeIds;
    private readonly IReadOnlyDictionary<string, Lazy<IReadOnlySet<OpaqueId128>>> _environmentIds;

    public Qa04EnvironmentCanonicalD0ReferenceResolverV1()
    {
        Qa04EnvironmentReferenceDecompositionV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();

        _tileScopeIds = Enumerable.Range(0, Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount)
            .Select(static tile => Qa04SpatialTileScopeAuthorityV1.ScopeId(checked((ushort)tile)))
            .ToHashSet();

        _environmentIds = Qa04EnvironmentReferenceDecompositionV1.Partitions
            .ToDictionary(
                static slice => slice.PartitionId.Value,
                static slice => new Lazy<IReadOnlySet<OpaqueId128>>(
                    () => BuildD0PartitionIds(slice),
                    LazyThreadSafetyMode.ExecutionAndPublication),
                StringComparer.Ordinal);
    }

    public bool Exists(PartitionRecordRefV1 reference)
    {
        if (reference.RecordId.IsZero) return false;
        if (reference.PartitionId.Value == SpatialScopeRegistryPayloadV1.PartitionId)
            return _tileScopeIds.Contains(reference.RecordId);
        return _environmentIds.TryGetValue(reference.PartitionId.Value, out var ids) &&
               ids.Value.Contains(reference.RecordId);
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

    public void ValidateFullD0Coverage()
    {
        ulong total = 0;
        foreach (var slice in Qa04EnvironmentReferenceDecompositionV1.Partitions)
        {
            var ids = _environmentIds[slice.PartitionId.Value].Value;
            if ((ulong)ids.Count != slice.D0Count)
                throw new InvalidDataException($"qa04.environment.d0-resolver-partition-count:{slice.PartitionId.Value}");
            total = checked(total + (ulong)ids.Count);
        }
        if (total != Qa04EnvironmentReferenceDecompositionV1.CanonicalD0Count)
            throw new InvalidDataException("qa04.environment.d0-resolver-total-count");
        if (_tileScopeIds.Count != Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount)
            throw new InvalidDataException("qa04.environment.d0-resolver-tile-scope-count");
    }

    private static IReadOnlySet<OpaqueId128> BuildD0PartitionIds(Qa04EnvironmentPartitionDecompositionV1 slice)
    {
        var ids = new HashSet<OpaqueId128>(checked((int)slice.D0Count));
        for (ulong local = 0; local < slice.D0Count; local++)
        {
            var binding = Qa04EnvironmentReferenceDecompositionV1.BindD0(checked(slice.D0StartOrdinal + local));
            if (binding.PartitionId != slice.PartitionId || !ids.Add(binding.Descriptor.RecordId))
                throw new InvalidDataException($"qa04.environment.d0-resolver-id-drift:{slice.PartitionId.Value}");
        }
        return ids;
    }
}
