using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ProductionExact103SnapshotPersistenceProofV1(
    ulong SnapshotStep,
    int SectionCount,
    int CoreSectionCount,
    int DomainSectionCount,
    int ChunkCount,
    ulong DomainLogicalRecordCount,
    byte[] SnapshotDigest,
    byte[] PhysicalManifestDigest);

/// <summary>
/// Gate3 Step 3 proof seam. Freezes the already-published production State(S+1) against a fresh
/// same-SQLite-read recovery cut, composes exactly 6 Core + 97 Domain streaming sections, drains all
/// fragments to durable production chunk/manifest files, atomically publishes the physical Snapshot,
/// records the exact logical/physical digests in SQLite, and reads those durable authorities back.
/// Gate3 Step 4 is then invoked from the committed durable authorities only; semantic rehash remains
/// intentionally deferred to Gate3 Step 5.
/// </summary>
public static class Qa04ProductionExact103SnapshotPersistenceProofRunnerV1
{
    public static async Task<Qa04ProductionExact103SnapshotPersistenceProofV1> VerifyAsync(
        AuthoritativeStepWorldStateV1 authoritative,
        IReadOnlyList<IDomainPartitionSnapshotAuthorityV1> basisAuthorities,
        Qa04CanonicalOperationMutationStateV1 mutationState,
        SqlitePersistenceStore store,
        WorldPersistencePaths world,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authoritative);
        ArgumentNullException.ThrowIfNull(basisAuthorities);
        ArgumentNullException.ThrowIfNull(mutationState);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(world);
        if (!authoritative.IsPublishable)
            throw new InvalidDataException("qa04.gate3.exact103.authoritative-state-not-publishable");
        if (basisAuthorities.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidDataException("qa04.gate3.exact103.basis-authority-count-not-97");

        var frozenState = authoritative.State;
        var recoveryCut = await store.ReadSnapshotRecoveryCutAsync(cancellationToken).ConfigureAwait(false);
        RequireRecoveryCutMatches(frozenState, recoveryCut);

        var transactions = CoreOperationStateSnapshotCutV2.DecodeTransactions(
            recoveryCut.CrossDomainTransactions,
            recoveryCut.FinalizedStep);
        var config = Qa04ReferenceConfigAuthorityV1.CreateCanonical();
        var detailMaterial = Qa04DetailRegionCanonicalAuthorityV1.MaterializeCanonical();
        var detailDirectory = new DetailDirectoryV1(
            detailMaterial.RegionsByTile,
            Array.Empty<DetailTransitionCandidateV1>());
        var registry = StandardDomainRegistryAuthorityV1.Generation1;

        var coreOwnerCut = CoreSnapshotOwnerMaterialCutV1.CreateV2(
            frozenState,
            recoveryCut.DurableOperations,
            recoveryCut.ScheduledOperations,
            transactions,
            new IFrozenCoreSnapshotOwnerMaterialV1[]
            {
                FrozenCoreConfigSnapshotOwnerV1.Freeze(frozenState.Header.Step, config),
                FrozenDetailDirectorySnapshotOwnerV1.Freeze(frozenState.Header.Step, detailDirectory),
                FrozenDomainRegistrySnapshotOwnerV1.Freeze(frozenState.Header.Step, registry),
            });

        var domainAuthorities = Qa04ProductionDomainSnapshotAuthorityBuilderV1.CreateResultingState(
            frozenState,
            basisAuthorities,
            mutationState);
        foreach (var authority in domainAuthorities.CanonicalAuthorities)
            authority.VerifyBoundAuthority();

        var providers = StandardDomainSnapshotOwnerCompositionV1.CreateAllProviders();
        var sections = StandardSnapshotStreamingOwnerCompositionV1.CreateAll103WithTerrainV2(
            coreOwnerCut,
            domainAuthorities,
            providers);
        if (sections.Count != SnapshotManifestValidation.StandardRequiredSectionCount || sections.Count != 103)
            throw new InvalidDataException("qa04.gate3.exact103.section-count-not-103");
        var coreSectionCount = sections.Count(static section => StandardSnapshotSectionSetV1.IsCoreSection(section.SectionId));
        var domainSectionCount = sections.Count(static section => StandardDomainPartitionRegistry.TryGet(section.SectionId, out _));
        if (coreSectionCount != 6 || domainSectionCount != 97)
            throw new InvalidDataException("qa04.gate3.exact103.section-owner-count-mismatch");

        ulong domainLogicalRecordCount = 0;
        foreach (var section in sections)
        {
            if (StandardDomainPartitionRegistry.TryGet(section.SectionId, out _))
                domainLogicalRecordCount = checked(domainLogicalRecordCount + section.LogicalItemCount);
        }

        var snapshotId = RunningSnapshotCoordinatorV1.DeriveSnapshotId(
            frozenState.Header.WorldId,
            frozenState.Header.Step,
            recoveryCut.HistoryAnchor.Sequence,
            recoveryCut.HistoryAnchor.Digest,
            recoveryCut.StateContinuityToken);
        var cut = new RunningSnapshotCutV1(
            frozenState,
            snapshotId,
            recoveryCut.HistoryAnchor,
            recoveryCut.StateContinuityToken.ToArray(),
            recoveryCut.DurableOperations,
            recoveryCut.ScheduledOperations)
        {
            CrossDomainTransactions = recoveryCut.CrossDomainTransactions,
            CoreOwnerMaterial = coreOwnerCut,
        };

        var physical = SnapshotPhysicalStaging.Prepare(world, cut.SnapshotId);
        var zstd = new ZstdSnapshotChunkCompressionCodecV1();
        var staged = await CanonicalSnapshotProductionManifestDrainV1.StageRunningCutStreamingAsync(
            cut,
            physical,
            sections,
            config,
            Qa04ReferenceLoadV1.WorldSeed,
            zstdCodec: zstd,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (staged.Manifest.Logical.Sections.Count != 103 || staged.Chunks.Count == 0)
            throw new InvalidDataException("qa04.gate3.exact103.staged-material-incomplete");
        if (!File.Exists(physical.StagingManifestPath))
            throw new InvalidDataException("qa04.gate3.exact103.staging-manifest-missing");

        var committed = await CommitEvidenceCutAsync(
            cut,
            store,
            world,
            physical,
            sections,
            staged,
            cancellationToken).ConfigureAwait(false);
        if (committed.SnapshotId != cut.SnapshotId || committed.SnapshotStep != cut.SnapshotStep)
            throw new InvalidDataException("qa04.gate3.exact103.commit-receipt-mismatch");

        await VerifyDurableReadbackAsync(
            cut,
            store,
            physical,
            sections,
            staged,
            cancellationToken).ConfigureAwait(false);

        var persisted = new Qa04ProductionExact103SnapshotPersistenceProofV1(
            cut.SnapshotStep,
            sections.Count,
            coreSectionCount,
            domainSectionCount,
            staged.Chunks.Count,
            domainLogicalRecordCount,
            staged.SnapshotDigest.ToArray(),
            staged.PhysicalManifestDigest.ToArray());

        Console.WriteLine("[qa04-production] Gate3 Step4 exact-103 Snapshot recovery proof start");
        var recovered = await Qa04ProductionExact103SnapshotRecoveryProofRunnerV1.VerifyAsync(
            persisted,
            store,
            world,
            cancellationToken).ConfigureAwait(false);
        if (recovered.SnapshotStep != persisted.SnapshotStep ||
            recovered.SectionCount != persisted.SectionCount ||
            recovered.CoreSectionCount != persisted.CoreSectionCount ||
            recovered.DomainSectionCount != persisted.DomainSectionCount ||
            recovered.ChunkCount != persisted.ChunkCount ||
            recovered.DomainLogicalRecordCount != persisted.DomainLogicalRecordCount)
            throw new InvalidDataException("qa04.gate3.recovery.production-proof-mismatch");
        Console.WriteLine(
            $"[qa04-production] Gate3 Step4 exact-103 Snapshot recovery proof complete sections={recovered.SectionCount} coreSections={recovered.CoreSectionCount} domainSections={recovered.DomainSectionCount} chunks={recovered.ChunkCount} fragments={recovered.FragmentCount} logicalRecords={recovered.DomainLogicalRecordCount}");

        return persisted;
    }

    private static void RequireRecoveryCutMatches(
        WorldStateV1 frozenState,
        SnapshotRecoveryStateCutV1 recoveryCut)
    {
        if (recoveryCut.FinalizedStep != frozenState.Header.Step)
            throw new InvalidDataException("qa04.gate3.exact103.finalized-step-mismatch");
        if (recoveryCut.ConfigGeneration != frozenState.Header.ConfigGeneration ||
            !CryptographicOperations.FixedTimeEquals(recoveryCut.ConfigDigest, frozenState.Diagnostic.ConfigDigest))
            throw new InvalidDataException("qa04.gate3.exact103.config-cut-mismatch");
    }

    private static async Task<DurableSnapshotCommitResult> CommitEvidenceCutAsync(
        RunningSnapshotCutV1 cut,
        SqlitePersistenceStore store,
        WorldPersistencePaths world,
        SnapshotPhysicalPaths physical,
        IReadOnlyList<CanonicalSnapshotStreamingSectionV1> sections,
        CanonicalSnapshotProductionStageResultV1 staged,
        CancellationToken cancellationToken)
    {
        var currentAnchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        if (currentAnchor.Sequence == ulong.MaxValue)
            throw new OverflowException("HistorySequence cannot wrap.");

        var logicalDigest = staged.SnapshotDigest.ToArray();
        var physicalDigest = staged.PhysicalManifestDigest.ToArray();
        var relativeDirectory = Path.GetRelativePath(world.GenerationDirectory, physical.FinalDirectory)
            .Replace('\\', '/');
        var snapshot = new SnapshotCommitMaterial(
            cut.SnapshotId,
            cut.SnapshotStep,
            cut.HistoryAnchor,
            cut.StateContinuityToken.ToArray(),
            logicalDigest,
            physicalDigest,
            relativeDirectory);

        var payloadBytes = cut.SnapshotId.ToBytes()
            .Concat(logicalDigest)
            .Concat(physicalDigest)
            .ToArray();
        var history = HistoryRecordMaterial.Create(
            cut.FrozenState.Header.WorldId,
            checked(currentAnchor.Sequence + 1),
            currentAnchor.Digest,
            "snapshot.committed.v1",
            "core.snapshot-committed.v1",
            1,
            0,
            payloadBytes,
            writer =>
            {
                writer.WriteMapStart(7);
                writer.WriteUnsigned(0); writer.WriteBytes(cut.SnapshotId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteUnsigned(cut.SnapshotStep);
                writer.WriteUnsigned(2); writer.WriteUnsigned(cut.HistoryAnchor.Sequence);
                writer.WriteUnsigned(3); writer.WriteBytes(cut.HistoryAnchor.Digest);
                writer.WriteUnsigned(4); writer.WriteBytes(cut.StateContinuityToken);
                writer.WriteUnsigned(5); writer.WriteBytes(logicalDigest);
                writer.WriteUnsigned(6); writer.WriteBytes(physicalDigest);
            });

        return await SnapshotCommitCoordinator.CommitAsync(
            store,
            world,
            physical,
            snapshot,
            history,
            async (candidate, token) =>
            {
                var manifest = await SnapshotPhysicalManifestStagingValidationV1.ValidateAsync(
                    candidate,
                    cancellationToken: token).ConfigureAwait(false);
                SnapshotPhysicalManifestStreamingAuthorityValidationV1.RequireExpectedAuthority(
                    manifest,
                    sections,
                    cut.FrozenState,
                    cut,
                    staged.SnapshotDigest,
                    staged.PhysicalManifestDigest);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyDurableReadbackAsync(
        RunningSnapshotCutV1 cut,
        SqlitePersistenceStore store,
        SnapshotPhysicalPaths physical,
        IReadOnlyList<CanonicalSnapshotStreamingSectionV1> sections,
        CanonicalSnapshotProductionStageResultV1 staged,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(physical.FinalDirectory) || Directory.Exists(physical.StagingDirectory))
            throw new InvalidDataException("qa04.gate3.exact103.atomic-publish-mismatch");
        if (!File.Exists(physical.FinalManifestPath) || !Directory.Exists(physical.FinalChunksDirectory))
            throw new InvalidDataException("qa04.gate3.exact103.final-material-missing");

        var candidates = await store.ListSnapshotCandidatesNewestFirstAsync(cancellationToken).ConfigureAwait(false);
        var candidate = candidates.SingleOrDefault(value => value.SnapshotId == cut.SnapshotId)
            ?? throw new InvalidDataException("qa04.gate3.exact103.catalog-entry-missing");
        if (candidate.SnapshotStep != cut.SnapshotStep ||
            candidate.HistoryAnchorSequence != cut.HistoryAnchor.Sequence ||
            !CryptographicOperations.FixedTimeEquals(candidate.HistoryAnchorDigest, cut.HistoryAnchor.Digest) ||
            !CryptographicOperations.FixedTimeEquals(candidate.StateContinuityToken, cut.StateContinuityToken) ||
            !CryptographicOperations.FixedTimeEquals(candidate.SnapshotDigest, staged.SnapshotDigest) ||
            !CryptographicOperations.FixedTimeEquals(candidate.PhysicalManifestDigest, staged.PhysicalManifestDigest))
            throw new InvalidDataException("qa04.gate3.exact103.catalog-authority-mismatch");

        var finalManifestBytes = await File.ReadAllBytesAsync(
            physical.FinalManifestPath,
            cancellationToken).ConfigureAwait(false);
        var finalManifest = PhysicalSnapshotManifestWireCodecV1.Decode(finalManifestBytes);
        SnapshotPhysicalManifestStreamingAuthorityValidationV1.RequireExpectedAuthority(
            finalManifest,
            sections,
            cut.FrozenState,
            cut,
            staged.SnapshotDigest,
            staged.PhysicalManifestDigest);

        var finalFiles = Directory.GetFiles(physical.FinalChunksDirectory)
            .OrderBy(static path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        if (finalFiles.Length != staged.Chunks.Count)
            throw new InvalidDataException("qa04.gate3.exact103.final-chunk-count-mismatch");

        for (var i = 0; i < staged.Chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = staged.Chunks[i];
            if (descriptor.ChunkIndex != checked((uint)i))
                throw new InvalidDataException("qa04.gate3.exact103.chunk-index-gap");
            SnapshotChunkFile.ValidateRelativePath(descriptor.RelativePath, descriptor.ChunkIndex);
            var expectedPath = Path.Combine(
                physical.FinalDirectory,
                descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!string.Equals(
                    Path.GetFullPath(finalFiles[i]),
                    Path.GetFullPath(expectedPath),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("qa04.gate3.exact103.final-chunk-path-mismatch");

            var header = await SnapshotChunkFile.ValidateAsync(expectedPath, cancellationToken).ConfigureAwait(false);
            if (header.UncompressedLength != descriptor.UncompressedLength ||
                header.StoredLength != descriptor.StoredLength ||
                header.Compression != descriptor.Compression ||
                !CryptographicOperations.FixedTimeEquals(header.LogicalPayloadDigest, descriptor.LogicalPayloadDigest) ||
                !CryptographicOperations.FixedTimeEquals(header.StoredPayloadDigest, descriptor.StoredPayloadDigest))
                throw new InvalidDataException($"qa04.gate3.exact103.final-chunk-header-mismatch:{descriptor.ChunkIndex}");
        }

        await store.ValidateQuickCheckAsync(cancellationToken).ConfigureAwait(false);
    }
}
