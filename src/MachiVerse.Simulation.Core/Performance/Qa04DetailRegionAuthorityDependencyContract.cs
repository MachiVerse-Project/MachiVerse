using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public enum Qa04DetailRegionAuthorityDependencyKindV1 : byte
{
    PartitionMaterialization = 1,
    GenesisProfileState = 2,
}

public sealed record Qa04DetailRegionAuthorityDependencyV1(
    StableToken DependencyId,
    Qa04DetailRegionAuthorityDependencyKindV1 Kind,
    StableToken FailureCode);

/// <summary>
/// Progress contract for canonical spatial.detail_regions authority required by the
/// perf.reference.v1 detail-transition workload.
///
/// The two formerly undefined surfaces are now fixed by the approved Alpha 1.1 benchmark authority
/// and implemented through Qa04DetailRegionCanonicalAuthorityV1: one actual DetailRegion per 4,096
/// canonical TileScopes and the exact D2-baseline / 1,424 CurrentLevel override genesis profile.
/// Production Snapshot/recovery and full 1,424-request binding proof cover this authority, so the
/// direct dependency contract is intentionally closed while retaining the historical enum surface.
/// </summary>
public static class Qa04DetailRegionAuthorityDependencyContractV1
{
    public const int CanonicalTileRegionCount = 4_096;
    public const ulong CanonicalRequestedOverrideCount = 1_424;

    private static readonly IReadOnlyList<Qa04DetailRegionAuthorityDependencyV1> BlockersValue =
        Array.AsReadOnly(Array.Empty<Qa04DetailRegionAuthorityDependencyV1>());

    public static IReadOnlyList<Qa04DetailRegionAuthorityDependencyV1> Blockers => BlockersValue;

    public static void ValidateCanonicalContract()
    {
        Qa04CanonicalDetailTransitionBindingV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();
        Qa04DetailRegionCanonicalAuthorityV1.ValidateCanonicalContract();

        var identity = StandardDomainPartitionRegistry.Get(SpatialDetailRegionsPayloadV1.PartitionId);
        if (identity.OwnerDomain.Value != "spatial" ||
            SpatialDetailRegionsPayloadV1.PartitionId != "spatial.detail_regions")
            throw new InvalidDataException("qa04.workload.detail-region-partition-contract-drift");
        if (Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount != CanonicalTileRegionCount ||
            Qa04ReferenceLoadV1.RegionalTileCount != CanonicalTileRegionCount ||
            Qa04CanonicalDetailTransitionBindingV1.CanonicalRequestCount != CanonicalRequestedOverrideCount ||
            Qa04DetailRegionCanonicalAuthorityV1.CanonicalRegionCount != CanonicalTileRegionCount ||
            Qa04DetailRegionCanonicalAuthorityV1.CanonicalOverrideCount != CanonicalRequestedOverrideCount)
            throw new InvalidDataException("qa04.workload.detail-region-canonical-count-drift");

        var requirements = Qa04CanonicalDetailTransitionBindingV1.CanonicalRequirements().ToArray();
        if ((ulong)requirements.Length != CanonicalRequestedOverrideCount ||
            requirements.Select(static requirement => requirement.TileIndex).Distinct().Count() != requirements.Length)
            throw new InvalidDataException("qa04.workload.detail-region-request-coverage-drift");
        foreach (var requirement in requirements)
        {
            if (requirement.SpatialScopeId != Qa04SpatialTileScopeAuthorityV1.ScopeId(requirement.TileIndex))
                throw new InvalidDataException("qa04.workload.detail-region-scope-authority-drift");
        }

        if (BlockersValue.Count != 0)
            throw new InvalidDataException("qa04.workload.detail-region-dependency-contract-drift");
    }
}
