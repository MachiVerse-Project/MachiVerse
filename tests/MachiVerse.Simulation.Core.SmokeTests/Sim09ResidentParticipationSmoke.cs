using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.ResidentParticipation;

internal static class Sim09ResidentParticipationSmoke
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Run()
    {
        VerifyGoalSelection();
        VerifyBoundedGoap();
        VerifyFallback();
        VerifyParticipationBinding();
        VerifyControlAvailabilityAndDeath();
    }

    private static void VerifyGoalSelection()
    {
        var a = new ResidentGoalCandidateV1(Id("0000000000000000000000000000a001"), 100, 0);
        var b = new ResidentGoalCandidateV1(Id("0000000000000000000000000000a002"), 100, -1);
        var c = new ResidentGoalCandidateV1(Id("0000000000000000000000000000a003"), 99, -100);

        var forward = DeterministicResidentGoalSelectorV1.Select([a, b, c]);
        var reverse = DeterministicResidentGoalSelectorV1.Select(new[] { a, b, c }.Reverse());
        Require(forward.GoalId == b.GoalId && reverse.GoalId == b.GoalId,
            "domain.resident.goal-selection: utility/priority/id canonical order mismatch.");
    }

    private static void VerifyBoundedGoap()
    {
        var start = Digest("start");
        var viaA = Digest("via-a");
        var viaB = Digest("via-b");
        var goal = Digest("goal");
        var graph = new ResidentGoapGraphV1([
            new ResidentGoapTransitionV1(start, viaB, new StableToken("resident.action.b"), 1),
            new ResidentGoapTransitionV1(viaB, goal, new StableToken("resident.action.finish"), 1),
            new ResidentGoapTransitionV1(start, viaA, new StableToken("resident.action.a"), 1),
            new ResidentGoapTransitionV1(viaA, goal, new StableToken("resident.action.finish"), 1),
        ]);
        var fallback = new ResidentFallbackBehaviorRegistryV1([
            new StableToken("resident.fallback.safe"),
            new StableToken("resident.fallback.routine"),
        ]);

        var result = DeterministicResidentGoapPlannerV1.Find(
            graph, start, goal, static _ => 0, fallback);
        Require(result.Status == ResidentPlanStatusV1.Found &&
                result.TotalCost == 2 &&
                result.Actions.Select(static token => token.Value).SequenceEqual([
                    "resident.action.a", "resident.action.finish"
                ]),
            "domain.resident.goap: equal-cost planning tie did not choose action token canonical order.");
        Require(result.ExpandedNodeCount <= DeterministicResidentGoapPlannerV1.StandardMaxExpandedNodes,
            "domain.resident.goap: expansion count exceeded standard bound.");

        var permuted = new ResidentGoapGraphV1(new[]
        {
            new ResidentGoapTransitionV1(start, viaA, new StableToken("resident.action.a"), 1),
            new ResidentGoapTransitionV1(viaA, goal, new StableToken("resident.action.finish"), 1),
            new ResidentGoapTransitionV1(start, viaB, new StableToken("resident.action.b"), 1),
            new ResidentGoapTransitionV1(viaB, goal, new StableToken("resident.action.finish"), 1),
        }.Reverse());
        var second = DeterministicResidentGoapPlannerV1.Find(
            permuted, start, goal, static _ => 0, fallback);
        Require(result.Actions.SequenceEqual(second.Actions) && result.TotalCost == second.TotalCost,
            "domain.resident.goap: transition input permutation changed semantic plan.");
    }

    private static void VerifyFallback()
    {
        var start = Digest("fallback-start");
        var unreachable = Digest("fallback-goal");
        var graph = new ResidentGoapGraphV1(Array.Empty<ResidentGoapTransitionV1>());
        var fallback = new ResidentFallbackBehaviorRegistryV1([
            new StableToken("resident.fallback.z"),
            new StableToken("resident.fallback.a"),
        ]);
        var result = DeterministicResidentGoapPlannerV1.Find(
            graph, start, unreachable, static _ => 0, fallback);
        Require(result.Status == ResidentPlanStatusV1.NoPlan &&
                result.FallbackBehavior?.Value == "resident.fallback.a",
            "domain.resident.goap-fallback: fallback registry selection must be deterministic.");

        var middle = Digest("budget-middle");
        var budgetGraph = new ResidentGoapGraphV1([
            new ResidentGoapTransitionV1(start, middle, new StableToken("resident.action.step"), 1),
            new ResidentGoapTransitionV1(middle, unreachable, new StableToken("resident.action.finish"), 1),
        ]);
        var budget = DeterministicResidentGoapPlannerV1.Find(
            budgetGraph, start, unreachable, static _ => 0, fallback, expansionBudget: 1);
        Require(budget.Status == ResidentPlanStatusV1.BudgetExceeded && budget.FallbackBehavior is not null,
            "domain.resident.goap-budget: bounded planner must expose deterministic fallback on budget exhaustion.");
    }

    private static void VerifyParticipationBinding()
    {
        var diver = Id("0000000000000000000000000000b001");
        var resident = Id("0000000000000000000000000000b002");
        var binding = new ParticipationBindingV1(
            Id("0000000000000000000000000000b003"),
            diver,
            resident,
            ParticipationBindingStatusV1.Active,
            1,
            10,
            null);
        var authority = new ParticipationBindingAuthorityV1([binding]);

        RequireReject(
            () => authority.ValidateCreate(new ParticipationBindingV1(
                Id("0000000000000000000000000000b004"),
                diver,
                Id("0000000000000000000000000000b005"),
                ParticipationBindingStatusV1.Active,
                2,
                11,
                null)),
            "participation.diver-active-binding-duplicate");
        RequireReject(
            () => authority.ValidateCreate(new ParticipationBindingV1(
                Id("0000000000000000000000000000b006"),
                Id("0000000000000000000000000000b007"),
                resident,
                ParticipationBindingStatusV1.Active,
                1,
                11,
                null)),
            "participation.resident-active-binding-duplicate");
        RequireReject(
            () => authority.Release(binding.BindingId, expectedGeneration: 2, effectiveStep: 12),
            "participation.binding-generation-stale");
    }

    private static void VerifyControlAvailabilityAndDeath()
    {
        var binding = new ParticipationBindingV1(
            Id("0000000000000000000000000000c001"),
            Id("0000000000000000000000000000c002"),
            Id("0000000000000000000000000000c003"),
            ParticipationBindingStatusV1.Active,
            7,
            20,
            null);
        var authority = new ParticipationBindingAuthorityV1([binding]);
        var policy = Id("0000000000000000000000000000c004");

        var available = authority.ProjectControlContext(
            binding.ResidentId, ParticipationControlAvailabilityV1.Available, policy, 21);
        var unavailable = authority.ProjectControlContext(
            binding.ResidentId, ParticipationControlAvailabilityV1.Unavailable, policy, 22);
        Require(available.ControlMode == ResidentControlModeV1.DiverControlAvailable &&
                unavailable.ControlMode == ResidentControlModeV1.DiverAbsentPolicy &&
                unavailable.BindingId == binding.BindingId &&
                authority.BindingsCanonical.Single().Status == ParticipationBindingStatusV1.Active,
            "domain.participation.disconnect: control unavailability must not release or reassign the active binding.");

        var deceased = authority.MarkResidentDeceased(binding.BindingId, 7, 23);
        var terminal = authority.ProjectTerminalControlContext(deceased, 23);
        Require(deceased.Status == ParticipationBindingStatusV1.ResidentDeceased &&
                deceased.ResidentId == binding.ResidentId &&
                terminal.ControlMode == ResidentControlModeV1.BoundResidentDeceased,
            "domain.participation.resident-death: resident death must stop control without changing resident identity.");
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);
    private static byte[] Digest(string value) => SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(value));

    private static void RequireReject(Action action, string expected)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expected)
        {
            return;
        }
        throw new InvalidOperationException($"Expected SIM-09 rejection: {expected}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
