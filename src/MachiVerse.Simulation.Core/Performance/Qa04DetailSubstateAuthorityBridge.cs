using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04DetailSubstateAuthorityProbeV1(
    ulong BasisStep,
    ulong FirstResultingStep,
    ulong FinalResultingStep,
    int WorkerCount,
    ulong ResidentRecordCount,
    int FirstStepCoreSubstateCandidateCount,
    int SecondStepCoreSubstateCandidateCount,
    bool DetailPromotionApplied,
    bool FirstStepDetailDigestMatchedRuntime,
    bool SecondStepDetailDigestMatchedRuntime,
    bool DetailLineageCarriedToSecondStep,
    bool FirstStateChainValid,
    bool SecondStateChainValid,
    bool RealSqliteCommitObservedThroughStepTwo,
    string BasisDetailDigest,
    string FirstDetailDigest,
    string FinalDetailDigest,
    string FinalContinuityToken,
    string[] BlockingFailureCodes)
{
    public bool ReducedDetailAuthorityAvailable => true;
    public bool ReferenceWorldMaterialized => false;
    public bool AuthoritativeStepLoopAvailable => false;
    public bool ReleaseEvidenceCapable => false;
}

/// <summary>
/// Reduced two-Step proof for candidate-bound detail authority. Step 0 promotes one resident detail
/// region from D2 to D0 using a real frozen scheduled Operation as trigger, the existing planner and
/// conservation gate, and the normal SQLite-backed Step finalization boundary. State 1 is then reused
/// as the exact basis of Step 1, where the resulting detail directory is explicitly rebound unchanged.
/// </summary>
public static class Qa04DetailSubstateAuthorityBridgeV1
{
    private static readonly StableToken StructuralInvariant = new("qa04.detail-substate-authority-path");
    private static readonly StableToken ResidentDomain = new("resident");
    private static readonly DetailTransitionPolicyV1 ReducedPolicy = new(
        PromotionHysteresisSteps: 0,
        DemotionQuietSteps: 0,
        MinimumResidenceSteps: 0,
        BoundResidentFloor: DetailLevelV1.D0Entity,
        ActiveTransactionFloor: DetailLevelV1.D0Entity,
        PromotionMaxRegionsPerStep: 4,
        PromotionMaxRecordsPerStep: 20_000,
        DemotionMaxRegionsPerStep: 8,
        DemotionMaxRecordsPerStep: 50_000);

