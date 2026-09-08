using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.PhysicalBuilt;

public readonly record struct StepRateV1(ulong StepsNumerator, ulong SecondsDenominator)
{
    public void Validate()
    {
        if (StepsNumerator == 0) throw new ArgumentOutOfRangeException(nameof(StepsNumerator));
        if (SecondsDenominator == 0) throw new ArgumentOutOfRangeException(nameof(SecondsDenominator));
    }
}

public readonly record struct PositionMmV1(long X, long Y, long Z);
public readonly record struct VelocityUmPerSecondV1(long X, long Y, long Z);
public readonly record struct AccelerationUmPerSecondSquaredV1(long X, long Y, long Z);

public readonly record struct PhysicalKinematicStateV1(
    PositionMmV1 PositionMm,
    VelocityUmPerSecondV1 VelocityUmPerSecond);

public static class PhysicalIntegerMathV1
{
    public static long DivideRoundToEven(Int128 numerator, Int128 denominator)
    {
        if (denominator == 0) throw new DivideByZeroException();
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var quotient = numerator / denominator;
        var remainder = numerator % denominator;
        var absoluteRemainder = remainder < 0 ? -remainder : remainder;
        var twiceRemainder = absoluteRemainder * 2;
        if (twiceRemainder > denominator ||
            (twiceRemainder == denominator && (quotient & (Int128)1) != 0))
        {
            quotient += numerator < 0 ? -1 : 1;
        }

        if (quotient < long.MinValue || quotient > long.MaxValue)
            throw new OverflowException("simulation.numeric-overflow");
        return (long)quotient;
    }
}

public static class SemiImplicitEulerV1
{
    public static PhysicalKinematicStateV1 Integrate(
        PhysicalKinematicStateV1 basis,
        AccelerationUmPerSecondSquaredV1 acceleration,
        StepRateV1 rate)
    {
        rate.Validate();
        var velocity = new VelocityUmPerSecondV1(
            IntegrateVelocity(basis.VelocityUmPerSecond.X, acceleration.X, rate),
            IntegrateVelocity(basis.VelocityUmPerSecond.Y, acceleration.Y, rate),
            IntegrateVelocity(basis.VelocityUmPerSecond.Z, acceleration.Z, rate));
        var position = new PositionMmV1(
            IntegratePosition(basis.PositionMm.X, velocity.X, rate),
            IntegratePosition(basis.PositionMm.Y, velocity.Y, rate),
            IntegratePosition(basis.PositionMm.Z, velocity.Z, rate));
        return new PhysicalKinematicStateV1(position, velocity);
    }

    private static long IntegrateVelocity(long velocity, long acceleration, StepRateV1 rate)
    {
        var delta = PhysicalIntegerMathV1.DivideRoundToEven(
            (Int128)acceleration * rate.SecondsDenominator,
            rate.StepsNumerator);
        try
        {
            return checked(velocity + delta);
        }
        catch (OverflowException ex)
        {
            throw new OverflowException("simulation.numeric-overflow", ex);
        }
    }

    private static long IntegratePosition(long positionMm, long velocityUmPerSecond, StepRateV1 rate)
    {
        var denominator = checked((Int128)rate.StepsNumerator * 1000);
        var deltaMm = PhysicalIntegerMathV1.DivideRoundToEven(
            (Int128)velocityUmPerSecond * rate.SecondsDenominator,
            denominator);
        try
        {
            return checked(positionMm + deltaMm);
        }
        catch (OverflowException ex)
        {
            throw new OverflowException("simulation.numeric-overflow", ex);
        }
    }
}

public readonly record struct PathEdgeV1(OpaqueId128 From, OpaqueId128 To, ulong TravelCostMicrosecond)
{
    public void Validate()
    {
        if (From.IsZero || To.IsZero) throw new InvalidDataException("path.node-id-zero");
        if (From == To) throw new InvalidDataException("path.self-edge");
    }
}

public sealed class DeterministicPathGraphV1
{
    private readonly Dictionary<OpaqueId128, PathEdgeV1[]> _outgoing;

    public DeterministicPathGraphV1(IEnumerable<PathEdgeV1> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        var materialized = edges.ToArray();
        foreach (var edge in materialized) edge.Validate();

        if (materialized.GroupBy(static edge => (edge.From, edge.To)).Any(static group => group.Count() > 1))
            throw new InvalidDataException("path.duplicate-edge");

        _outgoing = materialized
            .GroupBy(static edge => edge.From)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static edge => edge.To)
                    .ThenBy(static edge => edge.TravelCostMicrosecond)
                    .ToArray());
    }

    public IReadOnlyList<PathEdgeV1> Outgoing(OpaqueId128 node)
        => _outgoing.TryGetValue(node, out var edges) ? edges : Array.Empty<PathEdgeV1>();
}

public enum AStarPathStatusV1 : byte
{
    Found = 1,
    NoRoute = 2,
    BudgetExceeded = 3,
}

public sealed record AStarPathResultV1(
    AStarPathStatusV1 Status,
    IReadOnlyList<OpaqueId128> Nodes,
    ulong TotalCostMicrosecond,
    uint ExpandedNodeCount)
{
    public string StableCode => Status switch
    {
        AStarPathStatusV1.Found => "path.found",
        AStarPathStatusV1.NoRoute => "path.no-route",
        AStarPathStatusV1.BudgetExceeded => "path.budget-exceeded",
        _ => throw new InvalidOperationException("Unknown path status."),
    };
}

