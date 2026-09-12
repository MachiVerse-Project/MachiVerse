using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04CoreSubstateAuthorityProbeV1(
    ulong BasisStep,
    ulong FirstResultingStep,
    ulong FinalResultingStep,
    int WorkerCount,
    ulong ResidentRecordCount,
    int FirstStepCoreSubstateCandidateCount,
    int SecondStepCoreSubstateCandidateCount,
    int FirstStepFrozenOperationCount,
    int SecondStepFrozenOperationCount,
    bool FutureOperationCarriedToSecondStep,
    bool FirstStepSchedulerDigestMatchedDurableRuntime,
    bool FirstStepOperationDigestMatchedSqlite,
    bool SecondStepSchedulerDigestMatchedDurableRuntime,
    bool SecondStepOperationDigestMatchedSqlite,
    bool FirstOperationTerminalDurable,
    bool SecondOperationTerminalDurable,
    bool FirstStateChainValid,
    bool SecondStateChainValid,
    bool RealSqliteCommitObservedThroughStepTwo,
    string BasisStateDigest,
    string FirstStateDigest,
    string FinalStateDigest,
    string FinalContinuityToken,
    string[] BlockingFailureCodes)
{
    public bool ReducedTwoStepAuthorityAvailable => true;
    public bool ReferenceWorldMaterialized => false;
    public bool AuthoritativeStepLoopAvailable => false;
    public bool ReleaseEvidenceCapable => false;
}

/// <summary>
/// Reduced two-Step proof for candidate-bound scheduler and durable Operation core substates.
/// Two real SQLite-scheduled Operations are admitted: one executes at Step 0, the second remains
/// queued across State 0 -> State 1 and executes at Step 1. Each resulting WorldState is prepared
/// before COMMIT and published only after the matching durable transition receipt exists.
/// </summary>
public static class Qa04CoreSubstateAuthorityBridgeV1
{
    private static readonly StableToken StructuralInvariant = new("qa04.core-substate-authority-path");

    public static async Task<Qa04CoreSubstateAuthorityProbeV1> RunTwoStepAsync(
        int workerCount,
        ulong residentRecordCount,
        string persistenceRoot,
        CancellationToken cancellationToken = default)
    {
        if (!Qa04DomainExecutionTargetV1.CanonicalWorkerCounts.Contains(workerCount))
            throw new InvalidDataException("qa04.core-substate.worker-count-not-canonical");
        if (residentRecordCount is 0 or > Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount)
            throw new ArgumentOutOfRangeException(nameof(residentRecordCount));
        if (string.IsNullOrWhiteSpace(persistenceRoot))
            throw new ArgumentException("Persistence root is required.", nameof(persistenceRoot));

        var materialized = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(residentRecordCount);
        var genesisState = materialized.WorldState;
        if (genesisState.Header.Step != 0)
            throw new InvalidDataException("qa04.core-substate.genesis-step-mismatch");

        var paths = PersistenceLayout.Resolve(persistenceRoot, genesisState.Header.WorldId, 1);
        PersistenceLayout.EnsureGenerationDirectories(paths);
        await PersistenceLayout.WriteCurrentAsync(paths, 1, cancellationToken).ConfigureAwait(false);

        var genesis = CreateGenesisHistory(genesisState);
        var continuity0 = HistoryIntegrity.ComputeGenesisContinuityToken(genesisState.Header.WorldId, genesis.RecordDigest);

        await using var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths, cancellationToken).ConfigureAwait(false);
        await store.InitializeWorldMetadataAsync(
            new WorldPersistenceMetadataSeed(
                genesisState.Header.WorldId,
                PersistenceGeneration: 1,
                Qa04ReferenceLoadV1.WorldSeed,
                continuity0,
                ConfigGeneration: genesisState.Header.ConfigGeneration,
                genesisState.Diagnostic.ConfigDigest,
                MasterGeneration: genesisState.Header.MasterGeneration),
            genesis,
            cancellationToken).ConfigureAwait(false);

        var operationA = DeriveOperationId(0);
        var operationB = DeriveOperationId(1);
        var payloadA = DeriveOperationPayloadDigest(0);
        var payloadB = DeriveOperationPayloadDigest(1);
        var orderA = CreateOrderKey(operationA, 0);
        var orderB = CreateOrderKey(operationB, 1);

