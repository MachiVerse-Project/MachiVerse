using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ProductionCoreSnapshotSectionProofV1(
    ulong BasisStep,
    int CoreSectionCount,
    int DurableOperationCount,
    int ScheduledOperationCount,
    int CrossDomainTransactionCount);

/// <summary>
/// Gate 3 Step 1 proof seam. Captures the mutable Core recovery authority from the same SQLite
/// read transaction after the production Step COMMIT, reconstructs the three supplemental owner
/// materials from normative QA-04 authority, and emits/verifies exactly the six Core sections.
/// Domain partition sections and exact-103 encoding are intentionally outside this step.
/// </summary>
public static class Qa04ProductionCoreSnapshotSectionProofRunnerV1
{
    public static async Task<Qa04ProductionCoreSnapshotSectionProofV1> VerifyAsync(
        WorldStateV1 authoritativeState,
        SqlitePersistenceStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authoritativeState);
        ArgumentNullException.ThrowIfNull(store);

        var recoveryCut = await store.ReadSnapshotRecoveryCutAsync(cancellationToken).ConfigureAwait(false);
        if (recoveryCut.FinalizedStep != authoritativeState.Header.Step)
            throw new InvalidDataException("qa04.gate3.core-sections.finalized-step-mismatch");
        if (recoveryCut.ConfigGeneration != authoritativeState.Header.ConfigGeneration ||
            !CryptographicOperations.FixedTimeEquals(recoveryCut.ConfigDigest, authoritativeState.Diagnostic.ConfigDigest))
            throw new InvalidDataException("qa04.gate3.core-sections.config-cut-mismatch");

        var transactions = CoreOperationStateSnapshotCutV2.DecodeTransactions(
            recoveryCut.CrossDomainTransactions,
            recoveryCut.FinalizedStep);
        var detailMaterial = Qa04DetailRegionCanonicalAuthorityV1.MaterializeCanonical();
        var detailDirectory = new DetailDirectoryV1(
            detailMaterial.RegionsByTile,
            Array.Empty<DetailTransitionCandidateV1>());
        var config = Qa04ReferenceConfigAuthorityV1.CreateCanonical();
        var registry = StandardDomainRegistryAuthorityV1.Generation1;

        var ownerCut = CoreSnapshotOwnerMaterialCutV1.CreateV2(
            authoritativeState,
            recoveryCut.DurableOperations,
            recoveryCut.ScheduledOperations,
            transactions,
            new IFrozenCoreSnapshotOwnerMaterialV1[]
            {
                FrozenCoreConfigSnapshotOwnerV1.Freeze(authoritativeState.Header.Step, config),
                FrozenDetailDirectorySnapshotOwnerV1.Freeze(authoritativeState.Header.Step, detailDirectory),
                FrozenDomainRegistrySnapshotOwnerV1.Freeze(authoritativeState.Header.Step, registry),
            });

        var sections = CoreSnapshotProductionSectionProviderV1.CreateAllSixV2(ownerCut);
        CoreSnapshotProductionSectionProviderV1.VerifyAllSixV2(
            sections,
            authoritativeState.Header.Step,
            authoritativeState.Header.ConfigGeneration);
        if (sections.Count != 6)
            throw new InvalidDataException("qa04.gate3.core-sections.count-mismatch");

        return new Qa04ProductionCoreSnapshotSectionProofV1(
            authoritativeState.Header.Step,
            sections.Count,
            recoveryCut.DurableOperations.Count,
            recoveryCut.ScheduledOperations.Count,
            transactions.Count);
    }
}
