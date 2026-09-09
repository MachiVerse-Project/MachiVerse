using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04AuthoritativeStepLoopInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
        => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "machiverse-qa04-step-loop-" + Guid.NewGuid().ToString("N"));
        try
        {
            var materialized = Qa04ReferenceWorldMaterializerV1.MaterializeResidentIdentityLifecycle(128);
            var typedAuthorities = Qa04ReducedWorldTypedAuthorityV1.BindAll97(materialized);
            Require(typedAuthorities.CanonicalAuthorities.Count == StandardDomainPartitionRegistry.StandardPartitionCount,
                "Reduced authoritative loop must bind all 97 typed Domain roots before execution.");
            Require(typedAuthorities.CanonicalAuthorities.Sum(static value => checked((long)value.ActualItemCount)) == 128,
                "Reduced authoritative loop typed authority must contain only the 128 actual Resident records.");

            var result = await Qa04AuthoritativeStepLoopBridgeV1.RunReducedAsync(
                workerCount: 4,
                residentRecordCount: 128,
                persistenceRoot: root);
            Require(result.BasisStep == 0, "Reduced authoritative loop must start at Step 0.");
            Require(result.StepCount == Qa04AuthoritativeStepLoopBridgeV1.StandardReducedStepCount,
                "Reduced authoritative loop Step count drifted.");
            Require(result.FinalResultingStep == result.StepCount,
                "Reduced authoritative loop final Step mismatch.");
            Require(result.TotalDomainOutputCount == checked(result.StepCount * 8),
                "Reduced authoritative loop must execute all eight domains each Step.");
            Require(result.TotalPartitionCandidateCount == result.StepCount,
                "Reduced authoritative loop must carry one Resident partition candidate per Step.");
            Require(!result.CandidatePublishableBeforeCommitObserved &&
                    !result.PreparedStatePublishableBeforeCommitObserved,
                "Reduced authoritative loop observed premature publication.");
            Require(result.AllDurableReceiptsPublishable &&
                    result.AllResultingWorldStatesPublishable &&
                    result.AllStateChainsValid &&
                    result.SchedulerReopenedEachStep &&
                    result.RealSqliteCommitObservedThroughLoop,
                "Reduced authoritative loop authority proof is incomplete.");
            Require(result.FinalResidentPartitionRevision == checked(1UL + result.StepCount) &&
                    result.FinalResidentPartitionBasisStep == result.StepCount,
                "Reduced authoritative loop Resident header did not advance canonically.");
            Require(result.BasisStateDigest != result.FinalStateDigest,
                "Reduced authoritative loop must advance the WorldState digest.");
            Require(result.ReducedAuthoritativeStepLoopAvailable,
                "Reduced authoritative loop availability flag must be true.");
            Require(!result.ReferenceWorldMaterialized &&
                    !result.AuthoritativeStepLoopAvailable &&
                    !result.ReleaseEvidenceCapable,
                "Reduced authoritative loop must not claim full QA-04 completion.");
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
