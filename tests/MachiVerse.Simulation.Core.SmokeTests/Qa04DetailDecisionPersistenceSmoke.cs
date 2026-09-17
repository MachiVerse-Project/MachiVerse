using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

internal static class Qa04DetailDecisionPersistenceSmoke
{
    [ModuleInitializer]
    internal static void Run()
        => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-qa04-detail-decision-" + Guid.NewGuid().ToString("N"));
        var paths = PersistenceLayout.Resolve(root, Qa04ReferenceLoadV1.WorldId, 1);
        var config = Qa04ReferenceConfigAuthorityV1.CreateCanonical();
        try
        {
            PersistenceLayout.EnsureGenerationDirectories(paths);
            await PersistenceLayout.WriteCurrentAsync(paths, 1);

            var genesis = HistoryRecordMaterial.Create(
                Qa04ReferenceLoadV1.WorldId,
                sequence: 1,
                previousRecordDigest: new byte[32],
                recordType: "world.genesis.v1",
                payloadSchemaId: "core.world-genesis.v1",
                payloadSchemaMajor: 1,
                payloadSchemaMinor: 0,
                payloadBytes: Qa04ReferenceLoadV1.WorldId.ToBytes(),
                writeNormalizedPayload: writer =>
                {
                    writer.WriteArrayStart(2);
                    writer.WriteBytes(Qa04ReferenceLoadV1.WorldId.ToBytes());
                    writer.WriteBytes(Qa04ReferenceLoadV1.WorldSeed.ToBytes());
                });
            var genesisContinuity = HistoryIntegrity.ComputeGenesisContinuityToken(
                Qa04ReferenceLoadV1.WorldId,
                genesis.RecordDigest);

            Qa04DetailDecisionAuthorityV1 decision;
            Qa04TransitionCommittedAuthorityV1 transition;
            Qa04OperationClosedPrefixV1 resultingPrefix;

            await using (var store = await SqlitePersistenceStore.OpenOrCreateAsync(paths))
            {
                await store.InitializeWorldMetadataAsync(
                    new WorldPersistenceMetadataSeed(
                        Qa04ReferenceLoadV1.WorldId,
                        PersistenceGeneration: 1,
                        Qa04ReferenceLoadV1.WorldSeed,
                        genesisContinuity,
                        ConfigGeneration: 1,
                        config.Digest,
                        MasterGeneration: 1),
                    genesis);

                var state1History = HistoryRecordMaterial.Create(
                    Qa04ReferenceLoadV1.WorldId,
                    sequence: 2,
                    previousRecordDigest: genesis.RecordDigest,
                    recordType: "transition.committed.v1",
                    payloadSchemaId: "persistence.transition-committed",
                    payloadSchemaMajor: 1,
                    payloadSchemaMinor: 0,
                    payloadBytes: [1],
                    writeNormalizedPayload: writer =>
                    {
                        writer.WriteMapStart(2);
                        writer.WriteUnsigned(0); writer.WriteUnsigned(0);
                        writer.WriteUnsigned(1); writer.WriteUnsigned(1);
                    });
                var state1Continuity = HistoryIntegrity.ComputeTransitionContinuityToken(
                    Qa04ReferenceLoadV1.WorldId,
                    resultingStep: 1,
                    genesisContinuity,
                    state1History.RecordDigest);
                _ = await store.PersistTransitionCommitAsync(
                    effectiveStep: 0,
                    resultingStep: 1,
                    state1Continuity,
                    activeConfigGeneration: 1,
                    config.Digest,
                    state1History,
                    Array.Empty<TerminalOperationCommit>());

                var basisPrefix = Qa04OperationClosedPrefixV1.Empty();
                await store.InitializeQa04OperationClosedPrefixAsync(basisPrefix, stateStep: 1);

                var bindings = Qa04ReferenceLoadV1.OperationsForStep(0)
                    .Select(static descriptor => Qa04CanonicalOperationBindingV1.Bind(descriptor, 1))
                    .OrderBy(static binding => binding.OrderKey)
                    .ThenBy(static binding => binding.SourceDescriptor.OperationId)
                    .ToArray();
                Require(bindings.Length == checked((int)Qa04ReferenceLoadV1.SteadyOperationsPerStep),
                    "QA-04 detail decision persistence smoke must use the canonical 5,000-operation Step.");

                var batchAnchor = await store.ReadHistoryAnchorAsync();
                var batchAuthority = Qa04ScheduledOperationBatchAuthorityBuilderV1.Create(
                    Qa04ReferenceLoadV1.WorldId,
                    batchAnchor,
                    injectionStep: 0,
                    effectiveStep: 1,
                    bindings);
                _ = await store.PersistQa04ScheduledOperationBatchAsync(batchAuthority, bindings);

                var directory = new DetailDirectoryV1(Array.Empty<DetailRegionStateV1>());
                var policy = new DetailTransitionPolicyV1(
                    PromotionHysteresisSteps: 30,
                    DemotionQuietSteps: 300,
                    MinimumResidenceSteps: 300,
                    BoundResidentFloor: DetailLevelV1.D0Entity,
                    ActiveTransactionFloor: DetailLevelV1.D0Entity,
                    PromotionMaxRegionsPerStep: 4,
                    PromotionMaxRecordsPerStep: 20_000,
                    DemotionMaxRegionsPerStep: 8,
                    DemotionMaxRecordsPerStep: 50_000);
                var plan = DetailTransitionPlannerV1.Plan(
                    directory,
                    Array.Empty<DetailTransitionCandidateV1>(),
                    basisStep: 1,
                    policy);
                Require(plan.Selected.Count == 0 && plan.Deferred.Count == 0 && plan.NotYetEligible.Count == 0,
                    "Zero-candidate QA-04 Step must still produce an explicit empty Detail decision record.");

                var decisionAnchor = await store.ReadHistoryAnchorAsync();
                decision = Qa04DetailDecisionAuthorityBuilderV1.Create(
                    Qa04ReferenceLoadV1.WorldId,
                    decisionAnchor,
                    basisStep: 1,
                    plan);
                Require(decision.History.Sequence == checked(decisionAnchor.Sequence + 1UL) &&
                        decision.History.RecordType == "qa04.detail-promotion-decision.v1",
                    "Detail decision history material drifted before persistence.");

                var terminals = bindings
                    .Select(static binding => new TerminalOperationCommit(
                        binding.SourceDescriptor.OperationId,
                        (int)CoreOperationResultStatusV1.Success,
                        "operation.succeeded"))
                    .ToArray();
                var terminalBatchDigest = Qa04TerminalSemanticAuthorityV1.ComputeBatchDigest(bindings, terminals);
                var terminalStepDigest = Qa04TerminalSemanticAuthorityV1.ComputeStepItemDigest(
                    effectiveStep: 1,
                    operationCount: checked((ulong)terminals.Length),
                    terminalBatchDigest);
                var terminalAccumulator = new Qa04Step2DeterminismAccumulatorV1("mv.qa04-operation-terminal.v1");
                terminalAccumulator.Append(1, terminalStepDigest);
                resultingPrefix = basisPrefix.Advance(
                    injectionStep: 0,
                    operationCount: checked((ulong)terminals.Length),
                    terminalAccumulator.Digest,
                    resultingStateStep: 2);

                var recoveryBefore = await store.ReadRecoveryHeadAsync();
                transition = Qa04TransitionCommittedAuthorityV1.Create(
                    Qa04ReferenceLoadV1.WorldId,
                    historySequence: checked(decision.History.Sequence + 1UL),
                    previousHistoryRecordDigest: decision.History.RecordDigest,
                    effectiveStep: 1,
                    resultingStep: 2,
                    activeConfigGeneration: 1,
                    activeConfigDigest: config.Digest,
                    appliedOperationIds: bindings.Select(static binding => binding.SourceDescriptor.OperationId).ToArray(),
                    operationOutcomes: terminals,
                    previousStateContinuityToken: recoveryBefore.ContinuityToken,
                    stateDiagnosticHash: SHA256.HashData("qa04-detail-decision-state-2"u8),
                    partitionDigests:
                    [
                        new Qa04TransitionPartitionDigestV1(
                            "resident.identity",
                            SHA256.HashData("qa04-detail-decision-partition"u8)),
                    ]);
                Require(transition.History.Sequence == checked(decision.History.Sequence + 1UL) &&
                        transition.History.PreviousRecordDigest.AsSpan().SequenceEqual(decision.History.RecordDigest),
                    "transition.committed.v1 must immediately follow and bind the Detail decision record.");

                var durable = await store.PersistQa04CanonicalTransitionCommitAsync(
                    injectionStep: 0,
                    transition,
                    terminals,
                    basisPrefix,
                    resultingPrefix,
                    Array.Empty<CrossDomainTransactionStateV1>(),
                    CancellationToken.None,
                    decision);
                Require(durable.ResultingStep == 2 && durable.HistorySequence == transition.History.Sequence,
                    "Detail decision transition durability receipt drifted.");

                var anchorAfter = await store.ReadHistoryAnchorAsync();
                Require(anchorAfter.Sequence == transition.History.Sequence &&
                        anchorAfter.Digest.AsSpan().SequenceEqual(transition.History.RecordDigest),
                    "Detail decision + transition COMMIT did not advance one canonical history chain.");
                Require(await store.HistoryAnchorExistsAsync(decision.History.Sequence, decision.History.RecordDigest),
                    "Durable Detail decision history record is missing.");
                Require(await store.HistoryAnchorExistsAsync(transition.History.Sequence, transition.History.RecordDigest),
                    "Durable transition history record is missing after Detail decision.");

                var recoveryAfter = await store.ReadRecoveryHeadAsync();
                Require(recoveryAfter.FinalizedStep == 2 &&
                        recoveryAfter.ContinuityToken.AsSpan().SequenceEqual(transition.ResultingStateContinuityToken),
                    "Recovery head did not advance with the Detail decision transition COMMIT.");
                RequirePrefix(await store.ReadQa04OperationClosedPrefixAsync(), resultingPrefix);
            }

            await using (var reopened = await SqlitePersistenceStore.OpenOrCreateAsync(paths))
            {
                Require(await reopened.HistoryAnchorExistsAsync(decision.History.Sequence, decision.History.RecordDigest),
                    "Reopen lost the durable Detail decision history record.");
                Require(await reopened.HistoryAnchorExistsAsync(transition.History.Sequence, transition.History.RecordDigest),
                    "Reopen lost the transition record chained after the Detail decision.");
                var recovery = await reopened.ReadRecoveryHeadAsync();
                Require(recovery.FinalizedStep == 2 &&
                        recovery.ContinuityToken.AsSpan().SequenceEqual(transition.ResultingStateContinuityToken),
                    "Reopen lost recovery authority for the Detail decision transition.");
                RequirePrefix(await reopened.ReadQa04OperationClosedPrefixAsync(), resultingPrefix);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void RequirePrefix(
        Qa04OperationClosedPrefixV1? actual,
        Qa04OperationClosedPrefixV1 expected)
    {
        Require(actual is not null, "QA-04 operation prefix is missing.");
        Require(actual!.ProfileId == expected.ProfileId &&
                actual.FirstInjectionStep == expected.FirstInjectionStep &&
                actual.LastClosedInjectionStep == expected.LastClosedInjectionStep &&
                actual.TerminalOperationCount == expected.TerminalOperationCount &&
                actual.TerminalSemanticDigest.AsSpan().SequenceEqual(expected.TerminalSemanticDigest),
            "QA-04 operation prefix drifted across Detail decision persistence/reopen.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