        var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var acceptA = CreateAcceptedHistory(genesisState.Header.WorldId, operationA, payloadA, anchor.Sequence + 1, anchor.Digest);
        await store.PersistAcceptedOperationAsync(operationA, payloadA, acceptA, cancellationToken).ConfigureAwait(false);
        anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var scheduleA = CreateScheduledHistory(genesisState.Header.WorldId, operationA, 0, orderA, anchor.Sequence + 1, anchor.Digest);
        await store.PersistScheduledOperationAsync(operationA, 0, orderA, scheduleA, cancellationToken).ConfigureAwait(false);

        anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var acceptB = CreateAcceptedHistory(genesisState.Header.WorldId, operationB, payloadB, anchor.Sequence + 1, anchor.Digest);
        await store.PersistAcceptedOperationAsync(operationB, payloadB, acceptB, cancellationToken).ConfigureAwait(false);
        anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var scheduleB = CreateScheduledHistory(genesisState.Header.WorldId, operationB, 1, orderB, anchor.Sequence + 1, anchor.Digest);
        await store.PersistScheduledOperationAsync(operationB, 1, orderB, scheduleB, cancellationToken).ConfigureAwait(false);

        var scheduler = new OperationSchedulerStateV1(
            nextSchedulableStep: 0,
            freezeStep: null,
            scheduled:
            [
                new ScheduledOperationRefV1(operationA, 0, orderA),
                new ScheduledOperationRefV1(operationB, 1, orderB),
            ]);
        var durableBeforeStep0 = await store.ListOperationStatesCanonicalAsync(cancellationToken).ConfigureAwait(false);
        var state0 = BindCoreAuthority(
            genesisState,
            OperationSchedulerSubstateV1.Canonicalize(scheduler, 0),
            DurableOperationSubstateV1.Canonicalize(durableBeforeStep0));

        var step0 = await ExecuteStepAsync(
            state0,
            scheduler,
            store,
            materialized.Partition,
            workerCount,
            continuity0,
            operationA,
            cancellationToken).ConfigureAwait(false);

        var durableAfterStep0 = await store.ListOperationStatesCanonicalAsync(cancellationToken).ConfigureAwait(false);
        var schedulerDigestAfterStep0 = OperationSchedulerSubstateV1.Canonicalize(scheduler, 1);
        var operationDigestAfterStep0 = DurableOperationSubstateV1.Canonicalize(durableAfterStep0);
        var firstSchedulerMatch = DigestEquals(step0.Authoritative.State.SchedulerState, schedulerDigestAfterStep0);
        var firstOperationMatch = DigestEquals(step0.Authoritative.State.OperationState, operationDigestAfterStep0);
        if (!firstSchedulerMatch || !firstOperationMatch)
            throw new InvalidDataException("qa04.core-substate.step0-authority-digest-mismatch");

        var operationAAfterStep0 = durableAfterStep0.Single(item => item.OperationId == operationA);
        var operationBAfterStep0 = durableAfterStep0.Single(item => item.OperationId == operationB);
        var futureCarried = operationAAfterStep0.Lifecycle == DurableOperationLifecycleV1.TerminalDurable &&
                            operationBAfterStep0.Lifecycle == DurableOperationLifecycleV1.ScheduledDurable &&
                            operationBAfterStep0.EffectiveStep == 1 &&
                            scheduler.ForEffectiveStep(0).Count == 0 &&
                            scheduler.ForEffectiveStep(1).Count == 1 &&
                            scheduler.ForEffectiveStep(1)[0].OperationId == operationB;
        if (!futureCarried)
            throw new InvalidDataException("qa04.core-substate.future-operation-not-carried");

        var step1 = await ExecuteStepAsync(
            step0.Authoritative.State,
            scheduler,
            store,
            materialized.Partition,
            workerCount,
            step0.ContinuityToken,
            operationB,
            cancellationToken).ConfigureAwait(false);

        var durableAfterStep1 = await store.ListOperationStatesCanonicalAsync(cancellationToken).ConfigureAwait(false);
        var schedulerDigestAfterStep1 = OperationSchedulerSubstateV1.Canonicalize(scheduler, 2);
        var operationDigestAfterStep1 = DurableOperationSubstateV1.Canonicalize(durableAfterStep1);
        var secondSchedulerMatch = DigestEquals(step1.Authoritative.State.SchedulerState, schedulerDigestAfterStep1);
        var secondOperationMatch = DigestEquals(step1.Authoritative.State.OperationState, operationDigestAfterStep1);
        if (!secondSchedulerMatch || !secondOperationMatch)
            throw new InvalidDataException("qa04.core-substate.step1-authority-digest-mismatch");

