using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Persistence;

public sealed record CrossDomainTransactionSnapshotRecoveryResultV2(
    RecoveredCoreOperationStateV2 OperationState,
    DetailDirectoryV1 DetailDirectory);

/// <summary>
/// Recovery join for core.operation-state /2.0 and core.detail-directory. Transaction authority is
/// recovered first; detail.guard.active-transaction is then derived from recovered ACTIVE state and
/// compared against the recovered detail directory. Production binding resolves TileScope through the
/// authoritative DetailDirectory.SpatialScopeRef relation; the guard is never an independent authority.
/// </summary>
public static class CrossDomainTransactionSnapshotRecoveryV2
{
    public static CrossDomainTransactionSnapshotRecoveryResultV2 RecoverAndRebuildDetailGuardsFromTileScopes(
        CanonicalSnapshotSectionMaterialV1 operationStateSection,
        ulong snapshotStep,
        DetailDirectoryV1 recoveredDetailDirectory,
        Func<ushort, PartitionRecordRefV1> tileScopeForTile)
    {
        ArgumentNullException.ThrowIfNull(recoveredDetailDirectory);
        ArgumentNullException.ThrowIfNull(tileScopeForTile);

        var recoveredOperationState = CoreOperationStateSnapshotSectionProviderV2.Recover(
            operationStateSection,
            snapshotStep);
        var rebuilt = Qa04CrossDomainTransactionDetailGuardReconstructionV1.RebuildDirectoryGuardsFromTileScopes(
            recoveredDetailDirectory,
            recoveredOperationState.Transactions,
            tileScopeForTile);
        Qa04CrossDomainTransactionDetailGuardReconstructionV1.ValidateDirectoryMatchesTileScopeAuthority(
            rebuilt,
            recoveredOperationState.Transactions,
            tileScopeForTile);

        return new CrossDomainTransactionSnapshotRecoveryResultV2(recoveredOperationState, rebuilt);
    }

    public static void ValidateRecoveredDetailMatchesTileScopeAuthority(
        CanonicalSnapshotSectionMaterialV1 operationStateSection,
        ulong snapshotStep,
        DetailDirectoryV1 recoveredDetailDirectory,
        Func<ushort, PartitionRecordRefV1> tileScopeForTile)
    {
        ArgumentNullException.ThrowIfNull(recoveredDetailDirectory);
        ArgumentNullException.ThrowIfNull(tileScopeForTile);
        var recoveredOperationState = CoreOperationStateSnapshotSectionProviderV2.Recover(
            operationStateSection,
            snapshotStep);
        Qa04CrossDomainTransactionDetailGuardReconstructionV1.ValidateDirectoryMatchesTileScopeAuthority(
            recoveredDetailDirectory,
            recoveredOperationState.Transactions,
            tileScopeForTile);
    }

    public static CrossDomainTransactionSnapshotRecoveryResultV2 RecoverAndRebuildDetailGuards(
        CanonicalSnapshotSectionMaterialV1 operationStateSection,
        ulong snapshotStep,
        DetailDirectoryV1 recoveredDetailDirectory,
        Func<ushort, OpaqueId128> detailRegionForTile)
    {
        ArgumentNullException.ThrowIfNull(recoveredDetailDirectory);
        ArgumentNullException.ThrowIfNull(detailRegionForTile);

        var recoveredOperationState = CoreOperationStateSnapshotSectionProviderV2.Recover(
            operationStateSection,
            snapshotStep);
        var rebuilt = Qa04CrossDomainTransactionDetailGuardReconstructionV1.RebuildDirectoryGuards(
            recoveredDetailDirectory,
            recoveredOperationState.Transactions,
            detailRegionForTile);
        Qa04CrossDomainTransactionDetailGuardReconstructionV1.ValidateDirectoryMatchesAuthority(
            rebuilt,
            recoveredOperationState.Transactions,
            detailRegionForTile);

        return new CrossDomainTransactionSnapshotRecoveryResultV2(recoveredOperationState, rebuilt);
    }

    public static void ValidateRecoveredDetailMatchesTransactionAuthority(
        CanonicalSnapshotSectionMaterialV1 operationStateSection,
        ulong snapshotStep,
        DetailDirectoryV1 recoveredDetailDirectory,
        Func<ushort, OpaqueId128> detailRegionForTile)
    {
        ArgumentNullException.ThrowIfNull(recoveredDetailDirectory);
        ArgumentNullException.ThrowIfNull(detailRegionForTile);
        var recoveredOperationState = CoreOperationStateSnapshotSectionProviderV2.Recover(
            operationStateSection,
            snapshotStep);
        Qa04CrossDomainTransactionDetailGuardReconstructionV1.ValidateDirectoryMatchesAuthority(
            recoveredDetailDirectory,
            recoveredOperationState.Transactions,
            detailRegionForTile);
    }
}
