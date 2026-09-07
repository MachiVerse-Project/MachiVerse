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

        var result = next == ResidentLifecycleKindV1.Deceased
            ? this with { State = next, DeathStep = effectiveStep }
            : this with { State = next };
        result.Validate();
        return result;
    }
}

public readonly record struct ResidentPpmV1(uint Value)
{
    public const uint Max = 1_000_000;
    public const uint Scale = Max;

    public static ResidentPpmV1 Create(long value)
    {
        if (value < 0 || value > Max) throw new InvalidDataException("resident.ppm-out-of-range");
        return new ResidentPpmV1((uint)value);
    }

    public ResidentPpmV1 AddChecked(long delta)
    {
        long next;
        try
        {
            next = checked((long)Value + delta);
        }
        catch (OverflowException ex)
        {
            throw new OverflowException("simulation.numeric-overflow", ex);
        }
        return Create(next);
    }
}

public sealed record ResidentHealthStateV1(
    ResidentPpmV1 HealthCapacity,
    ResidentPpmV1 Pain,
    ResidentPpmV1 Stress,
    ResidentPpmV1 Fatigue)
{
    public ResidentHealthStateV1 ApplyDelta(
        long healthCapacityDelta,
        long painDelta,
        long stressDelta,
        long fatigueDelta)
        => new(
            HealthCapacity.AddChecked(healthCapacityDelta),
            Pain.AddChecked(painDelta),
            Stress.AddChecked(stressDelta),
            Fatigue.AddChecked(fatigueDelta));
}

public static class ResidentConditionRandomV1
{
    private static readonly StableToken ResidentDomain = new("resident");

    public static bool Occurs(
        WorldSeed256 worldSeed,
        OpaqueId128 worldId,
        ulong step,
        OpaqueId128 residentId,
        OpaqueId128 conditionId,
        StableToken eventKind,
        uint probabilityPpm)
    {
        if (worldId.IsZero || residentId.IsZero || conditionId.IsZero)
            throw new InvalidDataException("resident.condition-random-id-zero");
        if (probabilityPpm > ResidentPpmV1.Max)
            throw new InvalidDataException("resident.condition-random-probability-range");
        if (probabilityPpm == 0) return false;
        if (probabilityPpm == ResidentPpmV1.Max) return true;

        var context = new RandomContextV1(
            worldId,
            step,
            ResidentDomain,
            eventKind,
            residentId,
            conditionId,
            OpaqueId128.Zero,
            0);
        return DeterministicRandom.BoundedUInt64(
            worldSeed,
            context,
            drawIndex: 0,
            bound: ResidentPpmV1.Max) < probabilityPpm;
    }
}

public sealed record ResidentGoalCandidateV1(
    OpaqueId128 GoalId,
    long Utility,
    int SemanticPriority)
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
        var materialized = candidates.ToArray();
        if (materialized.Length == 0) throw new InvalidDataException("resident.goal-empty");
        foreach (var candidate in materialized) candidate.Validate();
        if (materialized.Select(static candidate => candidate.GoalId).Distinct().Count() != materialized.Length)
            throw new InvalidDataException("resident.goal-duplicate-id");

        return materialized
            .OrderByDescending(static candidate => candidate.Utility)
            .ThenBy(static candidate => candidate.SemanticPriority)
            .ThenBy(static candidate => candidate.GoalId)
            .First();
    }
}

public sealed class ResidentGoapStateV1 : IComparable<ResidentGoapStateV1>, IEquatable<ResidentGoapStateV1>
{
    public ResidentGoapStateV1(string stableStateDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableStateDigest);
        if (stableStateDigest.Length != 64 || !stableStateDigest.All(static ch => char.IsAsciiHexDigit(ch)))
            throw new InvalidDataException("resident.goap-state-digest-invalid");
        StableStateDigest = stableStateDigest.ToUpperInvariant();
    }

    public string StableStateDigest { get; }

    public int CompareTo(ResidentGoapStateV1? other)
        => other is null ? 1 : string.CompareOrdinal(StableStateDigest, other.StableStateDigest);

    public bool Equals(ResidentGoapStateV1? other)
        => other is not null && string.Equals(StableStateDigest, other.StableStateDigest, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ResidentGoapStateV1 other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(StableStateDigest);
    public override string ToString() => StableStateDigest;
}

