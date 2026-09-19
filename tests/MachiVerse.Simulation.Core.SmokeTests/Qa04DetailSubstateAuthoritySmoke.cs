using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04DetailSubstateAuthoritySmoke
{
    internal static async Task RunAsync()
    {
        VerifyFailClosedDetailBinding();

        var root = Path.Combine(Path.GetTempPath(), "machiverse-qa04-detail-substate-" + Guid.NewGuid().ToString("N"));
        try
        {
            var probe = await Qa04DetailSubstateAuthorityBridgeV1.RunTwoStepAsync(
                workerCount: 4,
                residentRecordCount: 32,
                persistenceRoot: root);

            Require(probe.BasisStep == 0 && probe.FirstResultingStep == 1 && probe.FinalResultingStep == 2,
                "QA-04 detail-substate bridge must publish consecutive State 1 and State 2.");
            Require(probe.WorkerCount == 4 && probe.ResidentRecordCount == 32,
                "QA-04 detail-substate bridge execution receipt mismatch.");
            Require(probe.FirstStepCoreSubstateCandidateCount == 3 && probe.SecondStepCoreSubstateCandidateCount == 3,
                "QA-04 detail-substate bridge must bind scheduler, operation, and detail candidates on both Steps.");
            Require(probe.DetailPromotionApplied,
                "QA-04 detail-substate bridge must apply the authoritative D2 -> D0 promotion at Step 0.");
            Require(probe.FirstStepDetailDigestMatchedRuntime && probe.SecondStepDetailDigestMatchedRuntime,
                "QA-04 published detail-state digests must match the live DetailDirectory authority.");
            Require(probe.DetailLineageCarriedToSecondStep,
                "QA-04 promoted detail lineage must survive State 1 reuse as the Step 1 basis.");
            Require(probe.FirstStateChainValid && probe.SecondStateChainValid && probe.RealSqliteCommitObservedThroughStepTwo,
                "QA-04 detail-substate proof must cross both SQLite COMMIT boundaries with exact state chaining.");
            Require(probe.ReducedDetailAuthorityAvailable &&
                    !probe.ReferenceWorldMaterialized &&
                    !probe.AuthoritativeStepLoopAvailable &&
                    !probe.ReleaseEvidenceCapable,
                "QA-04 reduced detail proof must remain release-ineligible.");
            Require(probe.BasisDetailDigest.Length == 64 &&
                    probe.FirstDetailDigest.Length == 64 &&
                    probe.FinalDetailDigest.Length == 64 &&
                    probe.FinalContinuityToken.Length == 64,
                "QA-04 detail authority digests must be SHA-256 hex values.");
            Require(!string.Equals(probe.BasisDetailDigest, probe.FirstDetailDigest, StringComparison.Ordinal),
                "QA-04 Step 0 detail promotion must change core.detail-state digest.");
            Require(string.Equals(probe.FirstDetailDigest, probe.FinalDetailDigest, StringComparison.Ordinal),
                "QA-04 Step 1 with no detail transition must preserve the promoted detail-state digest.");
            Require(!probe.BlockingFailureCodes.Contains(
                    "qa04.target.detail-substate-mutation-application-not-assembled",
                    StringComparer.Ordinal),
                "QA-04 detail authority proof must no longer report detail substate application as missing.");
            Require(probe.BlockingFailureCodes.Contains(
                    "qa04.target.canonical-detail-workload-not-injected",
                    StringComparer.Ordinal),
                "QA-04 reduced detail proof must still expose the missing canonical detail workload.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void VerifyFailClosedDetailBinding()
    {
        var empty = new DetailDirectoryV1(Array.Empty<DetailRegionStateV1>());
        Require(DigestEquals(
                DetailDirectorySubstateV1.Canonicalize(empty),
                WorldStateV1.EmptySubstate("core.detail-state")),
            "Empty DetailDirectory must canonicalize to the standard empty core.detail-state ref.");

        var resident = new StableToken("resident");
        var region = new DetailRegionStateV1(
            OpaqueId128.Parse("00000000000000000000000000000051"),
            OpaqueId128.Parse("00000000000000000000000000000052"),
            [new KeyValuePair<StableToken, DetailLevelV1>(resident, DetailLevelV1.D2RegionalAggregate)],
            lineageGeneration: 1,
            lastTransitionStep: 0);
        var directory = new DetailDirectoryV1([region]);
        var state = CreateWorldState(WorldStateV1.EmptySubstate("core.detail-state"));
        var policy = new DetailTransitionPolicyV1(
            0, 0, 0,
            DetailLevelV1.D0Entity,
            DetailLevelV1.D0Entity,
            1, 100, 1, 100);
        var plan = DetailTransitionPlannerV1.Plan(directory, Array.Empty<DetailTransitionCandidateV1>(), 0, policy);
        var conservation = DetailConservationInvariantV1.ValidateForTransitions(
            plan.Selected,
            new DetailConservationSnapshotV1(),
            new DetailConservationSnapshotV1());

        var rejected = false;
        try
        {
            _ = DetailDirectorySubstateV1.CreatePostTransitionCandidate(state, directory, plan, conservation);
        }
        catch (InvalidDataException ex) when (ex.Message == "detail-substate.basis-directory-mismatch")
        {
            rejected = true;
        }
        Require(rejected,
            "Detail substate projection must reject a live directory that is not the exact State(S) detail authority.");
    }

    private static WorldStateV1 CreateWorldState(WorldSubstateRefV1 detailState)
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
                OpaqueId128.Parse("00000000000000000000000000000050"),
                step: 0,
                worldSeedDigest: SHA256.HashData("detail-substate-smoke-seed"u8),
                configGeneration: 1,
                masterGeneration: 1,
                rateGeneration: 1),
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            detailState,
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            SHA256.HashData("detail-substate-smoke-config"u8));
    }

    private static bool DigestEquals(WorldSubstateRefV1 left, WorldSubstateRefV1 right)
        => left.Schema == right.Schema && left.CanonicalDigest.SequenceEqual(right.CanonicalDigest);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
