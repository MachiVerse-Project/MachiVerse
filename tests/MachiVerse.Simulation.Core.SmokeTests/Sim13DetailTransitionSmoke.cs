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
        VerifyFloors(policy);
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
            "detail.camera-independence: detail planner result must depend only on authoritative inputs.");

        var applied = directory.Apply(first.Selected, first.Deferred.Concat(first.NotYetEligible), transitionStep: 30);
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
        => new(
            regionId,
            domain,
            current,
            target,
            requiredStep,
            semanticPriority: 0,
            Id(trigger),
            triggerStep,
            records);

    private static string Key(DetailTransitionCandidateV1 candidate)
        => $"{candidate.RequiredEffectiveStep}:{candidate.SemanticPriority}:{candidate.DetailRegionId}:{candidate.DomainToken.Value}:{candidate.TriggerId}";

    private static OpaqueId128 Id(int value)
        => OpaqueId128.Parse(value.ToString("x32"));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