public sealed record ResidentGoapEdgeV1(
    ResidentGoapStateV1 From,
    ResidentGoapStateV1 To,
    StableToken ActionToken,
    ulong Cost)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(From);
        ArgumentNullException.ThrowIfNull(To);
        if (From.Equals(To)) throw new InvalidDataException("resident.goap-self-edge");
        if (Cost == 0) throw new InvalidDataException("resident.goap-zero-cost");
    }
}

public sealed class ResidentFallbackBehaviorRegistryV1
{
    private readonly StableToken[] _behaviors;

    public ResidentFallbackBehaviorRegistryV1(IEnumerable<StableToken> behaviors)
    {
        ArgumentNullException.ThrowIfNull(behaviors);
        _behaviors = behaviors.OrderBy(static token => token.Value, StringComparer.Ordinal).ToArray();
        if (_behaviors.Length == 0) throw new InvalidDataException("resident.fallback-registry-empty");
        if (_behaviors.Select(static token => token.Value).Distinct(StringComparer.Ordinal).Count() != _behaviors.Length)
            throw new InvalidDataException("resident.fallback-registry-duplicate");
    }

    public StableToken Select() => _behaviors[0];
}

public enum ResidentGoapStatusV1 : byte
{
    Found = 1,
    NoPlan = 2,
    BudgetExceeded = 3,
}

public sealed record ResidentGoapResultV1(
    ResidentGoapStatusV1 Status,
    IReadOnlyList<StableToken> Actions,
    ulong TotalCost,
    uint ExpandedNodes,
    StableToken? FallbackAction);

public static class ResidentGoapPlannerV1
{
    public const uint MaxExpandedNodes = 256;

    public static ResidentGoapResultV1 Plan(
        ResidentGoapStateV1 start,
        ResidentGoapStateV1 goal,
        IEnumerable<ResidentGoapEdgeV1> edges,
        Func<ResidentGoapStateV1, ulong> heuristic,
        ResidentFallbackBehaviorRegistryV1 fallbackRegistry,
        uint expansionBudget = MaxExpandedNodes)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(heuristic);
        ArgumentNullException.ThrowIfNull(fallbackRegistry);
        if (expansionBudget is 0 or > MaxExpandedNodes)
            throw new ArgumentOutOfRangeException(nameof(expansionBudget));

        if (start.Equals(goal))
            return new ResidentGoapResultV1(
                ResidentGoapStatusV1.Found,
                Array.Empty<StableToken>(),
                0,
                0,
                null);

        var materialized = edges.ToArray();
        foreach (var edge in materialized) edge.Validate();
        if (materialized
            .GroupBy(static edge => (edge.From.StableStateDigest, edge.To.StableStateDigest, edge.ActionToken.Value))
            .Any(static group => group.Count() > 1))
            throw new InvalidDataException("resident.goap-edge-duplicate");

