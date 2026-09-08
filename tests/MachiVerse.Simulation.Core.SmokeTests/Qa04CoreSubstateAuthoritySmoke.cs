using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04CoreSubstateAuthoritySmoke
{
    internal static async Task RunAsync()
    {
        VerifyFailClosedBindings();

        var root = Path.Combine(Path.GetTempPath(), "machiverse-qa04-core-substate-" + Guid.NewGuid().ToString("N"));
        try
        {
            var probe = await Qa04CoreSubstateAuthorityBridgeV1.RunTwoStepAsync(
                workerCount: 4,
                residentRecordCount: 32,
                persistenceRoot: root);

            Require(probe.BasisStep == 0 && probe.FirstResultingStep == 1 && probe.FinalResultingStep == 2,
                "QA-04 core-substate bridge must publish consecutive State 1 and State 2.");
            Require(probe.WorkerCount == 4 && probe.ResidentRecordCount == 32,
                "QA-04 core-substate bridge execution receipt mismatch.");
            Require(probe.FirstStepCoreSubstateCandidateCount == 2 && probe.SecondStepCoreSubstateCandidateCount == 2,
                "QA-04 core-substate bridge must bind scheduler and operation candidates on both Steps.");
            Require(probe.FirstStepFrozenOperationCount == 1 && probe.SecondStepFrozenOperationCount == 1,
                "QA-04 core-substate bridge must freeze exactly one scheduled Operation per Step.");
            Require(probe.FutureOperationCarriedToSecondStep,
                "QA-04 future scheduled Operation was not carried across State 0 -> State 1.");
            Require(probe.FirstStepSchedulerDigestMatchedDurableRuntime &&
                    probe.FirstStepOperationDigestMatchedSqlite &&
                    probe.SecondStepSchedulerDigestMatchedDurableRuntime &&
                    probe.SecondStepOperationDigestMatchedSqlite,
                "QA-04 published core-substate digests must match live scheduler and durable SQLite authority.");
            Require(probe.FirstOperationTerminalDurable && probe.SecondOperationTerminalDurable,
                "QA-04 scheduled Operations must become terminal durable at their effective Steps.");
            Require(probe.FirstStateChainValid && probe.SecondStateChainValid && probe.RealSqliteCommitObservedThroughStepTwo,
                "QA-04 two-Step state/continuity chain did not cross both SQLite COMMIT boundaries.");
            Require(probe.ReducedTwoStepAuthorityAvailable &&
                    !probe.ReferenceWorldMaterialized &&
                    !probe.AuthoritativeStepLoopAvailable &&
                    !probe.ReleaseEvidenceCapable,
                "QA-04 reduced two-Step proof must remain release-ineligible.");
            Require(new[]
                    {
                        probe.BasisStateDigest,
                        probe.FirstStateDigest,
                        probe.FinalStateDigest,
                        probe.FinalContinuityToken,
                    }
                    .All(digest => digest.Length == 64 && digest.Any(c => c != '0')),
                "QA-04 core-substate authority digests must be non-zero SHA-256 values.");
            Require(!string.Equals(probe.BasisStateDigest, probe.FirstStateDigest, StringComparison.Ordinal) &&
                    !string.Equals(probe.FirstStateDigest, probe.FinalStateDigest, StringComparison.Ordinal),
                "QA-04 consecutive authoritative State digests must advance.");
            Require(!probe.BlockingFailureCodes.Contains(
                    "qa04.target.core-substate-mutation-application-not-assembled",
                    StringComparer.Ordinal),
                "QA-04 two-Step proof must no longer report scheduler/operation core-substate application as missing.");
            Require(probe.BlockingFailureCodes.Contains(
                    "qa04.target.detail-substate-mutation-application-not-assembled",
                    StringComparer.Ordinal),
                "QA-04 two-Step proof must expose the remaining detail-substate mutation boundary.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        await Qa04DetailSubstateAuthoritySmoke.RunAsync();
    }

    private static void VerifyFailClosedBindings()
    {
        var state = CreateWorldState();
        var scheduler = new OperationSchedulerStateV1(
            nextSchedulableStep: 0,
            freezeStep: null,
            scheduled: Array.Empty<ScheduledOperationRefV1>());
        var frozen = StepInputFreezerV1.Freeze(state, scheduler);
        var schedulerCandidate = OperationSchedulerSubstateV1.CreatePostFinalizationCandidate(state, scheduler, frozen);
        var outputs = StandardDomainExecutionPlanV1.Create().Entries
            .Select(static entry => new DomainCandidateOutputV1(entry.DomainToken, basisStep: 0))
            .ToArray();

        RequireRejected(
            "step-candidate.duplicate-core-substate-candidate",
            () => StepCandidateV1.Build(
                OpaqueId128.Parse("00000000000000000000000000000031"),
                state,
                frozen,
                outputs,
                Array.Empty<ConflictGroupResolutionV1>(),
                coreSubstateCandidates: [schedulerCandidate, schedulerCandidate]),
            "StepCandidate must reject duplicate core-substate kinds.");

        var wrongBasis = new WorldSubstateRefV1(
            new SchemaRefV1("core.scheduler-state"),
            SHA256.HashData("wrong-scheduler-basis"u8));
        var wrongBasisCandidate = new StepCoreSubstateCandidateV1(
            StepCoreSubstateKindV1.Scheduler,
            basisStep: 0,
            wrongBasis,
            schedulerCandidate.ResultingState);
        RequireRejected(
            "step-core-substate.basis-state-mismatch",
            () => StepCandidateV1.Build(
                OpaqueId128.Parse("00000000000000000000000000000032"),
                state,
                frozen,
                outputs,
                Array.Empty<ConflictGroupResolutionV1>(),
                coreSubstateCandidates: [wrongBasisCandidate]),
            "StepCandidate must reject a core-substate candidate not bound to the basis WorldState digest.");

        var candidate = StepCandidateV1.Build(
            OpaqueId128.Parse("00000000000000000000000000000033"),
            state,
            frozen,
            outputs,
            Array.Empty<ConflictGroupResolutionV1>(),
            coreSubstateCandidates: [schedulerCandidate]);

        RequireRejected(
            "step-state.core-substate-material-coverage-mismatch",
            () => StepStateApplicationV1.Prepare(
                state,
                candidate,
                Array.Empty<StepPartitionStateMaterialV1>()),
            "State application must reject missing core-substate material.");

        var substitutedResult = new WorldSubstateRefV1(
            new SchemaRefV1("core.scheduler-state"),
            SHA256.HashData("substituted-scheduler-result"u8));
        RequireRejected(
            "step-core-substate.material-digest-mismatch",
            () => StepStateApplicationV1.Prepare(
                state,
                candidate,
                Array.Empty<StepPartitionStateMaterialV1>(),
                [new StepCoreSubstateStateMaterialV1(StepCoreSubstateKindV1.Scheduler, substitutedResult)]),
            "State application must reject a resulting core-substate digest substituted after candidate construction.");
    }

    private static WorldStateV1 CreateWorldState()
    {
        var partitions = StandardDomainPartitionRegistry.Entries.Select(identity => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                identity,
                revision: 1,
                basisStep: 0,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(identity.PartitionId.Value)))));
        return new WorldStateV1(
            new WorldStateHeaderV1(
                OpaqueId128.Parse("00000000000000000000000000000030"),
                step: 0,
                worldSeedDigest: SHA256.HashData("core-substate-smoke-seed"u8),
                configGeneration: 1,
                masterGeneration: 1,
                rateGeneration: 1),
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            SHA256.HashData("core-substate-smoke-config"u8));
    }

    private static void RequireRejected(string expectedCode, Action action, string message)
    {
        var rejected = false;
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expectedCode)
        {
            rejected = true;
        }
        Require(rejected, message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
