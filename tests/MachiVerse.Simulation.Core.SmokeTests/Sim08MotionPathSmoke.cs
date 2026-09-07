using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;

internal static class Sim08MotionPathSmoke
{
    internal static void Run()
    {
        VerifySemiImplicitEuler();
        VerifyAStarTieAndOptimality();
        VerifyHierarchicalComposition();
        VerifyNoRouteAndBudget();
        VerifyInputPermutation();
        Sim08CollisionSmoke.Run();
        Sim08SequentialImpulseSmoke.Run();
        Sim08StateRuntimeSmoke.RunAsync().GetAwaiter().GetResult();
    }

    private static void VerifySemiImplicitEuler()
    {
        var basis = new PhysicalKinematicStateV1(
            new PositionMmV1(10, -10, 0),
            new VelocityUmPerSecondV1(2_000, -2_000, 0));
        var result = SemiImplicitEulerV1.Integrate(
            basis,
            new AccelerationUmPerSecondSquaredV1(2_000, -2_000, 1_000),
            new StepRateV1(2, 1));

        Require(result.VelocityUmPerSecond == new VelocityUmPerSecondV1(3_000, -3_000, 500),
            "domain.physical.integration: semi-implicit velocity golden vector mismatch.");
        Require(result.PositionMm == new PositionMmV1(12, -12, 0),
            "domain.physical.integration: semi-implicit position golden vector mismatch.");
        Require(PhysicalIntegerMathV1.DivideRoundToEven(5, 2) == 2 &&
                PhysicalIntegerMathV1.DivideRoundToEven(7, 2) == 4 &&
                PhysicalIntegerMathV1.DivideRoundToEven(-5, 2) == -2 &&
                PhysicalIntegerMathV1.DivideRoundToEven(-7, 2) == -4,
            "domain.physical.integration: round-ties-to-even profile mismatch.");
    }

    private static void VerifyAStarTieAndOptimality()
    {
        var start = Id("00000000000000000000000000008100");
        var a = Id("00000000000000000000000000008101");
        var b = Id("00000000000000000000000000008102");
        var goal = Id("000000000000000000000000000081ff");

        var tieGraph = new DeterministicPathGraphV1([
            new PathEdgeV1(start, b, 1),
            new PathEdgeV1(b, goal, 1),
            new PathEdgeV1(start, a, 1),
            new PathEdgeV1(a, goal, 1),
        ]);
        var tie = DeterministicAStarV1.Find(tieGraph, start, goal, static _ => 0, 32);
        Require(tie.Status == AStarPathStatusV1.Found && tie.Nodes.SequenceEqual([start, a, goal]),
            "domain.path.astar.tie: equal-cost path must choose bytewise-smaller node id.");

        var optimalGraph = new DeterministicPathGraphV1([
            new PathEdgeV1(start, goal, 10),
            new PathEdgeV1(start, a, 2),
            new PathEdgeV1(a, goal, 2),
        ]);
        var optimal = DeterministicAStarV1.Find(optimalGraph, start, goal, static _ => 0, 32);
        Require(optimal.Status == AStarPathStatusV1.Found &&
                optimal.TotalCostMicrosecond == 4 &&
                optimal.Nodes.SequenceEqual([start, a, goal]),
            "domain.path.astar.optimal: shortest reference route mismatch.");
    }

    private static void VerifyHierarchicalComposition()
    {
        var start = Id("00000000000000000000000000008200");
        var portal = Id("00000000000000000000000000008201");
        var goal = Id("00000000000000000000000000008202");
        var first = new AStarPathResultV1(AStarPathStatusV1.Found, new[] { start, portal }, 7, 2);
        var second = new AStarPathResultV1(AStarPathStatusV1.Found, new[] { portal, goal }, 11, 3);
        var composed = DeterministicAStarV1.ComposeHierarchical([first, second]);

        Require(composed.Status == AStarPathStatusV1.Found &&
                composed.TotalCostMicrosecond == 18 &&
                composed.ExpandedNodeCount == 5 &&
                composed.Nodes.SequenceEqual([start, portal, goal]),
            "domain.path.hierarchical: local/regional composition mismatch.");
    }

    private static void VerifyNoRouteAndBudget()
    {
        var start = Id("00000000000000000000000000008300");
        var middle = Id("00000000000000000000000000008301");
        var goal = Id("00000000000000000000000000008302");

        var noRoute = DeterministicAStarV1.Find(
            new DeterministicPathGraphV1([new PathEdgeV1(start, middle, 1)]),
            start,
            goal,
            static _ => 0,
            32);
        Require(noRoute.Status == AStarPathStatusV1.NoRoute && noRoute.StableCode == "path.no-route",
            "domain.path.no-route: unreachable fixture must return stable no-route result.");

        var budget = DeterministicAStarV1.Find(
            new DeterministicPathGraphV1([
                new PathEdgeV1(start, middle, 1),
                new PathEdgeV1(middle, goal, 1),
            ]),
            start,
            goal,
            static _ => 0,
            1);
        Require(budget.Status == AStarPathStatusV1.BudgetExceeded && budget.StableCode == "path.budget-exceeded",
            "domain.path.budget: bounded search must expose a stable budget-exceeded result.");
    }

    private static void VerifyInputPermutation()
    {
        var start = Id("00000000000000000000000000008400");
        var a = Id("00000000000000000000000000008401");
        var b = Id("00000000000000000000000000008402");
        var goal = Id("000000000000000000000000000084ff");
        var edges = new[]
        {
            new PathEdgeV1(start, b, 3),
            new PathEdgeV1(b, goal, 3),
            new PathEdgeV1(start, a, 2),
            new PathEdgeV1(a, goal, 4),
        };

        var forward = DeterministicAStarV1.Find(new DeterministicPathGraphV1(edges), start, goal, static _ => 0, 32);
        var reverse = DeterministicAStarV1.Find(new DeterministicPathGraphV1(edges.Reverse()), start, goal, static _ => 0, 32);
        Require(forward.Status == reverse.Status &&
                forward.TotalCostMicrosecond == reverse.TotalCostMicrosecond &&
                forward.Nodes.SequenceEqual(reverse.Nodes),
            "SIM-08 path gate: edge input permutation changed semantic result.");
    }

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