public static class DeterministicAStarV1
{
    public static AStarPathResultV1 Find(
        DeterministicPathGraphV1 graph,
        OpaqueId128 start,
        OpaqueId128 goal,
        Func<OpaqueId128, ulong> heuristicMicrosecond,
        uint expansionBudget)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(heuristicMicrosecond);
        if (start.IsZero || goal.IsZero) throw new InvalidDataException("path.node-id-zero");
        if (expansionBudget == 0)
            return new AStarPathResultV1(AStarPathStatusV1.BudgetExceeded, Array.Empty<OpaqueId128>(), 0, 0);
        if (start == goal)
            return new AStarPathResultV1(AStarPathStatusV1.Found, Array.AsReadOnly(new[] { start }), 0, 0);

        var open = new SortedSet<AStarOpenEntryV1>(AStarOpenEntryComparerV1.Instance);
        var bestG = new Dictionary<OpaqueId128, ulong> { [start] = 0 };
        var parent = new Dictionary<OpaqueId128, OpaqueId128>();
        open.Add(new AStarOpenEntryV1(heuristicMicrosecond(start), 0, start));

        uint expanded = 0;
        while (open.Count > 0)
        {
            var current = open.Min;
            open.Remove(current);
            if (!bestG.TryGetValue(current.NodeId, out var knownG) || knownG != current.GCost)
                continue;

            if (current.NodeId == goal)
                return new AStarPathResultV1(
                    AStarPathStatusV1.Found,
                    Reconstruct(parent, start, goal),
                    current.GCost,
                    expanded);

            if (expanded >= expansionBudget)
                return new AStarPathResultV1(AStarPathStatusV1.BudgetExceeded, Array.Empty<OpaqueId128>(), 0, expanded);
            expanded++;

            foreach (var edge in graph.Outgoing(current.NodeId))
            {
                var tentativeG = CheckedAdd(current.GCost, edge.TravelCostMicrosecond);
                if (bestG.TryGetValue(edge.To, out var previousG) && tentativeG >= previousG)
                    continue;

                bestG[edge.To] = tentativeG;
                parent[edge.To] = current.NodeId;
                open.Add(new AStarOpenEntryV1(
                    CheckedAdd(tentativeG, heuristicMicrosecond(edge.To)),
                    tentativeG,
                    edge.To));
            }
        }

        return new AStarPathResultV1(AStarPathStatusV1.NoRoute, Array.Empty<OpaqueId128>(), 0, expanded);
    }

    public static AStarPathResultV1 ComposeHierarchical(IEnumerable<AStarPathResultV1> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var materialized = segments.ToArray();
        if (materialized.Length == 0)
            throw new ArgumentException("At least one hierarchical path segment is required.", nameof(segments));

        var nodes = new List<OpaqueId128>();
        ulong totalCost = 0;
        uint totalExpanded = 0;
        foreach (var segment in materialized)
        {
            if (segment.Status != AStarPathStatusV1.Found)
                return new AStarPathResultV1(
                    segment.Status,
                    Array.Empty<OpaqueId128>(),
                    0,
                    checked(totalExpanded + segment.ExpandedNodeCount));
            if (segment.Nodes.Count == 0)
                throw new InvalidDataException("path.found-empty-segment");
            if (nodes.Count > 0 && nodes[^1] != segment.Nodes[0])
                throw new InvalidDataException("path.hierarchical-disconnected-segment");

            nodes.AddRange(nodes.Count == 0 ? segment.Nodes : segment.Nodes.Skip(1));
            totalCost = CheckedAdd(totalCost, segment.TotalCostMicrosecond);
            totalExpanded = checked(totalExpanded + segment.ExpandedNodeCount);
        }

        return new AStarPathResultV1(
            AStarPathStatusV1.Found,
            Array.AsReadOnly(nodes.ToArray()),
            totalCost,
            totalExpanded);
    }

    private static IReadOnlyList<OpaqueId128> Reconstruct(
        IReadOnlyDictionary<OpaqueId128, OpaqueId128> parent,
        OpaqueId128 start,
        OpaqueId128 goal)
    {
        var reversed = new List<OpaqueId128> { goal };
        var current = goal;
        while (current != start)
        {
            if (!parent.TryGetValue(current, out var previous))
                throw new InvalidDataException("path.parent-chain-broken");
            reversed.Add(previous);
            current = previous;
        }
        reversed.Reverse();
        return Array.AsReadOnly(reversed.ToArray());
    }

    private static ulong CheckedAdd(ulong left, ulong right)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException ex)
        {
            throw new OverflowException("simulation.numeric-overflow", ex);
        }
    }

    private readonly record struct AStarOpenEntryV1(ulong FCost, ulong GCost, OpaqueId128 NodeId);

    private sealed class AStarOpenEntryComparerV1 : IComparer<AStarOpenEntryV1>
    {
        public static AStarOpenEntryComparerV1 Instance { get; } = new();

        public int Compare(AStarOpenEntryV1 x, AStarOpenEntryV1 y)
        {
            var comparison = x.FCost.CompareTo(y.FCost);
            if (comparison != 0) return comparison;
            comparison = x.GCost.CompareTo(y.GCost);
            if (comparison != 0) return comparison;
            return x.NodeId.CompareTo(y.NodeId);
        }
    }
}
