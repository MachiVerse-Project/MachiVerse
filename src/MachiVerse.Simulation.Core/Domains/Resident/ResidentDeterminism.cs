using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.Resident;

public enum ResidentLifecycleKindV1 : byte
{
    Developing = 1,
    Alive = 2,
    Deceased = 3,
}

public sealed record ResidentLifecycleStateV1(
    OpaqueId128 ResidentId,
    ResidentLifecycleKindV1 State,
    ulong? BirthStep,
    ulong? DeathStep)
{
    public void Validate()
    {
        if (ResidentId.IsZero) throw new InvalidDataException("resident.lifecycle-id-zero");
        if (!Enum.IsDefined(State)) throw new InvalidDataException("resident.lifecycle-state-invalid");
        if (BirthStep is not null && DeathStep is not null && DeathStep.Value < BirthStep.Value)
            throw new InvalidDataException("resident.lifecycle-death-before-birth");
        if (State == ResidentLifecycleKindV1.Deceased && DeathStep is null)
            throw new InvalidDataException("resident.lifecycle-death-step-required");
        if (State != ResidentLifecycleKindV1.Deceased && DeathStep is not null)
            throw new InvalidDataException("resident.lifecycle-live-has-death-step");
    }

    public ResidentLifecycleStateV1 TransitionTo(ResidentLifecycleKindV1 next, ulong effectiveStep)
    {
        Validate();
        if (!Enum.IsDefined(next)) throw new InvalidDataException("resident.lifecycle-state-invalid");
        if (next == State) return this;
        var allowed = (State, next) switch
        {
            (ResidentLifecycleKindV1.Developing, ResidentLifecycleKindV1.Alive) => true,
            (ResidentLifecycleKindV1.Alive, ResidentLifecycleKindV1.Deceased) => true,
            _ => false,
        };
        if (!allowed) throw new InvalidDataException("resident.lifecycle-transition-invalid");
        if (BirthStep is not null && effectiveStep < BirthStep.Value)
            throw new InvalidDataException("resident.lifecycle-transition-before-birth");
        return next == ResidentLifecycleKindV1.Deceased
            ? this with { State = next, DeathStep = effectiveStep }
            : this with { State = next };
    }
}

public readonly record struct ResidentPpmV1(uint Value)
{
    public const uint Max = 1_000_000;
    public static ResidentPpmV1 Create(long value)
    {
        if (value < 0 || value > Max) throw new InvalidDataException("resident.ppm-out-of-range");
        return new ResidentPpmV1((uint)value);
    }
}

public sealed record ResidentHealthStateV1(
    ResidentPpmV1 HealthCapacity,
    ResidentPpmV1 Pain,
    ResidentPpmV1 Stress,
    ResidentPpmV1 Fatigue)
{
    public ResidentHealthStateV1 ApplyDelta(long healthCapacityDelta, long painDelta, long stressDelta, long fatigueDelta)
        => new(
            ResidentPpmV1.Create(checked((long)HealthCapacity.Value + healthCapacityDelta)),
            ResidentPpmV1.Create(checked((long)Pain.Value + painDelta)),
            ResidentPpmV1.Create(checked((long)Stress.Value + stressDelta)),
            ResidentPpmV1.Create(checked((long)Fatigue.Value + fatigueDelta)));
}

public static class ResidentConditionRandomV1
{
    private static readonly StableToken ResidentDomain = new("resident");

    public static bool Occurs(WorldSeed256 worldSeed, OpaqueId128 worldId, ulong step, OpaqueId128 residentId, OpaqueId128 conditionId, StableToken eventKind, uint probabilityPpm)
    {
        if (worldId.IsZero || residentId.IsZero || conditionId.IsZero)
            throw new InvalidDataException("resident.condition-random-id-zero");
        if (probabilityPpm > ResidentPpmV1.Max)
            throw new InvalidDataException("resident.condition-random-probability-range");
        if (probabilityPpm == 0) return false;
        if (probabilityPpm == ResidentPpmV1.Max) return true;
        var context = new RandomContextV1(worldId, step, ResidentDomain, eventKind, residentId, conditionId, OpaqueId128.Zero, 0);
        return DeterministicRandom.BoundedUInt64(worldSeed, context, 0, ResidentPpmV1.Max) < probabilityPpm;
    }
}

public sealed record ResidentGoalCandidateV1(OpaqueId128 GoalId, long Utility, int SemanticPriority)
{
    public void Validate()
    {
        if (GoalId.IsZero) throw new InvalidDataException("resident.goal-id-zero");
    }
}

public static class ResidentGoalSelectorV1
{
    public static ResidentGoalCandidateV1 Select(IEnumerable<ResidentGoalCandidateV1> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var canonical = candidates.ToArray();
        if (canonical.Length == 0) throw new InvalidDataException("resident.goal-empty");
        foreach (var candidate in canonical) candidate.Validate();
        if (canonical.Select(static candidate => candidate.GoalId).Distinct().Count() != canonical.Length)
            throw new InvalidDataException("resident.goal-duplicate-id");
        return canonical
            .OrderByDescending(static candidate => candidate.Utility)
            .ThenBy(static candidate => candidate.SemanticPriority)
            .ThenBy(static candidate => candidate.GoalId)
            .First();
    }
}

public readonly record struct ResidentGoapStateV1(string StableStateDigest)
{
    public ResidentGoapStateV1 Validate()
    {
        if (StableStateDigest.Length != 64 || !StableStateDigest.All(static ch => Uri.IsHexDigit(ch)))
            throw new InvalidDataException("resident.goap-state-digest-invalid");
        return this;
    }
}