    public static async Task<Qa04DetailSubstateAuthorityProbeV1> RunTwoStepAsync(
        int workerCount,
        ulong residentRecordCount,
        string persistenceRoot,
        CancellationToken cancellationToken = default)
    {
        if (!Qa04DomainExecutionTargetV1.CanonicalWorkerCounts.Contains(workerCount))
            throw new InvalidDataException("qa04.detail-substate.worker-count-not-canonical");
        if (residentRecordCount is 0 or > Qa04ReferenceWorldMaterializerV1.CanonicalResidentCount)
            throw new ArgumentOutOfRangeException(nameof(residentRecordCount));
        if (string.IsNullOrWhiteSpace(persistenceRoot))
            throw new ArgumentException("Persistence root is required.", nameof(persistenceRoot));

        var materialized = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(residentRecordCount);
        var detailDirectory0 = CreateInitialDetailDirectory();
        var genesisState = BindCoreAuthority(
            materialized.WorldState,
            materialized.WorldState.SchedulerState,
            materialized.WorldState.OperationState,
            DetailDirectorySubstateV1.Canonicalize(detailDirectory0));
        if (genesisState.Header.Step != 0)
            throw new InvalidDataException("qa04.detail-substate.genesis-step-mismatch");

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
        await PersistAcceptedAndScheduledAsync(store, genesisState.Header.WorldId, operationA, 0, cancellationToken).ConfigureAwait(false);
        await PersistAcceptedAndScheduledAsync(store, genesisState.Header.WorldId, operationB, 1, cancellationToken).ConfigureAwait(false);

        var scheduler = new OperationSchedulerStateV1(
            nextSchedulableStep: 0,
            freezeStep: null,
            scheduled:
            [
                new ScheduledOperationRefV1(operationA, 0, CreateOrderKey(operationA, 0)),
                new ScheduledOperationRefV1(operationB, 1, CreateOrderKey(operationB, 1)),
            ]);
        var durableBeforeStep0 = await store.ListOperationStatesCanonicalAsync(cancellationToken).ConfigureAwait(false);
        var state0 = BindCoreAuthority(
            genesisState,
            OperationSchedulerSubstateV1.Canonicalize(scheduler, 0),
            DurableOperationSubstateV1.Canonicalize(durableBeforeStep0),
            DetailDirectorySubstateV1.Canonicalize(detailDirectory0));

        var basisDetailDigest = state0.DetailState.CanonicalDigest.ToArray();
        var step0 = await ExecuteStepAsync(
            state0,
            scheduler,
            store,
            detailDirectory0,
            workerCount,
            continuity0,
            operationA,
            applyPromotion: true,
            cancellationToken).ConfigureAwait(false);

        var detailAfterStep0 = DetailDirectorySubstateV1.Canonicalize(step0.DetailDirectory);
        var firstDetailMatch = DigestEquals(step0.Authoritative.State.DetailState, detailAfterStep0);
        var regionAfterStep0 = step0.DetailDirectory.Regions.Single();
        var promotionApplied = regionAfterStep0.GetLevel(ResidentDomain) == DetailLevelV1.D0Entity &&
                               regionAfterStep0.LineageGeneration == 2;
        if (!firstDetailMatch || !promotionApplied)
            throw new InvalidDataException("qa04.detail-substate.step0-authority-mismatch");

        var step1 = await ExecuteStepAsync(
            step0.Authoritative.State,
            scheduler,
            store,
            step0.DetailDirectory,
            workerCount,
            step0.ContinuityToken,
            operationB,
            applyPromotion: false,
            cancellationToken).ConfigureAwait(false);

        var detailAfterStep1 = DetailDirectorySubstateV1.Canonicalize(step1.DetailDirectory);
        var secondDetailMatch = DigestEquals(step1.Authoritative.State.DetailState, detailAfterStep1);
        var regionAfterStep1 = step1.DetailDirectory.Regions.Single();
        var lineageCarried = regionAfterStep1.GetLevel(ResidentDomain) == DetailLevelV1.D0Entity &&
                             regionAfterStep1.LineageGeneration == regionAfterStep0.LineageGeneration &&
                             CryptographicOperations.FixedTimeEquals(
                                 step0.Authoritative.State.DetailState.CanonicalDigest,
                                 step1.Authoritative.State.DetailState.CanonicalDigest);
        if (!secondDetailMatch || !lineageCarried)
            throw new InvalidDataException("qa04.detail-substate.step1-authority-mismatch");

        var firstChain = step0.Authoritative.State.Header.PreviousStateDigest is { } firstPrevious &&
                         CryptographicOperations.FixedTimeEquals(firstPrevious, state0.Diagnostic.StateDigest);
        var secondChain = step1.Authoritative.State.Header.PreviousStateDigest is { } secondPrevious &&
                          CryptographicOperations.FixedTimeEquals(secondPrevious, step0.Authoritative.State.Diagnostic.StateDigest);
        if (!firstChain || !secondChain)
            throw new InvalidDataException("qa04.detail-substate.state-chain-invalid");

        var recovery = await store.ReadRecoveryHeadAsync(cancellationToken).ConfigureAwait(false);
        var durableThroughTwo = recovery.FinalizedStep == 2 &&
                                CryptographicOperations.FixedTimeEquals(recovery.ContinuityToken, step1.ContinuityToken);
        if (!durableThroughTwo)
            throw new InvalidDataException("qa04.detail-substate.sqlite-recovery-head-mismatch");

        return new Qa04DetailSubstateAuthorityProbeV1(
            BasisStep: state0.Header.Step,
            FirstResultingStep: step0.Authoritative.State.Header.Step,
            FinalResultingStep: step1.Authoritative.State.Header.Step,
            WorkerCount: workerCount,
            ResidentRecordCount: residentRecordCount,
            FirstStepCoreSubstateCandidateCount: step0.Candidate.CoreSubstateCandidates.Count,
            SecondStepCoreSubstateCandidateCount: step1.Candidate.CoreSubstateCandidates.Count,
            DetailPromotionApplied: promotionApplied,
            FirstStepDetailDigestMatchedRuntime: firstDetailMatch,
            SecondStepDetailDigestMatchedRuntime: secondDetailMatch,
            DetailLineageCarriedToSecondStep: lineageCarried,
            FirstStateChainValid: firstChain,
            SecondStateChainValid: secondChain,
            RealSqliteCommitObservedThroughStepTwo: durableThroughTwo,
            BasisDetailDigest: Hex(basisDetailDigest),
            FirstDetailDigest: Hex(step0.Authoritative.State.DetailState.CanonicalDigest),
            FinalDetailDigest: Hex(step1.Authoritative.State.DetailState.CanonicalDigest),
            FinalContinuityToken: Hex(step1.ContinuityToken),
            BlockingFailureCodes:
            [
                "qa04.target.reference-world-other-partitions-not-materialized",
                "qa04.target.canonical-operation-load-not-injected",
                "qa04.target.canonical-detail-workload-not-injected",
                "qa04.target.authoritative-full-step-loop-not-assembled",
            ]);
    }