        var finalA = durableAfterStep1.Single(item => item.OperationId == operationA);
        var finalB = durableAfterStep1.Single(item => item.OperationId == operationB);
        var firstTerminal = finalA.Lifecycle == DurableOperationLifecycleV1.TerminalDurable && finalA.TerminalSequence is not null;
        var secondTerminal = finalB.Lifecycle == DurableOperationLifecycleV1.TerminalDurable && finalB.TerminalSequence is not null;
        if (!firstTerminal || !secondTerminal)
            throw new InvalidDataException("qa04.core-substate.operation-terminal-authority-missing");

        var firstChain = step0.Authoritative.State.Header.PreviousStateDigest is { } firstPrevious &&
            CryptographicOperations.FixedTimeEquals(firstPrevious, state0.Diagnostic.StateDigest);
        var secondChain = step1.Authoritative.State.Header.PreviousStateDigest is { } secondPrevious &&
            CryptographicOperations.FixedTimeEquals(secondPrevious, step0.Authoritative.State.Diagnostic.StateDigest);
        if (!firstChain || !secondChain)
            throw new InvalidDataException("qa04.core-substate.state-chain-invalid");

        var recovery = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        var durableThroughTwo = recovery.FinalizedStep == 2 &&
            CryptographicOperations.FixedTimeEquals(recovery.ContinuityToken, step1.ContinuityToken);
        if (!durableThroughTwo)
            throw new InvalidDataException("qa04.core-substate.sqlite-recovery-head-mismatch");

