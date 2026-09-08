using MachiVerse.Simulation.Core.Performance;

internal static class Qa04CoreSubstateAuthoritySmoke
{
    internal static async Task RunAsync()
    {
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
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
