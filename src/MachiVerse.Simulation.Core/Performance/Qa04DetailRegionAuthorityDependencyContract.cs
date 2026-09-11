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
/// Machine-readable boundary for the remaining canonical DetailRegion authority required by the
/// perf.reference.v1 detail-transition workload.
///
/// The production spatial.detail_regions schema, 4,096 canonical TileScope records, exact 89-cadence
/// / 1,424-request workload mapping, ConfigPolicy trigger identity, and production request/admission
/// path are already fixed. What is not yet authoritative is the actual spatial.detail_regions record
/// set itself and its genesis profile state. In particular, this contract does not derive a new
/// DetailRegionId from TileScope or synthesize unspecified normal-profile domain levels.
/// </summary>
public static class Qa04DetailRegionAuthorityDependencyContractV1
{
    public const int CanonicalTileRegionCount = 4_096;
    public const ulong CanonicalRequestedOverrideCount = 1_424;

    private static readonly IReadOnlyList<Qa04DetailRegionAuthorityDependencyV1> BlockersValue =
        Array.AsReadOnly(new[]
        {
            Blocker(
                "workload.detail-region.genesis-profile-state",
                Qa04DetailRegionAuthorityDependencyKindV1.GenesisProfileState,
                "qa04.workload.detail-region-genesis-state-undefined"),
            Blocker(
                "workload.detail-region.partition-materialization",
                Qa04DetailRegionAuthorityDependencyKindV1.PartitionMaterialization,
                "qa04.workload.detail-region-partition-undefined"),
        }
        .OrderBy(static blocker => blocker.DependencyId.Value, StringComparer.Ordinal)
        .ToArray());

    public static IReadOnlyList<Qa04DetailRegionAuthorityDependencyV1> Blockers => BlockersValue;

    public static void ValidateCanonicalContract()
    {
        Qa04CanonicalDetailTransitionBindingV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();

        var identity = StandardDomainPartitionRegistry.Get(SpatialDetailRegionsPayloadV1.PartitionId);
        if (identity.OwnerDomain.Value != "spatial" ||
            SpatialDetailRegionsPayloadV1.PartitionId != "spatial.detail_regions")
            throw new InvalidDataException("qa04.workload.detail-region-partition-contract-drift");
        if (Qa04SpatialTileScopeAuthorityV1.CanonicalScopeCount != CanonicalTileRegionCount ||
            Qa04ReferenceLoadV1.RegionalTileCount != CanonicalTileRegionCount ||
            Qa04CanonicalDetailTransitionBindingV1.CanonicalRequestCount != CanonicalRequestedOverrideCount)
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

        if (BlockersValue.Count != 2 ||
            BlockersValue.Select(static blocker => blocker.DependencyId).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Select(static blocker => blocker.FailureCode).Distinct().Count() != BlockersValue.Count ||
            BlockersValue.Any(static blocker => !Enum.IsDefined(blocker.Kind)))
            throw new InvalidDataException("qa04.workload.detail-region-dependency-contract-drift");

        var ordered = BlockersValue.Select(static blocker => blocker.DependencyId.Value).ToArray();
        if (!ordered.SequenceEqual(ordered.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("qa04.workload.detail-region-dependency-order-drift");
    }

    private static Qa04DetailRegionAuthorityDependencyV1 Blocker(
        string dependencyId,
        Qa04DetailRegionAuthorityDependencyKindV1 kind,
        string failureCode)
        => new(new StableToken(dependencyId), kind, new StableToken(failureCode));
}