        var outgoing = materialized
            .GroupBy(static edge => edge.From.StableStateDigest, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static edge => edge.ActionToken.Value, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.To.StableStateDigest, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.Cost)
                    .ToArray(),
                StringComparer.Ordinal);

        var open = new SortedSet<OpenEntryV1>(OpenEntryComparerV1.Instance);
        var best = new Dictionary<string, BestEntryV1>(StringComparer.Ordinal)
        {
            [start.StableStateDigest] = new BestEntryV1(0, string.Empty, null, null),
        };
        open.Add(new OpenEntryV1(
            heuristic(start),
            0,
            string.Empty,
            start.StableStateDigest));

        uint expanded = 0;
        while (open.Count != 0)
        {
            var current = open.Min!;
            open.Remove(current);
            if (!best.TryGetValue(current.StateDigest, out var known) ||
                known.GCost != current.GCost ||
                !string.Equals(known.ArrivalAction, current.ActionToken, StringComparison.Ordinal))
                continue;

            if (string.Equals(current.StateDigest, goal.StableStateDigest, StringComparison.Ordinal))
                return BuildFound(start.StableStateDigest, goal.StableStateDigest, best, current.GCost, expanded);

            if (expanded >= expansionBudget)
                return Fallback(ResidentGoapStatusV1.BudgetExceeded, expanded, fallbackRegistry);
            expanded++;

            if (!outgoing.TryGetValue(current.StateDigest, out var currentEdges))
                continue;

            foreach (var edge in currentEdges)
            {
                ulong nextG;
                ulong fCost;
                try
                {
                    nextG = checked(current.GCost + edge.Cost);
                    fCost = checked(nextG + heuristic(edge.To));
                }
                catch (OverflowException ex)
                {
                    throw new OverflowException("simulation.numeric-overflow", ex);
                }

                var nextDigest = edge.To.StableStateDigest;
                var candidate = new BestEntryV1(
                    nextG,
                    edge.ActionToken.Value,
                    current.StateDigest,
                    edge.ActionToken);
                if (best.TryGetValue(nextDigest, out var existing) && !IsBetter(candidate, existing))
                    continue;

                best[nextDigest] = candidate;
                open.Add(new OpenEntryV1(fCost, nextG, edge.ActionToken.Value, nextDigest));
            }
        }

        return Fallback(ResidentGoapStatusV1.NoPlan, expanded, fallbackRegistry);
    }

    public static ResidentGoapResultV1 Plan(
        ResidentGoapStateV1 start,
        ResidentGoapStateV1 goal,
        IEnumerable<ResidentGoapEdgeV1> edges,
        Func<ResidentGoapStateV1, ulong> heuristic,
        StableToken fallbackAction,
        int expansionBudget = (int)MaxExpandedNodes)
        => Plan(
            start,
            goal,
            edges,
            heuristic,
            new ResidentFallbackBehaviorRegistryV1([fallbackAction]),
            checked((uint)expansionBudget));

    private static bool IsBetter(BestEntryV1 candidate, BestEntryV1 existing)
    {
        var compare = candidate.GCost.CompareTo(existing.GCost);
        if (compare != 0) return compare < 0;
        compare = string.CompareOrdinal(candidate.ArrivalAction, existing.ArrivalAction);
        if (compare != 0) return compare < 0;
        return string.CompareOrdinal(candidate.ParentStateDigest, existing.ParentStateDigest) < 0;
    }

    private static ResidentGoapResultV1 BuildFound(
        string startDigest,
        string goalDigest,
        IReadOnlyDictionary<string, BestEntryV1> best,
        ulong totalCost,
        uint expanded)
    {
        var actions = new List<StableToken>();
        var cursor = goalDigest;
        while (!string.Equals(cursor, startDigest, StringComparison.Ordinal))
        {
            if (!best.TryGetValue(cursor, out var step) || step.ParentStateDigest is null || step.Action is null)
                throw new InvalidDataException("resident.goap-parent-missing");
            actions.Add(step.Action.Value);
            cursor = step.ParentStateDigest;
        }
        actions.Reverse();
        return new ResidentGoapResultV1(
            ResidentGoapStatusV1.Found,
            Array.AsReadOnly(actions.ToArray()),
            totalCost,
            expanded,
            null);
    }

    private static ResidentGoapResultV1 Fallback(
        ResidentGoapStatusV1 status,
        uint expanded,
        ResidentFallbackBehaviorRegistryV1 fallbackRegistry)
        => new(
            status,
            Array.Empty<StableToken>(),
            0,
            expanded,
            fallbackRegistry.Select());

    private sealed record BestEntryV1(
        ulong GCost,
        string ArrivalAction,
        string? ParentStateDigest,
        StableToken? Action);

    private sealed record OpenEntryV1(
        ulong FCost,
        ulong GCost,
        string ActionToken,
        string StateDigest);

    private sealed class OpenEntryComparerV1 : IComparer<OpenEntryV1>
    {
        public static OpenEntryComparerV1 Instance { get; } = new();

        public int Compare(OpenEntryV1? left, OpenEntryV1? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var compare = left.FCost.CompareTo(right.FCost);
            if (compare != 0) return compare;
            compare = left.GCost.CompareTo(right.GCost);
            if (compare != 0) return compare;
            compare = string.CompareOrdinal(left.ActionToken, right.ActionToken);
            if (compare != 0) return compare;
            return string.CompareOrdinal(left.StateDigest, right.StateDigest);
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
        if (result < 0 || result > ResidentPpmV1.Max)
            throw new InvalidDataException("resident.skill-result-range");
        return (uint)result;
    }

    private static Int128 DivideRoundToEven(Int128 numerator, Int128 denominator)
    {
        var quotient = numerator / denominator;
        var remainder = numerator % denominator;
        var twice = remainder * 2;
        if (twice > denominator || (twice == denominator && (quotient & 1) != 0))
            quotient++;
        return quotient;
    }
}
