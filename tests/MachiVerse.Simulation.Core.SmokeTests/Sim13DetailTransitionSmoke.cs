using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim13DetailTransitionSmoke
{
    internal static void Run()
    {
        var config = new CoreConfigCoordinator().LoadStartup("""
[meta]
format = "machiverse-config"
schema_version = "1.0"
component = "simulation-core"
""");
        var policy = DetailTransitionPolicyV1.FromConfig(config);

        Require(policy.PromotionHysteresisSteps == 30 &&
                policy.DemotionQuietSteps == 300 &&
                policy.MinimumResidenceSteps == 300 &&
                policy.PromotionMaxRegionsPerStep == 4 &&
                policy.PromotionMaxRecordsPerStep == 20000 &&
                policy.DemotionMaxRegionsPerStep == 8 &&
                policy.DemotionMaxRecordsPerStep == 50000,
            "SIM-13 detail policy must match Phase 4 defaults.");

        VerifyHysteresis(policy);
        VerifyBudgetAndPermutation(policy);
        VerifyBudgetWorkerCountDeterminismAsync(policy).GetAwaiter().GetResult();
        VerifyOversizedTransitionRejected(policy);
        VerifyFloors(policy);
        VerifyTriggerSourceGate();
        VerifyConservationAuthorityGate(policy);
        VerifyPlannerPlanBinding(policy);
        VerifyApplyAndCameraIndependence(policy);
        Sim13DetailConservationSmoke.Run();
    }

    private static void VerifyHysteresis(DetailTransitionPolicyV1 policy)
    {
        var resident = new StableToken("resident");
        var region = Region(1, resident, DetailLevelV1.D2RegionalAggregate, lastTransitionStep: 0);
        var directory = new DetailDirectoryV1([region]);
        var promotion = Transition(
            region.DetailRegionId,
            resident,
            DetailLevelV1.D2RegionalAggregate,
            DetailLevelV1.D0Entity,
            requiredStep: 100,
            triggerStep: 100,
            trigger: 1001,
            records: 10);

        var early = DetailTransitionPlannerV1.Plan(directory, [promotion], 129, policy);
        var eligible = DetailTransitionPlannerV1.Plan(directory, [promotion], 130, policy);
        Require(early.Selected.Count == 0 && early.NotYetEligible.Count == 1,
            "detail.hysteresis: promotion must wait 30 authoritative Steps.");
        Require(eligible.Selected.Count == 1,
            "detail.hysteresis: promotion must become eligible at the exact Step threshold.");

        var recentRegion = Region(2, resident, DetailLevelV1.D0Entity, lastTransitionStep: 200);
        var recentDirectory = new DetailDirectoryV1([recentRegion]);
        var demotion = Transition(
            recentRegion.DetailRegionId,
            resident,
            DetailLevelV1.D0Entity,
            DetailLevelV1.D2RegionalAggregate,
            requiredStep: 201,
            triggerStep: 100,
            trigger: 1002,
            records: 10);
        var tooSoon = DetailTransitionPlannerV1.Plan(recentDirectory, [demotion], 499, policy);
        var demotionEligible = DetailTransitionPlannerV1.Plan(recentDirectory, [demotion], 500, policy);
        Require(tooSoon.Selected.Count == 0,
            "detail.hysteresis: demotion must satisfy quiet and minimum-residence Steps.");
        Require(demotionEligible.Selected.Count == 1,
            "detail.hysteresis: demotion must become eligible from Step authority only.");
    }

    private static void VerifyBudgetAndPermutation(DetailTransitionPolicyV1 policy)
    {
        var resident = new StableToken("resident");
        var regions = Enumerable.Range(10, 6)
            .Select(index => Region(index, resident, DetailLevelV1.D3BoundarySummary, lastTransitionStep: 0))
            .ToArray();
        var directory = new DetailDirectoryV1(regions);
        var requests = regions
            .Select((region, index) => Transition(
                region.DetailRegionId,
                resident,
                DetailLevelV1.D3BoundarySummary,
                DetailLevelV1.D0Entity,
                requiredStep: 50,
                triggerStep: 0,
                trigger: 2000 + index,
                records: 4000))
            .ToArray();

        var forward = DetailTransitionPlannerV1.Plan(directory, requests, 100, policy);
        var reverse = DetailTransitionPlannerV1.Plan(directory, requests.Reverse(), 100, policy);
        Require(forward.Selected.Count == 4 && forward.Deferred.Count == 2 && forward.HasMaterializationPending,
            "detail.budget.defer-order: default promotion region budget must select 4 and defer 2.");
        Require(forward.Selected.Select(Key).SequenceEqual(reverse.Selected.Select(Key)) &&
                forward.Deferred.Select(Key).SequenceEqual(reverse.Deferred.Select(Key)),
            "detail.budget.defer-order: request permutation must not alter selection/defer order.");

        var expected = requests
            .OrderBy(static request => request.DetailRegionId)
            .Take(4)
            .Select(Key)
            .ToArray();
        Require(forward.Selected.Select(Key).SequenceEqual(expected),
            "detail.budget.defer-order: canonical queue key must choose the same regions before budget exhaustion.");
    }

    private static async Task VerifyBudgetWorkerCountDeterminismAsync(DetailTransitionPolicyV1 policy)
    {
        var resident = new StableToken("resident");
        var regions = Enumerable.Range(50, 6)
            .Select(index => Region(index, resident, DetailLevelV1.D3BoundarySummary, lastTransitionStep: 0))
            .ToArray();
        var directory = new DetailDirectoryV1(regions);
        var requests = regions
            .Select((region, index) => Transition(
                region.DetailRegionId,
                resident,
                DetailLevelV1.D3BoundarySummary,
                DetailLevelV1.D0Entity,
                requiredStep: 50,
                triggerStep: 0,
                trigger: 5000 + index,
                records: 4000))
            .ToArray();

        string[]? baseline = null;
        foreach (var workerCount in new[] { 1, 4, 8, 16 })
        {
            foreach (var input in new[] { requests, requests.Reverse().ToArray() })
            {
                var generated = await DeterministicBatchExecutor.RunAsync(
                    input,
                    workerCount,
                    (request, _) => ValueTask.FromResult(request));
                var plan = DetailTransitionPlannerV1.Plan(directory, generated, 100, policy);
                var canonical = plan.Selected.Select(candidate => "S:" + Key(candidate))
                    .Concat(plan.Deferred.Select(candidate => "D:" + Key(candidate)))
                    .Concat(plan.NotYetEligible.Select(candidate => "N:" + Key(candidate)))
                    .ToArray();

                baseline ??= canonical;
                Require(canonical.SequenceEqual(baseline),
                    $"detail.budget.defer-order: worker/input permutation changed plan at worker-count={workerCount}.");
            }
        }
    }

    private static void VerifyOversizedTransitionRejected(DetailTransitionPolicyV1 policy)
    {
        var resident = new StableToken("resident");
        var region = Region(25, resident, DetailLevelV1.D3BoundarySummary, lastTransitionStep: 0);
        var directory = new DetailDirectoryV1([region]);
        var oversized = Transition(
            region.DetailRegionId,
            resident,
            DetailLevelV1.D3BoundarySummary,
            DetailLevelV1.D0Entity,
            requiredStep: 0,
            triggerStep: 0,
            trigger: 2501,
            records: checked((uint)policy.PromotionMaxRecordsPerStep + 1));

        var rejected = false;
        try
        {
            _ = DetailTransitionPlannerV1.Plan(directory, [oversized], 30, policy);
        }
        catch (InvalidDataException ex) when (ex.Message == "detail.transition-estimate-exceeds-step-budget")
        {
            rejected = true;
        }
        Require(rejected,
            "detail.budget.progress: a transition that can never fit the configured per-Step budget must fail explicitly instead of deferring forever.");
    }

    private static void VerifyFloors(DetailTransitionPolicyV1 policy)
    {
        var resident = new StableToken("resident");
        var bound = Region(
            30,
            resident,
            DetailLevelV1.D0Entity,
            lastTransitionStep: 0,
            [DetailTransitionGuardV1.BoundResident]);
        var activeTransaction = Region(
            31,
            resident,
            DetailLevelV1.D0Entity,
            lastTransitionStep: 0,
            [DetailTransitionGuardV1.ActiveTransaction]);
        var directory = new DetailDirectoryV1([bound, activeTransaction]);
        var requests = new[]
        {
            Transition(bound.DetailRegionId, resident, DetailLevelV1.D0Entity, DetailLevelV1.D1LocalAggregate, 0, 0, 3001, 10),
            Transition(activeTransaction.DetailRegionId, resident, DetailLevelV1.D0Entity, DetailLevelV1.D1LocalAggregate, 0, 0, 3002, 10),
        };
        var plan = DetailTransitionPlannerV1.Plan(directory, requests, 1000, policy);

        Require(plan.Selected.Count == 0 && plan.NotYetEligible.Count == 2,
            "detail.bound-resident-floor/detail.active-transaction-floor: D0 guards must prevent demotion.");
    }

    private static void VerifyTriggerSourceGate()
    {
        var invalidEnumRejected = false;
        try
        {
            _ = new DetailTransitionRequestV1(
                Id(39),
                new StableToken("resident"),
                DetailLevelV1.D2RegionalAggregate,
                DetailLevelV1.D0Entity,
                requiredEffectiveStep: 0,
                semanticPriority: 0,
                (DetailTransitionTriggerSourceV1)255,
                Id(3901),
                triggerObservedStep: 0,
                estimatedRecordCount: 1);
        }
        catch (ArgumentOutOfRangeException)
        {
            invalidEnumRejected = true;
        }
        Require(invalidEnumRejected,
            "detail.camera-independence: unregistered trigger source must be rejected.");

        var triggerId = Id(3902);
        var authority = ScheduledOperationAuthority(triggerId, 0);
        var forged = new DetailTransitionRequestV1(
            Id(38),
            new StableToken("resident"),
            DetailLevelV1.D2RegionalAggregate,
            DetailLevelV1.D0Entity,
            requiredEffectiveStep: 0,
            semanticPriority: 0,
            DetailTransitionTriggerSourceV1.MutationIntent,
            triggerId,
            triggerObservedStep: 0,
            estimatedRecordCount: 1);
        var forgedRejected = false;
        try
        {
            _ = DetailTransitionAdmissionV1.Admit(forged, authority);
        }
        catch (InvalidDataException ex) when (ex.Message == "detail.transition-trigger-not-authoritative")
        {
            forgedRejected = true;
        }
        Require(forgedRejected,
            "detail.camera-independence: a caller cannot relabel an arbitrary trigger ID as an authoritative source.");
    }

    private static void VerifyConservationAuthorityGate(DetailTransitionPolicyV1 policy)
    {
        var resident = new StableToken("resident");
        var region = Region(41, resident, DetailLevelV1.D2RegionalAggregate, lastTransitionStep: 0);
        var directory = new DetailDirectoryV1([region]);
        var request = Transition(
            region.DetailRegionId,
            resident,
            DetailLevelV1.D2RegionalAggregate,
            DetailLevelV1.D0Entity,
            requiredStep: 0,
            triggerStep: 0,
            trigger: 4101,
            records: 10);
        var plan = DetailTransitionPlannerV1.Plan(directory, [request], 30, policy);
        var before = new DetailConservationSnapshotV1(
            stockTotals: [new ConservedQuantityV1(new StableToken("material.test.g"), 10)]);
        var afterLostStock = new DetailConservationSnapshotV1(
            stockTotals: [new ConservedQuantityV1(new StableToken("material.test.g"), 9)]);
        var failedValidation = DetailConservationInvariantV1.ValidateForTransitions(
            plan.Selected,
            before,
            afterLostStock);

        var blocked = false;
        try
        {
            _ = directory.Apply(plan, failedValidation);
        }
        catch (InvalidDataException ex) when (ex.Message == "detail.conservation-blocked")
        {
            blocked = true;
        }
        Require(blocked,
            "detail.stock-conservation: failed conservation proof must block authority-changing detail Apply.");

        var mismatchedValidation = DetailConservationInvariantV1.ValidateForTransitions([], before, before);
        var mismatchRejected = false;
        try
        {
            _ = directory.Apply(plan, mismatchedValidation);
        }
        catch (InvalidDataException ex) when (ex.Message == "detail.conservation-transition-mismatch")
        {
            mismatchRejected = true;
        }
        Require(mismatchRejected,
            "detail conservation proof must be bound to the exact canonical selected transition set.");
    }

    private static void VerifyPlannerPlanBinding(DetailTransitionPolicyV1 policy)
    {
        var resident = new StableToken("resident");
        var region = Region(42, resident, DetailLevelV1.D2RegionalAggregate, lastTransitionStep: 0);
        var sourceDirectory = new DetailDirectoryV1([region]);
        var transition = Transition(
            region.DetailRegionId,
            resident,
            DetailLevelV1.D2RegionalAggregate,
            DetailLevelV1.D0Entity,
            0,
            0,
            4201,
            10);
        var plan = DetailTransitionPlannerV1.Plan(sourceDirectory, [transition], 30, policy);
        var validation = DetailConservationInvariantV1.ValidateForTransitions(
            plan.Selected,
            new DetailConservationSnapshotV1(),
            new DetailConservationSnapshotV1());

        var alteredRegion = Region(
            42,
            resident,
            DetailLevelV1.D2RegionalAggregate,
            lastTransitionStep: 0,
            [DetailTransitionGuardV1.BoundResident]);
        var alteredDirectory = new DetailDirectoryV1([alteredRegion]);
        var rejected = false;
        try
        {
            _ = alteredDirectory.Apply(plan, validation);
        }
        catch (InvalidDataException ex) when (ex.Message == "detail.transition-plan-directory-mismatch")
        {
            rejected = true;
        }
        Require(rejected,
            "detail planner-issued plan must be bound to the exact directory authority used for floor/hysteresis/budget validation.");
    }

    private static void VerifyApplyAndCameraIndependence(DetailTransitionPolicyV1 policy)
    {
        var resident = new StableToken("resident");
        var region = Region(40, resident, DetailLevelV1.D2RegionalAggregate, lastTransitionStep: 0);
        var directory = new DetailDirectoryV1([region]);
        var request = Transition(
            region.DetailRegionId,
            resident,
            DetailLevelV1.D2RegionalAggregate,
            DetailLevelV1.D0Entity,
            requiredStep: 10,
            triggerStep: 0,
            trigger: 4001,
            records: 100);

        var first = DetailTransitionPlannerV1.Plan(directory, [request], 30, policy);
        var second = DetailTransitionPlannerV1.Plan(directory, [request], 30, policy);
        Require(first.Selected.Select(Key).SequenceEqual(second.Selected.Select(Key)),
            "detail.camera-independence: detail planner result must depend only on admitted authoritative inputs.");

        var validation = DetailConservationInvariantV1.ValidateForTransitions(
            first.Selected,
            new DetailConservationSnapshotV1(),
            new DetailConservationSnapshotV1());
        var applied = directory.Apply(first, validation);
        var next = applied.GetRegion(region.DetailRegionId);
        Require(next.GetLevel(resident) == DetailLevelV1.D0Entity &&
                next.LineageGeneration == region.LineageGeneration + 1 &&
                next.LastTransitionStep == 30,
            "detail.materialization-repeatability: applying the same selected transition must produce canonical detail metadata.");
    }

    private static DetailRegionStateV1 Region(
        int suffix,
        StableToken domain,
        DetailLevelV1 level,
        ulong lastTransitionStep,
        IEnumerable<StableToken>? guards = null)
        => new(
            Id(suffix),
            Id(10000 + suffix),
            [new KeyValuePair<StableToken, DetailLevelV1>(domain, level)],
            lineageGeneration: 1,
            lastTransitionStep,
            guards);

    private static DetailTransitionCandidateV1 Transition(
        OpaqueId128 regionId,
        StableToken domain,
        DetailLevelV1 current,
        DetailLevelV1 target,
        ulong requiredStep,
        ulong triggerStep,
        int trigger,
        uint records)
    {
        var triggerId = Id(trigger);
        var request = new DetailTransitionRequestV1(
            regionId,
            domain,
            current,
            target,
            requiredStep,
            semanticPriority: 0,
            DetailTransitionTriggerSourceV1.ScheduledOperation,
            triggerId,
            triggerStep,
            records);
        return DetailTransitionAdmissionV1.Admit(request, ScheduledOperationAuthority(triggerId, triggerStep));
    }

    private static DetailTransitionTriggerAuthorityV1 ScheduledOperationAuthority(OpaqueId128 triggerId, ulong triggerStep)
    {
        var orderKey = new SameStepOrderKey(
            phase: 0,
            domainRank: 0,
            conflictScopeDigest: new byte[32],
            semanticPriority: 0,
            intentId: triggerId);
        var scheduled = new ScheduledOperationRefV1(triggerId, triggerStep, orderKey);
        var frozen = new FrozenStepInputV1(
            Id(900000),
            triggerStep,
            configGeneration: 1,
            configDigest: new byte[32],
            [scheduled]);
        return DetailTransitionTriggerAuthorityV1.FromStep(frozen);
    }

    private static string Key(DetailTransitionCandidateV1 candidate)
        => $"{candidate.RequiredEffectiveStep}:{candidate.SemanticPriority}:{candidate.DetailRegionId}:{candidate.DomainToken.Value}:{candidate.TriggerSource}:{candidate.TriggerId}";

    private static OpaqueId128 Id(int value)
        => OpaqueId128.Parse(value.ToString("x32"));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
