using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04ProductionGate3Step6ReplayEquivalenceProofV1(
    ulong BasisStep,
    ulong ResultingStep,
    ulong HistorySequence,
    int TerminalOperationCount,
    int CrossDomainTransactionCount,
    byte[] ReplayedStateDigest,
    byte[] RecoveredStateDigest);

/// <summary>
/// Gate3 Step 6 production replay/equivalence proof. The replay never calls the durable finalizer or
/// Publish. It re-reads the committed exact-103 Snapshot, restores scheduler/Operation V2 authority,
/// reconstructs the pre-COMMIT ScheduledDurable Operation catalog from retained lifecycle provenance,
/// replays the same typed mutation/preparation path, and rebuilds the final core-substate candidate.
/// Equivalence is accepted only when the replay candidate diagnostic matches the durable receipt and
/// the non-publishable replay State(S+1) digest matches both durable semantic recovery and the actual
/// authoritative post-COMMIT state.
/// </summary>
public static class Qa04ProductionGate3Step6ReplayEquivalenceProofRunnerV1
{
    private sealed record RecoveredCoreStateV1(
        OperationSchedulerStateV1 Scheduler,
        RecoveredCoreOperationStateV2 Operation);

    public static async Task<Qa04ProductionGate3Step6ReplayEquivalenceProofV1> VerifyAsync(
        Qa04CanonicalOperationStepFinalizationResultV1 finalized,
        Qa04CanonicalOperationMutationStateV1 initialMutationState,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> orderedBindings,
        IDomainRecordSchemaResolverV1 references,
        Qa04ProductionExact103SnapshotPersistenceProofV1 step5Proof,
        SqlitePersistenceStore store,
        WorldPersistencePaths world,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(finalized);
        ArgumentNullException.ThrowIfNull(initialMutationState);
        ArgumentNullException.ThrowIfNull(orderedBindings);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(step5Proof);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(world);

        if (!finalized.AuthoritativeState.IsPublishable || !finalized.DurableReceipt.IsPublishable)
            throw new InvalidDataException("qa04.gate3.step6.authoritative-finalization-missing");
        if (step5Proof.RecoveredStateDigest is null || step5Proof.RecoveredStateDigest.Length != 32)
            throw new InvalidDataException("qa04.gate3.step6.step5-recovered-digest-missing");

        var basisState = finalized.Preparation.BasisState
            ?? throw new InvalidDataException("qa04.gate3.step6.basis-state-missing");
        var originalStep5Candidate = finalized.Preparation.Candidate;
        var originalFrozen = originalStep5Candidate.FrozenInput;
        var authoritativeState = finalized.AuthoritativeState.State;
        if (basisState.Header.Step != finalized.DurableReceipt.BasisStep ||
            authoritativeState.Header.Step != finalized.DurableReceipt.ResultingStep ||
            authoritativeState.Header.Step != checked(basisState.Header.Step + 1UL))
            throw new InvalidDataException("qa04.gate3.step6.finalization-step-drift");

        var decoders = CanonicalSnapshotProductionPhysicalDrainV1.ProductionDecoders();
        var durable = await CanonicalSnapshotDurableRecoveryV1.RecoverNewestAsync(
            store,
            world,
            decoders,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        RequireSnapshotMatchesStep5(durable, step5Proof, authoritativeState);
        await RequireContinuityAndHistoryAsync(durable, finalized, store, cancellationToken).ConfigureAwait(false);

        var recoveredCore = await RecoverCoreStateAsync(
            durable,
            world,
            decoders,
            cancellationToken).ConfigureAwait(false);
        RequireRecoveredCoreMatchesAuthoritative(recoveredCore, authoritativeState);

        var recoveredOperations = recoveredCore.Operation.Operations
            .OrderBy(static operation => operation.OperationId)
            .ToArray();
        var finalizedOperations = finalized.TerminalOperations
            .OrderBy(static operation => operation.OperationId)
            .ToArray();
        if (recoveredOperations.Length != finalizedOperations.Length ||
            recoveredOperations.Length != originalFrozen.ScheduledOperations.Count)
            throw new InvalidDataException("qa04.gate3.step6.terminal-operation-count-drift");
        for (var i = 0; i < recoveredOperations.Length; i++)
        {
            if (!SameOperation(recoveredOperations[i], finalizedOperations[i]))
                throw new InvalidDataException("qa04.gate3.step6.recovered-terminal-operation-drift");
        }

        var durableBefore = new DurableOperationStateV1[recoveredOperations.Length];
        var terminalCommits = new TerminalOperationCommit[recoveredOperations.Length];
        for (var i = 0; i < recoveredOperations.Length; i++)
        {
            var terminal = recoveredOperations[i];
            if (terminal.Lifecycle != DurableOperationLifecycleV1.TerminalDurable ||
                terminal.AcceptedSequence is null || terminal.ScheduledSequence is null ||
                terminal.EffectiveStep != basisState.Header.Step ||
                terminal.TerminalSequence != durable.Catalog.HistoryAnchorSequence ||
                terminal.TerminalStatus is null || terminal.ResultCode is null)
                throw new InvalidDataException("qa04.gate3.step6.terminal-operation-provenance-invalid");

            durableBefore[i] = terminal with
            {
                OperationPayloadDigest = terminal.OperationPayloadDigest.ToArray(),
                Lifecycle = DurableOperationLifecycleV1.ScheduledDurable,
                TerminalSequence = null,
                TerminalStatus = null,
                ResultCode = null,
                RichResultPayload = null,
            };
            terminalCommits[i] = new TerminalOperationCommit(
                terminal.OperationId,
                terminal.TerminalStatus.Value,
                terminal.ResultCode,
                terminal.RichResultPayload?.ToArray());
        }

        var reconstructedBasisOperation = CoreOperationStateSubstateV2.Canonicalize(
            durableBefore,
            recoveredCore.Operation.Transactions,
            basisState.Header.Step);
        RequireSubstateEqual(
            reconstructedBasisOperation,
            basisState.OperationState,
            "qa04.gate3.step6.reconstructed-basis-operation-drift");

        var replayScheduler = new OperationSchedulerStateV1(
            basisState.Header.Step,
            freezeStep: null,
            originalFrozen.ScheduledOperations);
        var reconstructedBasisScheduler = OperationSchedulerSubstateV1.Canonicalize(
            replayScheduler,
            basisState.Header.Step);
        RequireSubstateEqual(
            reconstructedBasisScheduler,
            basisState.SchedulerState,
            "qa04.gate3.step6.reconstructed-basis-scheduler-drift");
        var replayFrozen = StepInputFreezerV1.Freeze(basisState, replayScheduler);
        RequireFrozenEquivalent(originalFrozen, replayFrozen);

        var replayMutation = Qa04CanonicalOperationMutationBatchV1.Apply(
            basisState.Header.WorldId,
            basisState.Header.Step,
            orderedBindings,
            initialMutationState,
            references);
        var runtimeOnlyOutputs = finalized.Preparation.DomainOutputs.Outputs
            .Select(static output => new DomainCandidateOutputV1(
                output.DomainToken,
                output.BasisStep,
                intents: output.Intents,
                localPartitionCandidates: Array.Empty<PartitionCandidateV1>()))
            .ToArray();
        var replayPreparation = Qa04ProductionAuthoritativeStepPreparationV1.Prepare(
            originalStep5Candidate.CandidateId,
            basisState,
            replayFrozen,
            orderedBindings,
            replayMutation,
            references,
            runtimeOnlyOutputs);
        if (replayPreparation.Candidate.IsPublishable || replayPreparation.PreparedState.IsPublishable ||
            !CryptographicOperations.FixedTimeEquals(
                replayPreparation.Candidate.DiagnosticDigest,
                originalStep5Candidate.DiagnosticDigest))
            throw new InvalidDataException("qa04.gate3.step6.step5-replay-drift");

        var schedulerCore = OperationSchedulerSubstateV1.CreatePostFinalizationCandidate(
            basisState,
            replayScheduler,
            replayFrozen);
        var operationCore = CoreOperationStateSubstateV2.CreatePostTransitionCandidate(
            basisState,
            durableBefore,
            recoveredCore.Operation.Transactions,
            terminalCommits,
            basisState.Header.Step,
            durable.Catalog.HistoryAnchorSequence);
        RequireSubstateEqual(
            schedulerCore.ResultingState,
            authoritativeState.SchedulerState,
            "qa04.gate3.step6.replay-scheduler-result-drift");
        RequireSubstateEqual(
            operationCore.ResultingState,
            authoritativeState.OperationState,
            "qa04.gate3.step6.replay-operation-result-drift");

        var coreCandidates = new[] { schedulerCore, operationCore }
            .OrderBy(static candidate => candidate.Kind)
            .ToArray();
        var replayCandidate = StepCandidateV1.Build(
            originalStep5Candidate.CandidateId,
            basisState,
            replayFrozen,
            replayPreparation.DomainOutputs.Outputs,
            replayPreparation.Candidate.ConflictResolutions,
            invariantResults: replayPreparation.Candidate.InvariantResults,
            coreSubstateCandidates: coreCandidates);
        if (!replayCandidate.CommitDecision.CanCommit || replayCandidate.IsPublishable ||
            !CryptographicOperations.FixedTimeEquals(
                replayCandidate.DiagnosticDigest,
                finalized.DurableReceipt.CandidateDiagnosticDigest))
            throw new InvalidDataException("qa04.gate3.step6.final-candidate-equivalence-failed");

        var partitionMaterials = replayCandidate.PartitionCandidates
            .Select(partition => new StepPartitionStateMaterialV1(
                replayPreparation.PreparedState.ResultingState.Partitions.Get(partition.PartitionId.Value).Header))
            .ToArray();
        var coreMaterials = coreCandidates
            .Select(static core => new StepCoreSubstateStateMaterialV1(core.Kind, core.ResultingState))
            .ToArray();
        var replayPrepared = StepStateApplicationV1.Prepare(
            basisState,
            replayCandidate,
            partitionMaterials,
            coreMaterials);
        if (replayPrepared.IsPublishable ||
            replayPrepared.CandidateId != finalized.DurableReceipt.CandidateId ||
            replayPrepared.TargetStep != authoritativeState.Header.Step ||
            !CryptographicOperations.FixedTimeEquals(
                replayPrepared.ResultingState.Diagnostic.StateDigest,
                authoritativeState.Diagnostic.StateDigest) ||
            !CryptographicOperations.FixedTimeEquals(
                replayPrepared.ResultingState.Diagnostic.StateDigest,
                step5Proof.RecoveredStateDigest))
            throw new InvalidDataException("qa04.gate3.step6.replayed-state-equivalence-failed");

        return new Qa04ProductionGate3Step6ReplayEquivalenceProofV1(
            basisState.Header.Step,
            replayPrepared.TargetStep,
            durable.Catalog.HistoryAnchorSequence,
            recoveredOperations.Length,
            recoveredCore.Operation.Transactions.Count,
            replayPrepared.ResultingState.Diagnostic.StateDigest.ToArray(),
            step5Proof.RecoveredStateDigest.ToArray());
    }

    private static void RequireSnapshotMatchesStep5(
        CanonicalSnapshotDurableRecoveryResultV1 durable,
        Qa04ProductionExact103SnapshotPersistenceProofV1 step5Proof,
        WorldStateV1 authoritativeState)
    {
        if (durable.Catalog.SnapshotStep != step5Proof.SnapshotStep ||
            durable.Catalog.SnapshotStep != authoritativeState.Header.Step ||
            durable.Sections.Count != step5Proof.SectionCount ||
            !CryptographicOperations.FixedTimeEquals(durable.Catalog.SnapshotDigest, step5Proof.SnapshotDigest) ||
            !CryptographicOperations.FixedTimeEquals(durable.Catalog.PhysicalManifestDigest, step5Proof.PhysicalManifestDigest))
            throw new InvalidDataException("qa04.gate3.step6.snapshot-step5-identity-drift");
    }

    private static async Task RequireContinuityAndHistoryAsync(
        CanonicalSnapshotDurableRecoveryResultV1 durable,
        Qa04CanonicalOperationStepFinalizationResultV1 finalized,
        SqlitePersistenceStore store,
        CancellationToken cancellationToken)
    {
        if (durable.Catalog.HistoryAnchorSequence != finalized.DurableReceipt.HistorySequence ||
            durable.Catalog.HistoryAnchorSequence != finalized.FinalizeMaterial.TransitionHistory.Sequence ||
            !CryptographicOperations.FixedTimeEquals(
                durable.Catalog.HistoryAnchorDigest,
                finalized.FinalizeMaterial.TransitionHistory.RecordDigest) ||
            !CryptographicOperations.FixedTimeEquals(
                durable.Catalog.StateContinuityToken,
                finalized.ResultingContinuityToken) ||
            !CryptographicOperations.FixedTimeEquals(
                durable.Catalog.StateContinuityToken,
                finalized.FinalizeMaterial.ResultingStateContinuityToken))
            throw new InvalidDataException("qa04.gate3.step6.continuity-history-drift");

        if (!await store.HistoryAnchorExistsAsync(
                durable.Catalog.HistoryAnchorSequence,
                durable.Catalog.HistoryAnchorDigest,
                cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("qa04.gate3.step6.history-anchor-missing");
    }

    private static async Task<RecoveredCoreStateV1> RecoverCoreStateAsync(
        CanonicalSnapshotDurableRecoveryResultV1 durable,
        WorldPersistencePaths world,
        IEnumerable<ISnapshotChunkCompressionDecoderV1> decoders,
        CancellationToken cancellationToken)
    {
        var operationMetadata = durable.Sections.Single(section => string.Equals(
            section.SectionId,
            CoreSnapshotOwnerSectionRegistryV1.OperationState,
            StringComparison.Ordinal));
        var schedulerMetadata = durable.Sections.Single(section => string.Equals(
            section.SectionId,
            CoreSnapshotOwnerSectionRegistryV1.SchedulerState,
            StringComparison.Ordinal));
        if (operationMetadata.SectionSchema != CoreOperationStateSnapshotAuthorityV2.Schema)
            throw new InvalidDataException("qa04.gate3.step6.operation-v2-schema-missing");

        var operationFragments = new List<SnapshotSectionFragmentMaterialV1>();
        var schedulerFragments = new List<SnapshotSectionFragmentMaterialV1>();
        var finalDirectory = ResolveFinalDirectory(world, durable.Catalog.RelativeDirectory);
        foreach (var descriptor in durable.Manifest.Chunks.OrderBy(static chunk => chunk.ChunkIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SnapshotChunkFile.ValidateRelativePath(descriptor.RelativePath, descriptor.ChunkIndex);
            var chunkPath = Path.Combine(
                finalDirectory,
                descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var decoded = await CanonicalSnapshotChunkFileV1.ReadValidatedAsync(
                chunkPath,
                decoders,
                cancellationToken).ConfigureAwait(false);
            foreach (var fragment in decoded.Payload.Fragments)
            {
                if (string.Equals(fragment.SectionId, operationMetadata.SectionId, StringComparison.Ordinal))
                    operationFragments.Add(fragment);
                else if (string.Equals(fragment.SectionId, schedulerMetadata.SectionId, StringComparison.Ordinal))
                    schedulerFragments.Add(fragment);
            }
        }

        var orderedOperation = operationFragments.OrderBy(static fragment => fragment.FragmentIndex).ToArray();
        var orderedScheduler = schedulerFragments.OrderBy(static fragment => fragment.FragmentIndex).ToArray();
        if (orderedOperation.Length != operationMetadata.FragmentCount ||
            orderedScheduler.Length != schedulerMetadata.FragmentCount)
            throw new InvalidDataException("qa04.gate3.step6.core-fragment-count-drift");

        var operationSection = new CanonicalSnapshotSectionMaterialV1(
            operationMetadata.SectionId,
            operationMetadata.SectionSchema,
            operationMetadata.LogicalItemCount,
            operationMetadata.LogicalContentDigest.ToArray(),
            Array.AsReadOnly(orderedOperation));
        var operation = CoreOperationStateSnapshotSectionProviderV2.Recover(
            operationSection,
            durable.Catalog.SnapshotStep);

        var schedulerVerifier = CoreSnapshotPrimarySemanticVerifierV1.Scheduler(durable.Catalog.SnapshotStep);
        if (schedulerVerifier.SectionSchema != schedulerMetadata.SectionSchema)
            throw new InvalidDataException("qa04.gate3.step6.scheduler-schema-drift");
        var schedulerSemantic = schedulerVerifier.Verify(orderedScheduler)
            ?? throw new InvalidDataException("qa04.gate3.step6.scheduler-verifier-null");
        if (schedulerSemantic.LogicalItemCount != schedulerMetadata.LogicalItemCount ||
            !CryptographicOperations.FixedTimeEquals(
                schedulerSemantic.LogicalContentDigest,
                schedulerMetadata.LogicalContentDigest))
            throw new InvalidDataException("qa04.gate3.step6.scheduler-semantic-drift");

        var scheduler = DecodeScheduler(orderedScheduler, durable.Catalog.SnapshotStep);
        return new RecoveredCoreStateV1(scheduler, operation);
    }

    private static OperationSchedulerStateV1 DecodeScheduler(
        IReadOnlyList<SnapshotSectionFragmentMaterialV1> fragments,
        ulong expectedWorldStep)
    {
        if (fragments.Count == 0)
            throw new InvalidDataException("qa04.gate3.step6.scheduler-fragment-missing");
        ulong? next = null;
        ulong? freeze = null;
        var scheduled = new List<ScheduledOperationRefV1>();
        ulong itemCount = 0;
        foreach (var fragment in fragments)
        {
            var decoded = CoreSchedulerStateSnapshotWireCodecV1.Decode(fragment.FragmentPayload);
            if (decoded.WorldStep != expectedWorldStep)
                throw new InvalidDataException("qa04.gate3.step6.scheduler-world-step-drift");
            if (next is null)
            {
                next = decoded.NextSchedulableStep;
                freeze = decoded.FreezeStep;
            }
            else if (next.Value != decoded.NextSchedulableStep || freeze != decoded.FreezeStep)
                throw new InvalidDataException("qa04.gate3.step6.scheduler-fragment-metadata-drift");
            if (fragment.ItemCount != checked((ulong)decoded.ScheduledOperations.Count))
                throw new InvalidDataException("qa04.gate3.step6.scheduler-fragment-item-count-drift");
            itemCount = checked(itemCount + fragment.ItemCount);
            scheduled.AddRange(decoded.ScheduledOperations);
        }
        CoreSchedulerStateSnapshotWireCodecV1.ValidateCanonical(scheduled);
        if (itemCount != checked((ulong)scheduled.Count))
            throw new InvalidDataException("qa04.gate3.step6.scheduler-item-count-drift");
        return new OperationSchedulerStateV1(
            next ?? throw new InvalidDataException("qa04.gate3.step6.scheduler-next-step-missing"),
            freeze,
            scheduled);
    }

    private static void RequireRecoveredCoreMatchesAuthoritative(
        RecoveredCoreStateV1 recovered,
        WorldStateV1 authoritativeState)
    {
        if (recovered.Scheduler.NextSchedulableStep != authoritativeState.Header.Step ||
            recovered.Scheduler.FreezeStep is not null ||
            recovered.Scheduler.CanonicalBuckets.Any())
            throw new InvalidDataException("qa04.gate3.step6.recovered-scheduler-not-finalized");
        var schedulerAuthority = OperationSchedulerSubstateV1.Canonicalize(
            recovered.Scheduler,
            authoritativeState.Header.Step);
        RequireSubstateEqual(
            schedulerAuthority,
            authoritativeState.SchedulerState,
            "qa04.gate3.step6.recovered-scheduler-authority-drift");

        var operationAuthority = CoreOperationStateSubstateV2.Canonicalize(
            recovered.Operation.Operations,
            recovered.Operation.Transactions,
            authoritativeState.Header.Step);
        RequireSubstateEqual(
            operationAuthority,
            authoritativeState.OperationState,
            "qa04.gate3.step6.recovered-operation-authority-drift");
    }

    private static void RequireFrozenEquivalent(FrozenStepInputV1 expected, FrozenStepInputV1 actual)
    {
        if (expected.WorldId != actual.WorldId || expected.BasisStep != actual.BasisStep ||
            expected.ConfigGeneration != actual.ConfigGeneration ||
            !CryptographicOperations.FixedTimeEquals(expected.ConfigDigest, actual.ConfigDigest) ||
            expected.ScheduledOperations.Count != actual.ScheduledOperations.Count)
            throw new InvalidDataException("qa04.gate3.step6.frozen-input-drift");
        for (var i = 0; i < expected.ScheduledOperations.Count; i++)
        {
            var left = expected.ScheduledOperations[i];
            var right = actual.ScheduledOperations[i];
            if (left.OperationId != right.OperationId || left.EffectiveStep != right.EffectiveStep ||
                !left.OrderKey.ToDatabaseBytes().AsSpan().SequenceEqual(right.OrderKey.ToDatabaseBytes()))
                throw new InvalidDataException("qa04.gate3.step6.frozen-operation-drift");
        }
    }

    private static void RequireSubstateEqual(
        WorldSubstateRefV1 actual,
        WorldSubstateRefV1 expected,
        string error)
    {
        if (actual.Schema != expected.Schema ||
            !CryptographicOperations.FixedTimeEquals(actual.CanonicalDigest, expected.CanonicalDigest))
            throw new InvalidDataException(error);
    }

    private static bool SameOperation(DurableOperationStateV1 left, DurableOperationStateV1 right)
        => left.OperationId == right.OperationId &&
           left.Lifecycle == right.Lifecycle &&
           left.AcceptedSequence == right.AcceptedSequence &&
           left.ScheduledSequence == right.ScheduledSequence &&
           left.EffectiveStep == right.EffectiveStep &&
           left.TerminalSequence == right.TerminalSequence &&
           left.TerminalStatus == right.TerminalStatus &&
           string.Equals(left.ResultCode, right.ResultCode, StringComparison.Ordinal) &&
           left.OperationPayloadDigest.AsSpan().SequenceEqual(right.OperationPayloadDigest) &&
           PayloadEquals(left.RichResultPayload, right.RichResultPayload);

    private static bool PayloadEquals(byte[]? left, byte[]? right)
        => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    private static string ResolveFinalDirectory(WorldPersistencePaths world, string relativeDirectory)
    {
        var generationRoot = Path.GetFullPath(world.GenerationDirectory);
        var finalDirectory = Path.GetFullPath(Path.Combine(
            generationRoot,
            relativeDirectory.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = generationRoot.EndsWith(Path.DirectorySeparatorChar)
            ? generationRoot
            : generationRoot + Path.DirectorySeparatorChar;
        if (!finalDirectory.StartsWith(prefix, comparison))
            throw new InvalidDataException("qa04.gate3.step6.snapshot-directory-escape");
        return finalDirectory;
    }
}
