using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04AuthoritativeStepLoopProbeV1(
    ulong BasisStep,
    ulong FinalResultingStep,
    ulong StepCount,
    int WorkerCount,
    ulong ResidentRecordCount,
    ulong TotalDomainOutputCount,
    ulong TotalPartitionCandidateCount,
    bool CandidatePublishableBeforeCommitObserved,
    bool PreparedStatePublishableBeforeCommitObserved,
    bool AllDurableReceiptsPublishable,
    bool AllResultingWorldStatesPublishable,
    bool AllStateChainsValid,
    bool SchedulerReopenedEachStep,
    bool RealSqliteCommitObservedThroughLoop,
    ulong FinalResidentPartitionRevision,
    ulong FinalResidentPartitionBasisStep,
    string BasisStateDigest,
    string FinalStateDigest,
    string FinalContinuityToken,
    string[] BlockingFailureCodes)
{
    public bool ReducedAuthoritativeStepLoopAvailable => true;
    public bool ReferenceWorldMaterialized => false;
    public bool AuthoritativeStepLoopAvailable => false;
    public bool ReleaseEvidenceCapable => false;
}

/// <summary>
/// Reduced consecutive-Step authority proof. This is deliberately not the canonical 27,000-Step
/// QA-04 performance loop and does not enable release evidence. It proves that one truthful
/// Resident material slice can repeatedly traverse the ordinary eight-domain execution,
/// candidate/prepare, SQLite COMMIT, and post-COMMIT publish boundary without resetting world state.
/// </summary>
public static class Qa04AuthoritativeStepLoopBridgeV1
{
    public const ulong StandardReducedStepCount = 30;

    private static readonly StableToken StructuralInvariant = new("qa04.reduced-authoritative-step-loop");

    public static async Task<Qa04AuthoritativeStepLoopProbeV1> RunReducedAsync(
        int workerCount,
        ulong residentRecordCount,
        string persistenceRoot,
        CancellationToken cancellationToken = default)
    {
        if (!Qa04DomainExecutionTargetV1.CanonicalWorkerCounts.Contains(workerCount))
            throw new InvalidDataException("qa04.loop.worker-count-not-canonical");
        if (residentRecordCount is 0 or > Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount)
            throw new ArgumentOutOfRangeException(nameof(residentRecordCount));
        if (string.IsNullOrWhiteSpace(persistenceRoot))
            throw new ArgumentException("Persistence root is required.", nameof(persistenceRoot));

        var materialized = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(residentRecordCount);
        var state = materialized.WorldState;
        if (state.Header.Step != 0)
            throw new InvalidDataException("qa04.loop.genesis-step-mismatch");

        var scheduler = new OperationSchedulerStateV1(
            nextSchedulableStep: state.Header.Step,
            freezeStep: null,
            scheduled: Array.Empty<ScheduledOperationRefV1>());
        var paths = PersistenceLayout.Resolve(persistenceRoot, state.Header.WorldId, 1);
        PersistenceLayout.EnsureGenerationDirectories(paths);
        await PersistenceLayout.WriteCurrentAsync(paths, 1, cancellationToken).ConfigureAwait(false);

        var genesis = CreateGenesisHistory(state);
        var continuity = HistoryIntegrity.ComputeGenesisContinuityToken(state.Header.WorldId, genesis.RecordDigest);
        await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths, cancellationToken).ConfigureAwait(false);
        await store.InitializeWorldMetadataAsync(
            new WorldPersistenceMetadataSeed(
                state.Header.WorldId,
                PersistenceGeneration: 1,
                Qa04ReferenceLoadV1.WorldSeed,
                continuity,
                ConfigGeneration: state.Header.ConfigGeneration,
                state.Diagnostic.ConfigDigest,
                MasterGeneration: state.Header.MasterGeneration),
            genesis,
            cancellationToken).ConfigureAwait(false);

        var basisDigest = state.Diagnostic.StateDigest.ToArray();
        ulong domainOutputCount = 0;
        ulong partitionCandidateCount = 0;
        var allReceiptsPublishable = true;
        var allStatesPublishable = true;
        var allChainsValid = true;
        var schedulerReopenedEachStep = true;
        var candidatePublishableBeforeCommitObserved = false;
        var preparedPublishableBeforeCommitObserved = false;