    private static async Task<StepExecutionReceipt> ExecuteStepAsync(
        WorldStateV1 state,
        OperationSchedulerStateV1 scheduler,
        SqlitePersistenceStore store,
        DetailDirectoryV1 detailDirectory,
        int workerCount,
        byte[] previousContinuityToken,
        OpaqueId128 terminalOperationId,
        bool applyPromotion,
        CancellationToken cancellationToken)
    {
        var frozen = StepInputFreezerV1.Freeze(state, scheduler);
        if (frozen.ScheduledOperations.Count != 1 || frozen.ScheduledOperations[0].OperationId != terminalOperationId)
            throw new InvalidDataException("qa04.detail-substate.frozen-operation-mismatch");

        var durableBeforeTransition = await store.ListOperationStatesCanonicalAsync(cancellationToken).ConfigureAwait(false);
        var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var transitionSequence = checked(anchor.Sequence + 1);
        var terminal = new TerminalOperationCommit(
            terminalOperationId,
            (int)CoreOperationResultStatusV1.Success,
            applyPromotion ? "qa04.detail.step0.success" : "qa04.detail.step1.success");

        var schedulerCandidate = OperationSchedulerSubstateV1.CreatePostFinalizationCandidate(state, scheduler, frozen);
        var operationCandidate = DurableOperationSubstateV1.CreatePostTransitionCandidate(
            state,
            durableBeforeTransition,
            [terminal],
            state.Header.Step,
            transitionSequence);

        IReadOnlyList<DetailTransitionCandidateV1> detailRequests;
        if (applyPromotion)
        {
            var region = detailDirectory.Regions.Single();
            var request = new DetailTransitionRequestV1(
                region.DetailRegionId,
                ResidentDomain,
                region.GetLevel(ResidentDomain),
                DetailLevelV1.D0Entity,
                requiredEffectiveStep: state.Header.Step,
                semanticPriority: 0,
                DetailTransitionTriggerSourceV1.ScheduledOperation,
                terminalOperationId,
                triggerObservedStep: state.Header.Step,
                estimatedRecordCount: 1);
            detailRequests =
            [
                DetailTransitionAdmissionV1.Admit(
                    request,
                    DetailTransitionTriggerAuthorityV1.FromStep(frozen)),
            ];
        }
        else
        {
            detailRequests = Array.Empty<DetailTransitionCandidateV1>();
        }

        var detailPlan = DetailTransitionPlannerV1.Plan(
            detailDirectory,
            detailRequests,
            state.Header.Step,
            ReducedPolicy);
        if (applyPromotion && detailPlan.Selected.Count != 1)
            throw new InvalidDataException("qa04.detail-substate.promotion-not-selected");
        if (!applyPromotion && detailPlan.Selected.Count != 0)
            throw new InvalidDataException("qa04.detail-substate.unexpected-second-step-transition");

        var conservation = DetailConservationInvariantV1.ValidateForTransitions(
            detailPlan.Selected,
            new DetailConservationSnapshotV1(),
            new DetailConservationSnapshotV1());
        var detailProjection = DetailDirectorySubstateV1.CreatePostTransitionCandidate(
            state,
            detailDirectory,
            detailPlan,
            conservation);

        var outputs = await DomainRuntimeExecutorV1.ExecuteAsync(
            StandardDomainExecutionPlanV1.Create(),
            state,
            frozen,
            CreateStructuralRuntimes(),
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
            coreSubstateCandidates:
            [
                schedulerCandidate,
                operationCandidate,
                detailProjection.Candidate,
            ]);
        if (candidate.CoreSubstateCandidates.Count != 3 ||
            !candidate.CoreSubstateCandidates.Any(item => item.Kind == StepCoreSubstateKindV1.Scheduler) ||
            !candidate.CoreSubstateCandidates.Any(item => item.Kind == StepCoreSubstateKindV1.Operation) ||
            !candidate.CoreSubstateCandidates.Any(item => item.Kind == StepCoreSubstateKindV1.Detail))
            throw new InvalidDataException("qa04.detail-substate.candidate-coverage-mismatch");

        var prepared = StepStateApplicationV1.Prepare(
            state,
            candidate,
            Array.Empty<StepPartitionStateMaterialV1>(),
            [
                new StepCoreSubstateStateMaterialV1(StepCoreSubstateKindV1.Scheduler, schedulerCandidate.ResultingState),
                new StepCoreSubstateStateMaterialV1(StepCoreSubstateKindV1.Operation, operationCandidate.ResultingState),
                new StepCoreSubstateStateMaterialV1(StepCoreSubstateKindV1.Detail, detailProjection.Candidate.ResultingState),
            ]);
        if (candidate.IsPublishable || prepared.IsPublishable)
            throw new InvalidDataException("qa04.detail-substate.premature-authority");

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
            throw new InvalidDataException("qa04.detail-substate.post-commit-authority-missing");

        return new StepExecutionReceipt(candidate, authoritative, detailProjection.ResultingDirectory, continuity);
    }