public sealed record ResidentGoapEdgeV1(ResidentGoapStateV1 From, ResidentGoapStateV1 To, StableToken ActionToken, ulong Cost)
{
    public void Validate()
    {
        From.Validate();
        To.Validate();
        if (Cost == 0) throw new InvalidDataException("resident.goap-cost-zero");
    }
}

public enum ResidentGoapStatusV1 : byte
{
    Found = 1,
    Fallback = 2,
}

public sealed record ResidentGoapResultV1(ResidentGoapStatusV1 Status, IReadOnlyList<StableToken> Actions, ulong TotalCost, int ExpandedNodes, StableToken? FallbackAction);

public static class ResidentGoapPlannerV1
{
    public const int MaxExpandedNodes = 256;

    public static ResidentGoapResultV1 Plan(ResidentGoapStateV1 start, ResidentGoapStateV1 goal, IEnumerable<ResidentGoapEdgeV1> edges, Func<ResidentGoapStateV1, ulong> heuristic, StableToken fallbackAction, int expansionBudget = MaxExpandedNodes)
    {
        start.Validate();
        goal.Validate();
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(heuristic);
        if (expansionBudget is < 1 or > MaxExpandedNodes) throw new ArgumentOutOfRangeException(nameof(expansionBudget));
        var edgeArray = edges.ToArray();
        foreach (var edge in edgeArray) edge.Validate();
        var adjacency = edgeArray.GroupBy(static edge => edge.From).ToDictionary(
            static group => group.Key,
            static group => group.OrderBy(static edge => edge.ActionToken.Value, StringComparer.Ordinal)
                                 .ThenBy(static edge => edge.To.StableStateDigest, StringComparer.Ordinal)
                                 .ThenBy(static edge => edge.Cost)
                                 .ToArray());
        var bestCost = new Dictionary<ResidentGoapStateV1, ulong> { [start] = 0 };
        var previous = new Dictionary<ResidentGoapStateV1, (ResidentGoapStateV1 Previous, StableToken Action)>();
        var open = new List<OpenNode> { new(start, heuristic(start), 0, new StableToken("goap.start")) };
        var expanded = 0;
        while (open.Count != 0 && expanded < expansionBudget)
        {
            open.Sort(OpenNodeComparer.Instance);
            var current = open[0];
            open.RemoveAt(0);
            if (!bestCost.TryGetValue(current.State, out var currentBest) || currentBest != current.GCost) continue;
            expanded++;
            if (current.State == goal) return BuildFound(start, goal, current.GCost, expanded, previous);
            if (!adjacency.TryGetValue(current.State, out var outgoing)) continue;
            foreach (var edge in outgoing)
            {
                var candidateCost = checked(current.GCost + edge.Cost);
                if (bestCost.TryGetValue(edge.To, out var oldCost) && oldCost <= candidateCost) continue;
                bestCost[edge.To] = candidateCost;
                previous[edge.To] = (current.State, edge.ActionToken);
                open.Add(new OpenNode(edge.To, checked(candidateCost + heuristic(edge.To)), candidateCost, edge.ActionToken));
            }
        }
        return new ResidentGoapResultV1(ResidentGoapStatusV1.Fallback, Array.Empty<StableToken>(), 0, expanded, fallbackAction);
    }

    private static ResidentGoapResultV1 BuildFound(ResidentGoapStateV1 start, ResidentGoapStateV1 goal, ulong totalCost, int expanded, IReadOnlyDictionary<ResidentGoapStateV1, (ResidentGoapStateV1 Previous, StableToken Action)> previous)
    {
        var actions = new List<StableToken>();
        var cursor = goal;
        while (cursor != start)
        {
            if (!previous.TryGetValue(cursor, out var entry)) throw new InvalidDataException("resident.goap-path-broken");
            actions.Add(entry.Action);
            cursor = entry.Previous;
        }
        actions.Reverse();
        return new ResidentGoapResultV1(ResidentGoapStatusV1.Found, Array.AsReadOnly(actions.ToArray()), totalCost, expanded, null);
    }

    private sealed record OpenNode(ResidentGoapStateV1 State, ulong FCost, ulong GCost, StableToken ActionToken);
    private sealed class OpenNodeComparer : IComparer<OpenNode>
    {
        public static OpenNodeComparer Instance { get; } = new();
        public int Compare(OpenNode? left, OpenNode? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var comparison = left.FCost.CompareTo(right.FCost);
            if (comparison != 0) return comparison;
            comparison = left.GCost.CompareTo(right.GCost);
            if (comparison != 0) return comparison;
            comparison = string.CompareOrdinal(left.ActionToken.Value, right.ActionToken.Value);
            if (comparison != 0) return comparison;
            return string.CompareOrdinal(left.State.StableStateDigest, right.State.StableStateDigest);
        }
    }
}

public static class ResidentSkillCurveV1
{
    public static uint Learn(uint skillPpm, uint baseGainPpm)
    {
        if (skillPpm > ResidentPpmV1.Max || baseGainPpm > ResidentPpmV1.Max)
            throw new InvalidDataException("resident.skill-ppm-range");
        var remaining = ResidentPpmV1.Max - skillPpm;
        var increment = DivideRoundToEven((Int128)baseGainPpm * remaining, ResidentPpmV1.Max);
        var result = checked((Int128)skillPpm + increment);
        if (result < 0 || result > ResidentPpmV1.Max) throw new InvalidDataException("resident.skill-result-range");
        return (uint)result;
    }

    private static Int128 DivideRoundToEven(Int128 numerator, Int128 denominator)
    {
        var quotient = numerator / denominator;
        var remainder = numerator % denominator;
        var twice = remainder * 2;
        if (twice > denominator || (twice == denominator && (quotient & 1) != 0)) quotient++;
        return quotient;
    }
}