        for (ulong ordinal = 0; ordinal < StandardReducedStepCount; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (state.Header.Step != ordinal)
                throw new InvalidDataException("qa04.loop.state-step-drift");

            var frozen = StepInputFreezerV1.Freeze(state, scheduler);
            var outputs = await DomainRuntimeExecutorV1.ExecuteAsync(
                StandardDomainExecutionPlanV1.Create(),
                state,
                frozen,
                CreateStructuralRuntimes(materialized.Partition),
                workerCount,
                cancellationToken).ConfigureAwait(false);
            domainOutputCount = checked(domainOutputCount + (ulong)outputs.Count);

            var candidate = StepCandidateV1.Build(
                DeriveCandidateId(state.Header.Step),
                state,
                frozen,
                outputs,
                Array.Empty<ConflictGroupResolutionV1>(),
                invariantResults:
                [
                    new InvariantResultV1(
                        StructuralInvariant,
                        InvariantSeverityV1.CommitBlocking,
                        InvariantOutcomeV1.Pass),
                ]);
            partitionCandidateCount = checked(partitionCandidateCount + (ulong)candidate.PartitionCandidates.Count);
            if (!candidate.CommitDecision.CanCommit)
                throw new InvalidDataException("qa04.loop.candidate-not-committable");
            candidatePublishableBeforeCommitObserved |= candidate.IsPublishable;
            if (candidate.PartitionCandidates.Count != 1 ||
                candidate.PartitionCandidates[0].PartitionId.Value != ResidentIdentityLifecyclePayloadV1.PartitionId)
                throw new InvalidDataException("qa04.loop.resident-partition-candidate-missing");

            var basisResident = state.Partitions.Get(ResidentIdentityLifecyclePayloadV1.PartitionId).Header;
            var resultingResident = CreateResultingResidentHeader(
                materialized.Partition,
                basisResident,
                candidate.TargetStep);
            var prepared = StepStateApplicationV1.Prepare(
                state,
                candidate,
                [new StepPartitionStateMaterialV1(resultingResident)]);
            preparedPublishableBeforeCommitObserved |= prepared.IsPublishable;
            if (candidate.IsPublishable || prepared.IsPublishable)
                throw new InvalidDataException("qa04.loop.premature-authority");

            var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
            var transition = CreateTransitionHistory(
                candidate,
                prepared,
                checked(anchor.Sequence + 1),
                anchor.Digest);
            var nextContinuity = HistoryIntegrity.ComputeTransitionContinuityToken(
                candidate.WorldId,
                candidate.TargetStep,
                continuity,
                transition.RecordDigest);
            var finalizeMaterial = new StepFinalizeMaterialV1(
                candidate.ConfigGeneration,
                candidate.ConfigDigest,
                nextContinuity,
                transition,
                Array.Empty<TerminalOperationCommit>());

            var receipt = await new StepFinalizationCoordinatorV1(new SqliteStepTransitionDurabilityV1(store))
                .FinalizeAsync(candidate, scheduler, finalizeMaterial, cancellationToken)
                .ConfigureAwait(false);
            var authoritative = StepStateApplicationV1.Publish(prepared, receipt);
            allReceiptsPublishable &= receipt.IsPublishable;
            allStatesPublishable &= authoritative.IsPublishable;
            schedulerReopenedEachStep &= scheduler.FreezeStep is null &&
                                         scheduler.NextSchedulableStep == authoritative.State.Header.Step;

            var previousDigest = authoritative.State.Header.PreviousStateDigest;
            var chainValid = previousDigest is not null &&
                             CryptographicOperations.FixedTimeEquals(previousDigest, state.Diagnostic.StateDigest);
            allChainsValid &= chainValid;
            if (!receipt.IsPublishable || !authoritative.IsPublishable || !chainValid)
                throw new InvalidDataException("qa04.loop.post-commit-authority-invalid");

            state = authoritative.State;
            continuity = nextContinuity;
        }

