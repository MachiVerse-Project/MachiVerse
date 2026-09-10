using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

/// <summary>
/// Production-facing bindings for consumers of the canonical perf.reference.v1 TileScope authority.
/// Explicit resolver injection remains available on the underlying components for negative tests,
/// while release material uses these bindings to avoid synthetic scope identities.
/// </summary>
public static class Qa04CanonicalTileScopeBindingsV1
{
    public static Qa04TerrainTileAuthorityV1 MaterializeTerrainTile(ushort tileIndex)
        => Qa04TerrainRootMaterializerV1.MaterializeTile(
            tileIndex,
            Qa04SpatialTileScopeAuthorityV1.ScopeRef);

    public static IEnumerable<SpatialTerrainGeometryRecordMaterialV2> MaterializeTerrainRootsAndAnchors()
        => Qa04TerrainRootMaterializerV1.MaterializeCanonical(
            Qa04SpatialTileScopeAuthorityV1.ScopeRef);

    public static Func<ushort, OpaqueId128> CreateDetailRegionResolver(DetailDirectoryV1 directory)
        => Qa04CrossDomainTransactionDetailGuardReconstructionV1.CreateRegionResolverFromTileScopes(
            directory,
            Qa04SpatialTileScopeAuthorityV1.ScopeRef);

    public static IReadOnlyList<Qa04ActiveTransactionDetailGuardCountV1> ReconstructActiveTransactionGuardCounts(
        IEnumerable<CrossDomainTransactionStateV1> transactionStates,
        DetailDirectoryV1 directory)
        => Qa04CrossDomainTransactionDetailGuardReconstructionV1.ReconstructCountsFromTileScopes(
            transactionStates,
            directory,
            Qa04SpatialTileScopeAuthorityV1.ScopeRef);

    public static DetailDirectoryV1 RebuildActiveTransactionGuards(
        DetailDirectoryV1 recoveredDirectory,
        IEnumerable<CrossDomainTransactionStateV1> transactionStates)
        => Qa04CrossDomainTransactionDetailGuardReconstructionV1.RebuildDirectoryGuardsFromTileScopes(
            recoveredDirectory,
            transactionStates,
            Qa04SpatialTileScopeAuthorityV1.ScopeRef);

    public static void ValidateActiveTransactionGuards(
        DetailDirectoryV1 recoveredDirectory,
        IEnumerable<CrossDomainTransactionStateV1> transactionStates)
        => Qa04CrossDomainTransactionDetailGuardReconstructionV1.ValidateDirectoryMatchesTileScopeAuthority(
            recoveredDirectory,
            transactionStates,
            Qa04SpatialTileScopeAuthorityV1.ScopeRef);

    public static PartitionRecordRefV1 EnvironmentD0Scope(Qa04EnvironmentD0BindingV1 binding)
        => Qa04EnvironmentD0PartitionMaterializerV1.ResolveSpatialScope(binding);

    public static PartitionRecordRefV1 EnvironmentD1Scope(Qa04EnvironmentD1BindingV1 binding)
        => Qa04EnvironmentD1PartitionMaterializerV1.ResolveSpatialScope(binding);
}