    private static DetailDirectoryV1 CreateInitialDetailDirectory()
        => new(
        [
            new DetailRegionStateV1(
                DeriveOpaqueId("detail-region"),
                DeriveOpaqueId("detail-spatial-scope"),
                [new KeyValuePair<StableToken, DetailLevelV1>(ResidentDomain, DetailLevelV1.D2RegionalAggregate)],
                lineageGeneration: 1,
                lastTransitionStep: 0),
        ]);

    private static IReadOnlyCollection<IDomainRuntimeV1> CreateStructuralRuntimes()
    {
        static ValueTask<IReadOnlyList<MutationIntentCandidateV1>> NoIntents(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<MutationIntentCandidateV1>>(
                Array.Empty<MutationIntentCandidateV1>());
        }

        return
        [
            new SpatialDomainRuntimeV1(NoIntents),
            new EnvironmentDomainRuntimeV1(NoIntents),
            new PhysicalBuiltDomainRuntimeV1(NoIntents),
            new ParticipationDomainRuntimeV1(NoIntents),
            new ResidentDomainRuntimeV1(NoIntents),
            new SocietyEconomyDomainRuntimeV1(NoIntents),
            new GovernanceSecurityDomainRuntimeV1(NoIntents),
            new InfrastructureInformationDomainRuntimeV1(NoIntents),
        ];
    }