        var recovery = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        var durableThroughLoop = recovery.FinalizedStep == StandardReducedStepCount &&
                                 CryptographicOperations.FixedTimeEquals(recovery.ContinuityToken, continuity);
        if (!durableThroughLoop)
            throw new InvalidDataException("qa04.loop.sqlite-recovery-head-mismatch");

        var finalResident = state.Partitions.Get(ResidentIdentityLifecyclePayloadV1.PartitionId).Header;
        if (finalResident.Revision != checked(1UL + StandardReducedStepCount) ||
            finalResident.BasisStep != StandardReducedStepCount)
            throw new InvalidDataException("qa04.loop.resident-header-progress-mismatch");
        if (candidatePublishableBeforeCommitObserved || preparedPublishableBeforeCommitObserved)
            throw new InvalidDataException("qa04.loop.precommit-publication-observed");

        return new Qa04AuthoritativeStepLoopProbeV1(
            BasisStep: 0,
            FinalResultingStep: state.Header.Step,
            StepCount: StandardReducedStepCount,
            WorkerCount: workerCount,
            ResidentRecordCount: residentRecordCount,
            TotalDomainOutputCount: domainOutputCount,
            TotalPartitionCandidateCount: partitionCandidateCount,
            CandidatePublishableBeforeCommitObserved: candidatePublishableBeforeCommitObserved,
            PreparedStatePublishableBeforeCommitObserved: preparedPublishableBeforeCommitObserved,
            AllDurableReceiptsPublishable: allReceiptsPublishable,
            AllResultingWorldStatesPublishable: allStatesPublishable,
            AllStateChainsValid: allChainsValid,
            SchedulerReopenedEachStep: schedulerReopenedEachStep,
            RealSqliteCommitObservedThroughLoop: durableThroughLoop,
            FinalResidentPartitionRevision: finalResident.Revision,
            FinalResidentPartitionBasisStep: finalResident.BasisStep,
            BasisStateDigest: Hex(basisDigest),
            FinalStateDigest: Hex(state.Diagnostic.StateDigest),
            FinalContinuityToken: Hex(continuity),
            BlockingFailureCodes:
            [
                "qa04.target.reference-world-other-partitions-not-materialized",
                "qa04.target.canonical-operation-load-not-injected",
                "qa04.target.detail-substate-mutation-application-not-assembled",
                "qa04.target.authoritative-full-step-loop-not-assembled",
            ]);
    }

    private static IReadOnlyCollection<IDomainRuntimeV1> CreateStructuralRuntimes(
        DomainPartitionStateV1<ResidentIdentityLifecyclePayloadV1> residentPartition)
    {
        static ValueTask<IReadOnlyList<MutationIntentCandidateV1>> NoIntents(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<MutationIntentCandidateV1>>(
                Array.Empty<MutationIntentCandidateV1>());
        }

        ValueTask<IReadOnlyList<PartitionCandidateV1>> ResidentPartition(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = context.State.Partitions.Get(ResidentIdentityLifecyclePayloadV1.PartitionId).Header;
            var resulting = CreateResultingResidentHeader(
                residentPartition,
                current,
                checked(context.FrozenInput.BasisStep + 1));
            var changeSetDigest = StepPartitionStateMaterialV1.ComputeChangeSetDigest(
                current,
                resulting,
                context.FrozenInput.BasisStep,
                checked(context.FrozenInput.BasisStep + 1));
            return ValueTask.FromResult<IReadOnlyList<PartitionCandidateV1>>(
            [
                ResidentParticipationPartitionCandidateFactoryV1.CreateResident(
                    context.State,
                    ResidentIdentityLifecyclePayloadV1.PartitionId,
                    changeSetDigest),
            ]);
        }

        return
        [
            new SpatialDomainRuntimeV1(NoIntents),
            new EnvironmentDomainRuntimeV1(NoIntents),
            new PhysicalBuiltDomainRuntimeV1(NoIntents),
            new ParticipationDomainRuntimeV1(NoIntents),
            new ResidentDomainRuntimeV1(NoIntents, ResidentPartition),
            new SocietyEconomyDomainRuntimeV1(NoIntents),
            new GovernanceSecurityDomainRuntimeV1(NoIntents),
            new InfrastructureInformationDomainRuntimeV1(NoIntents),
        ];
    }

    private static PartitionStateHeaderV1 CreateResultingResidentHeader(
        DomainPartitionStateV1<ResidentIdentityLifecyclePayloadV1> partition,
        PartitionStateHeaderV1 basisHeader,
        ulong targetStep)
        => PartitionStateHeaderV1.CreateCanonical(
            partition,
            revision: checked(basisHeader.Revision + 1),
            basisStep: targetStep,
            detailLevel: basisHeader.DetailLevel,
            static payload => payload.CanonicalDigest());

    private static HistoryRecordMaterial CreateGenesisHistory(WorldStateV1 state)
        => HistoryRecordMaterial.Create(
            state.Header.WorldId,
            sequence: 1,
            previousRecordDigest: new byte[32],
            recordType: "world.genesis.v1",
            payloadSchemaId: "core.world-genesis.v1",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: state.Header.WorldId.ToBytes().Concat(Qa04ReferenceLoadV1.WorldSeed.ToBytes()).ToArray(),
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(5);
                writer.WriteUnsigned(0); writer.WriteBytes(state.Header.WorldId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(Qa04ReferenceLoadV1.WorldSeed.ToBytes());
                writer.WriteUnsigned(2); writer.WriteUnsigned(state.Header.Step);
                writer.WriteUnsigned(3); writer.WriteBytes(state.Diagnostic.StateDigest);
                writer.WriteUnsigned(4); writer.WriteBytes(state.Diagnostic.ConfigDigest);
            });

    private static HistoryRecordMaterial CreateTransitionHistory(
        StepCandidateV1 candidate,
        PreparedStepWorldStateV1 prepared,
        ulong sequence,
        ReadOnlySpan<byte> previousRecordDigest)
        => HistoryRecordMaterial.Create(
            candidate.WorldId,
            sequence,
            previousRecordDigest,
            recordType: "transition.committed.v1",
            payloadSchemaId: "persistence.transition-committed",
            payloadSchemaMajor: 1,
            payloadSchemaMinor: 0,
            payloadBytes: candidate.DiagnosticDigest.Concat(prepared.ResultingState.Diagnostic.StateDigest).ToArray(),
            writeNormalizedPayload: writer =>
            {
                writer.WriteMapStart(7);
                writer.WriteUnsigned(0); writer.WriteUnsigned(candidate.BasisStep);
                writer.WriteUnsigned(1); writer.WriteUnsigned(candidate.TargetStep);
                writer.WriteUnsigned(2); writer.WriteBytes(candidate.CandidateId.ToBytes());
                writer.WriteUnsigned(3); writer.WriteBytes(candidate.DiagnosticDigest);
                writer.WriteUnsigned(4); writer.WriteBytes(prepared.BasisStateDigest);
                writer.WriteUnsigned(5); writer.WriteBytes(prepared.ResultingState.Diagnostic.StateDigest);
                writer.WriteUnsigned(6);
                writer.WriteArrayStart((ulong)candidate.PartitionCandidates.Count);
                foreach (var partition in candidate.PartitionCandidates)
                {
                    writer.WriteArrayStart(2);
                    writer.WriteAsciiText(partition.PartitionId.Value);
                    writer.WriteBytes(partition.CandidateDigest);
                }
            });

    private static OpaqueId128 DeriveCandidateId(ulong basisStep)
    {
        var digest = HashSuite.DomainHash("mv.qa04-reduced-authoritative-step-loop.v1", writer =>
        {
            writer.WriteMapStart(3);
            writer.WriteUnsigned(0); writer.WriteBytes(Qa04ReferenceLoadV1.WorldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteUnsigned(basisStep);
            writer.WriteUnsigned(2); writer.WriteAsciiText(Qa04ReferenceLoadV1.BenchmarkProfileId);
        });
        var candidateId = HashSuite.Trunc128(digest);
        if (candidateId.IsZero)
            throw new InvalidDataException("qa04.loop.candidate-id-zero");
        return candidateId;
    }

    private static string Hex(ReadOnlySpan<byte> value)
        => Convert.ToHexString(value).ToLowerInvariant();
}