        return new Qa04CoreSubstateAuthorityProbeV1(
            BasisStep: state0.Header.Step,
            FirstResultingStep: step0.Authoritative.State.Header.Step,
            FinalResultingStep: step1.Authoritative.State.Header.Step,
            WorkerCount: workerCount,
            ResidentRecordCount: residentRecordCount,
            FirstStepCoreSubstateCandidateCount: step0.Candidate.CoreSubstateCandidates.Count,
            SecondStepCoreSubstateCandidateCount: step1.Candidate.CoreSubstateCandidates.Count,
            FirstStepFrozenOperationCount: step0.Candidate.FrozenInput.ScheduledOperations.Count,
            SecondStepFrozenOperationCount: step1.Candidate.FrozenInput.ScheduledOperations.Count,
            FutureOperationCarriedToSecondStep: futureCarried,
            FirstStepSchedulerDigestMatchedDurableRuntime: firstSchedulerMatch,
            FirstStepOperationDigestMatchedSqlite: firstOperationMatch,
            SecondStepSchedulerDigestMatchedDurableRuntime: secondSchedulerMatch,
            SecondStepOperationDigestMatchedSqlite: secondOperationMatch,
            FirstOperationTerminalDurable: firstTerminal,
            SecondOperationTerminalDurable: secondTerminal,
            FirstStateChainValid: firstChain,
            SecondStateChainValid: secondChain,
            RealSqliteCommitObservedThroughStepTwo: durableThroughTwo,
            BasisStateDigest: Hex(state0.Diagnostic.StateDigest),
            FirstStateDigest: Hex(step0.Authoritative.State.Diagnostic.StateDigest),
            FinalStateDigest: Hex(step1.Authoritative.State.Diagnostic.StateDigest),
            FinalContinuityToken: Hex(step1.ContinuityToken),
            BlockingFailureCodes:
            [
                "qa04.target.reference-world-other-partitions-not-materialized",
                "qa04.target.canonical-operation-load-not-injected",
                "qa04.target.detail-substate-mutation-application-not-assembled",
                "qa04.target.authoritative-full-step-loop-not-assembled",
            ]);
    }

    private static async Task<StepExecutionReceipt> ExecuteStepAsync(
        WorldStateV1 state,
        OperationSchedulerStateV1 scheduler,
        SqlitePersistenceStore store,
        DomainPartitionStateV1<Qa04ResidentIdentityLifecyclePayloadV1> residentPartition,
        int workerCount,
        byte[] previousContinuityToken,
        OpaqueId128 terminalOperationId,
        CancellationToken cancellationToken)
    {
        var frozen = StepInputFreezerV1.Freeze(state, scheduler);
        if (frozen.ScheduledOperations.Count != 1 || frozen.ScheduledOperations[0].OperationId != terminalOperationId)
            throw new InvalidDataException("qa04.core-substate.frozen-operation-mismatch");

        var durableBeforeTransition = await store.ListOperationStatesCanonicalAsync(cancellationToken).ConfigureAwait(false);
        var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var transitionSequence = checked(anchor.Sequence + 1);
        var terminal = new TerminalOperationCommit(
            terminalOperationId,
            (int)CoreOperationResultStatusV1.Success,
            state.Header.Step == 0 ? "qa04.step0.success" : "qa04.step1.success");

        var schedulerCandidate = OperationSchedulerSubstateV1.CreatePostFinalizationCandidate(state, scheduler, frozen);
        var operationCandidate = DurableOperationSubstateV1.CreatePostTransitionCandidate(
            state,
            durableBeforeTransition,
            [terminal],
            state.Header.Step,
            transitionSequence);

        var outputs = await DomainRuntimeExecutorV1.ExecuteAsync(
            StandardDomainExecutionPlanV1.Create(),
            state,
            frozen,
            CreateStructuralRuntimes(residentPartition),
            workerCount,
            cancellationToken).ConfigureAwait(false);

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
            ],
            coreSubstateCandidates: [schedulerCandidate, operationCandidate]);
        if (candidate.CoreSubstateCandidates.Count != 2 ||
            !candidate.CoreSubstateCandidates.Any(item => item.Kind == StepCoreSubstateKindV1.Scheduler) ||
            !candidate.CoreSubstateCandidates.Any(item => item.Kind == StepCoreSubstateKindV1.Operation))
            throw new InvalidDataException("qa04.core-substate.candidate-coverage-mismatch");

        var basisResident = state.Partitions.Get("resident.identity_lifecycle").Header;
        var resultingResident = CreateResultingResidentHeader(residentPartition, basisResident, candidate.TargetStep);
        var prepared = StepStateApplicationV1.Prepare(
            state,
            candidate,
            [new StepPartitionStateMaterialV1(resultingResident)],
            [
                new StepCoreSubstateStateMaterialV1(StepCoreSubstateKindV1.Scheduler, schedulerCandidate.ResultingState),
                new StepCoreSubstateStateMaterialV1(StepCoreSubstateKindV1.Operation, operationCandidate.ResultingState),
            ]);
        if (candidate.IsPublishable || prepared.IsPublishable)
            throw new InvalidDataException("qa04.core-substate.premature-authority");

        var transition = CreateTransitionHistory(candidate, prepared, transitionSequence, anchor.Digest);
        var continuity = HistoryIntegrity.ComputeTransitionContinuityToken(
            candidate.WorldId,
            candidate.TargetStep,
            previousContinuityToken,
            transition.RecordDigest);
        var finalizeMaterial = new StepFinalizeMaterialV1(
            candidate.ConfigGeneration,
            candidate.ConfigDigest,
            continuity,
            transition,
            [terminal]);
        var receipt = await new StepFinalizationCoordinatorV1(new SqliteStepTransitionDurabilityV1(store))
            .FinalizeAsync(candidate, scheduler, finalizeMaterial, cancellationToken)
            .ConfigureAwait(false);
        var authoritative = StepStateApplicationV1.Publish(prepared, receipt);
        if (!receipt.IsPublishable || !authoritative.IsPublishable)
            throw new InvalidDataException("qa04.core-substate.post-commit-authority-missing");

        return new StepExecutionReceipt(candidate, authoritative, continuity);
    }

    private static WorldStateV1 BindCoreAuthority(
        WorldStateV1 state,
        WorldSubstateRefV1 schedulerState,
        WorldSubstateRefV1 operationState)
        => new(
            new WorldStateHeaderV1(
                state.Header.WorldId,
                state.Header.Step,
                state.Header.WorldSeedDigest,
                state.Header.ConfigGeneration,
                state.Header.MasterGeneration,
                state.Header.RateGeneration,
                state.Header.PreviousStateDigest),
            state.Partitions,
            schedulerState,
            operationState,
            state.DetailState,
            state.DomainRegistryState,
            state.Diagnostic.ConfigDigest);

    private static IReadOnlyCollection<IDomainRuntimeV1> CreateStructuralRuntimes(
        DomainPartitionStateV1<Qa04ResidentIdentityLifecyclePayloadV1> residentPartition)
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
            var current = context.State.Partitions.Get("resident.identity_lifecycle").Header;
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
                    "resident.identity_lifecycle",
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
        DomainPartitionStateV1<Qa04ResidentIdentityLifecyclePayloadV1> partition,
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

    private static HistoryRecordMaterial CreateAcceptedHistory(
        OpaqueId128 worldId,
        OpaqueId128 operationId,
        byte[] payloadDigest,
        ulong sequence,
        byte[] previousRecordDigest)
        => HistoryRecordMaterial.Create(
            worldId,
            sequence,
            previousRecordDigest,
            "operation.accepted.v1",
            "core.operation-accepted.v1",
            1,
            0,
            operationId.ToBytes().Concat(payloadDigest).ToArray(),
            writer =>
            {
                writer.WriteMapStart(2);
                writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteBytes(payloadDigest);
            });

    private static HistoryRecordMaterial CreateScheduledHistory(
        OpaqueId128 worldId,
        OpaqueId128 operationId,
        ulong effectiveStep,
        SameStepOrderKey orderKey,
        ulong sequence,
        byte[] previousRecordDigest)
        => HistoryRecordMaterial.Create(
            worldId,
            sequence,
            previousRecordDigest,
            "operation.scheduled.v1",
            "core.operation-scheduled.v1",
            1,
            0,
            operationId.ToBytes().Concat(orderKey.ToDatabaseBytes()).ToArray(),
            writer =>
            {
                writer.WriteMapStart(3);
                writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteUnsigned(effectiveStep);
                writer.WriteUnsigned(2); writer.WriteBytes(orderKey.ToDatabaseBytes());
            });

    private static HistoryRecordMaterial CreateTransitionHistory(
        StepCandidateV1 candidate,
        PreparedStepWorldStateV1 prepared,
        ulong sequence,
        byte[] previousRecordDigest)
        => HistoryRecordMaterial.Create(
            candidate.WorldId,
            sequence,
            previousRecordDigest,
            "transition.committed.v1",
            "persistence.transition-committed",
            1,
            0,
            candidate.DiagnosticDigest.Concat(prepared.ResultingState.Diagnostic.StateDigest).ToArray(),
            writer =>
            {
                writer.WriteMapStart(8);
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
                writer.WriteUnsigned(7);
                writer.WriteArrayStart((ulong)candidate.CoreSubstateCandidates.Count);
                foreach (var core in candidate.CoreSubstateCandidates)
                {
                    writer.WriteArrayStart(2);
                    writer.WriteUnsigned((byte)core.Kind);
                    writer.WriteBytes(core.CandidateDigest);
                }
            });

    private static OpaqueId128 DeriveOperationId(ulong ordinal)
    {
        var digest = HashSuite.DomainHash("mv.qa04-core-substate-operation-id.v1", writer =>
        {
            writer.WriteMapStart(2);
            writer.WriteUnsigned(0); writer.WriteBytes(Qa04ReferenceLoadV1.WorldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteUnsigned(ordinal);
        });
        var value = HashSuite.Trunc128(digest);
        if (value.IsZero) throw new InvalidDataException("qa04.core-substate.operation-id-zero");
        return value;
    }

    private static byte[] DeriveOperationPayloadDigest(ulong ordinal)
        => HashSuite.DomainHash("mv.qa04-core-substate-operation-payload.v1", writer =>
        {
            writer.WriteMapStart(2);
            writer.WriteUnsigned(0); writer.WriteBytes(Qa04ReferenceLoadV1.WorldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteUnsigned(ordinal);
        });

    private static SameStepOrderKey CreateOrderKey(OpaqueId128 operationId, ulong effectiveStep)
        => new(
            phase: 0,
            domainRank: 0,
            conflictScopeDigest: HashSuite.DomainHash("mv.qa04-core-substate-order-scope.v1", writer =>
            {
                writer.WriteMapStart(2);
                writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteUnsigned(effectiveStep);
            }),
            semanticPriority: 0,
            intentId: operationId);

    private static OpaqueId128 DeriveCandidateId(ulong basisStep)
    {
        var digest = HashSuite.DomainHash("mv.qa04-core-substate-step-candidate.v1", writer =>
        {
            writer.WriteMapStart(3);
            writer.WriteUnsigned(0); writer.WriteBytes(Qa04ReferenceLoadV1.WorldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteUnsigned(basisStep);
            writer.WriteUnsigned(2); writer.WriteAsciiText(Qa04ReferenceLoadV1.BenchmarkProfileId);
        });
        var value = HashSuite.Trunc128(digest);
        if (value.IsZero) throw new InvalidDataException("qa04.core-substate.candidate-id-zero");
        return value;
    }

    private static bool DigestEquals(WorldSubstateRefV1 left, WorldSubstateRefV1 right)
        => left.Schema == right.Schema &&
           CryptographicOperations.FixedTimeEquals(left.CanonicalDigest, right.CanonicalDigest);

    private static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();

    private sealed record StepExecutionReceipt(
        StepCandidateV1 Candidate,
        AuthoritativeStepWorldStateV1 Authoritative,
        byte[] ContinuityToken);
}