    private static async Task PersistAcceptedAndScheduledAsync(
        SqlitePersistenceStore store,
        OpaqueId128 worldId,
        OpaqueId128 operationId,
        ulong effectiveStep,
        CancellationToken cancellationToken)
    {
        var payload = DeriveOperationPayloadDigest(effectiveStep);
        var anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var accepted = CreateAcceptedHistory(worldId, operationId, payload, anchor.Sequence + 1, anchor.Digest);
        await store.PersistAcceptedOperationAsync(operationId, payload, accepted, cancellationToken).ConfigureAwait(false);

        anchor = await store.ReadHistoryAnchorAsync(cancellationToken).ConfigureAwait(false);
        var orderKey = CreateOrderKey(operationId, effectiveStep);
        var scheduled = CreateScheduledHistory(worldId, operationId, effectiveStep, orderKey, anchor.Sequence + 1, anchor.Digest);
        await store.PersistScheduledOperationAsync(operationId, effectiveStep, orderKey, scheduled, cancellationToken).ConfigureAwait(false);
    }

    private static WorldStateV1 BindCoreAuthority(
        WorldStateV1 state,
        WorldSubstateRefV1 schedulerState,
        WorldSubstateRefV1 operationState,
        WorldSubstateRefV1 detailState)
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
            detailState,
            state.DomainRegistryState,
            state.Diagnostic.ConfigDigest);

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
                writer.WriteUnsigned(6); writer.WriteArrayStart(0);
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
        => DeriveOpaqueId($"operation-{ordinal}");

    private static OpaqueId128 DeriveCandidateId(ulong basisStep)
        => DeriveOpaqueId($"candidate-{basisStep}");

    private static OpaqueId128 DeriveOpaqueId(string purpose)
    {
        var value = HashSuite.Trunc128(HashSuite.DomainHash("mv.qa04-detail-substate-id.v1", writer =>
        {
            writer.WriteMapStart(2);
            writer.WriteUnsigned(0); writer.WriteBytes(Qa04ReferenceLoadV1.WorldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteAsciiText(purpose);
        }));
        if (value.IsZero) throw new InvalidDataException("qa04.detail-substate.derived-id-zero");
        return value;
    }

    private static byte[] DeriveOperationPayloadDigest(ulong ordinal)
        => HashSuite.DomainHash("mv.qa04-detail-substate-operation-payload.v1", writer =>
        {
            writer.WriteMapStart(2);
            writer.WriteUnsigned(0); writer.WriteBytes(Qa04ReferenceLoadV1.WorldId.ToBytes());
            writer.WriteUnsigned(1); writer.WriteUnsigned(ordinal);
        });

    private static SameStepOrderKey CreateOrderKey(OpaqueId128 operationId, ulong effectiveStep)
        => new(
            phase: 0,
            domainRank: 0,
            conflictScopeDigest: HashSuite.DomainHash("mv.qa04-detail-substate-order-scope.v1", writer =>
            {
                writer.WriteMapStart(2);
                writer.WriteUnsigned(0); writer.WriteBytes(operationId.ToBytes());
                writer.WriteUnsigned(1); writer.WriteUnsigned(effectiveStep);
            }),
            semanticPriority: 0,
            intentId: operationId);

    private static bool DigestEquals(WorldSubstateRefV1 left, WorldSubstateRefV1 right)
        => left.Schema == right.Schema &&
           CryptographicOperations.FixedTimeEquals(left.CanonicalDigest, right.CanonicalDigest);

    private static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();

    private sealed record StepExecutionReceipt(
        StepCandidateV1 Candidate,
        AuthoritativeStepWorldStateV1 Authoritative,
        DetailDirectoryV1 DetailDirectory,
        byte[] ContinuityToken);
}
