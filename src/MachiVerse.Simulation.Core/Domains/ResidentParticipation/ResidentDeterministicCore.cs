using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.ResidentParticipation;

public readonly record struct ResidentPpmV1
{
    public const uint Scale = 1_000_000;

    private ResidentPpmV1(uint value) => Value = value;

    public uint Value { get; }

    public static ResidentPpmV1 Create(uint value)
    {
        if (value > Scale) throw new InvalidDataException("resident.ppm-out-of-range");
        return new ResidentPpmV1(value);
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
        if (next is < 0 or > Scale) throw new InvalidDataException("resident.ppm-out-of-range");
        return Create((uint)next);
    }
}

public enum ResidentLifecycleKindV1 : byte
{
    Developing = 1,
    Alive = 2,
    Deceased = 3,
}

public sealed record ResidentLifecycleStateV1(
    OpaqueId128 ResidentId,
    ResidentLifecycleKindV1 State,
    ulong BirthStep,
    ulong? DeathStep)
{
    public void Validate()
    {
        if (ResidentId.IsZero) throw new InvalidDataException("resident.lifecycle-id-zero");
        if (!Enum.IsDefined(State)) throw new InvalidDataException("resident.lifecycle-state-invalid");
        if (State == ResidentLifecycleKindV1.Deceased && DeathStep is null)
            throw new InvalidDataException("resident.lifecycle-death-step-missing");
        if (State != ResidentLifecycleKindV1.Deceased && DeathStep is not null)
            throw new InvalidDataException("resident.lifecycle-death-step-invalid");
        if (DeathStep is { } deathStep && deathStep < BirthStep)
            throw new InvalidDataException("resident.lifecycle-death-before-birth");
    }

    public ResidentLifecycleStateV1 TransitionTo(ResidentLifecycleKindV1 target, ulong effectiveStep)
    {
        Validate();
        var legal = (State, target) switch
        {
            (ResidentLifecycleKindV1.Developing, ResidentLifecycleKindV1.Alive) => true,
            (ResidentLifecycleKindV1.Alive, ResidentLifecycleKindV1.Deceased) => true,
            _ => false,
        };
        if (!legal || effectiveStep < BirthStep)
            throw new InvalidDataException("resident.lifecycle-transition-invalid");

        var next = this with
        {
            State = target,
            DeathStep = target == ResidentLifecycleKindV1.Deceased ? effectiveStep : null,
        };
        next.Validate();
        return next;
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
            throw new InvalidDataException("resident.disease-random-id-zero");
        if (probabilityPpm > ResidentPpmV1.Scale)
            throw new InvalidDataException("resident.ppm-out-of-range");
        if (probabilityPpm == 0) return false;
        if (probabilityPpm == ResidentPpmV1.Scale) return true;

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
            bound: ResidentPpmV1.Scale) < probabilityPpm;
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
            throw new InvalidDataException("resident.goal-id-duplicate");

        return materialized
            .OrderByDescending(static candidate => candidate.Utility)
            .ThenBy(static candidate => candidate.SemanticPriority)
            .ThenBy(static candidate => candidate.GoalId)
            .First();
    }
}

public sealed class ResidentGoapStateV1 : IComparable<ResidentGoapStateV1>, IEquatable<ResidentGoapStateV1>
{
    public ResidentGoapStateV1(string digestHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(digestHex);
        if (digestHex.Length != 64 || !digestHex.All(static ch => char.IsAsciiHexDigit(ch)))
            throw new InvalidDataException("resident.goap-state-digest-invalid");
        DigestHex = digestHex.ToUpperInvariant();
    }

    public string DigestHex { get; }

    public int CompareTo(ResidentGoapStateV1? other)
        => other is null ? 1 : string.CompareOrdinal(DigestHex, other.DigestHex);

    public bool Equals(ResidentGoapStateV1? other)
        => other is not null && string.Equals(DigestHex, other.DigestHex, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(DigestHex);
    public override string ToString() => DigestHex;
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

public enum ResidentGoapStatusV1 : byte
{
    Found = 1,
    Fallback = 2,
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
        StableToken fallbackAction,
        uint expansionBudget = MaxExpandedNodes)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(heuristic);
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
            .GroupBy(static edge => (edge.From.DigestHex, edge.To.DigestHex, edge.ActionToken.Value), StringTupleComparer.Instance)
            .Any(static group => group.Count() > 1))
            throw new InvalidDataException("resident.goap-edge-duplicate");

        var outgoing = materialized
            .GroupBy(static edge => edge.From.DigestHex, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static edge => edge.ActionToken.Value, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.To.DigestHex, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var open = new SortedSet<OpenEntryV1>(OpenEntryComparerV1.Instance);
        var best = new Dictionary<string, BestEntryV1>(StringComparer.Ordinal)
        {
            [start.DigestHex] = new BestEntryV1(0, string.Empty, string.Empty, null, null),
        };
        open.Add(new OpenEntryV1(
            checked(heuristic(start)),
            0,
            string.Empty,
            start.DigestHex));

        uint expanded = 0;
        while (open.Count != 0)
        {
            var current = open.Min!;
            open.Remove(current);
            if (!best.TryGetValue(current.StateDigest, out var known) ||
                known.GCost != current.GCost ||
                !string.Equals(known.ArrivalAction, current.ActionToken, StringComparison.Ordinal))
                continue;

            if (string.Equals(current.StateDigest, goal.DigestHex, StringComparison.Ordinal))
                return BuildFound(start.DigestHex, goal.DigestHex, best, current.GCost, expanded);

            if (expanded >= expansionBudget)
                return Fallback(expanded, fallbackAction);
            expanded++;

            if (!outgoing.TryGetValue(current.StateDigest, out var currentEdges))
                continue;

            foreach (var edge in currentEdges)
            {
                ulong nextG;
                try
                {
                    nextG = checked(current.GCost + edge.Cost);
                }
                catch (OverflowException ex)
                {
                    throw new OverflowException("simulation.numeric-overflow", ex);
                }

                var nextDigest = edge.To.DigestHex;
                var candidate = new BestEntryV1(
                    nextG,
                    edge.ActionToken.Value,
                    current.StateDigest,
                    current.StateDigest,
                    edge.ActionToken);

                if (best.TryGetValue(nextDigest, out var existing) && !IsBetter(candidate, existing))
                    continue;

                best[nextDigest] = candidate;
                ulong fCost;
                try
                {
                    fCost = checked(nextG + heuristic(edge.To));
                }
                catch (OverflowException ex)
                {
                    throw new OverflowException("simulation.numeric-overflow", ex);
                }
                open.Add(new OpenEntryV1(fCost, nextG, edge.ActionToken.Value, nextDigest));
            }
        }

        return Fallback(expanded, fallbackAction);
    }

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

    private static ResidentGoapResultV1 Fallback(uint expanded, StableToken fallbackAction)
        => new(
            ResidentGoapStatusV1.Fallback,
            Array.Empty<StableToken>(),
            0,
            expanded,
            fallbackAction);

    private sealed record BestEntryV1(
        ulong GCost,
        string ArrivalAction,
        string ParentTie,
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

    private sealed class StringTupleComparer : IEqualityComparer<(string From, string To, string Action)>
    {
        public static StringTupleComparer Instance { get; } = new();

        public bool Equals((string From, string To, string Action) x, (string From, string To, string Action) y)
            => string.Equals(x.From, y.From, StringComparison.Ordinal) &&
               string.Equals(x.To, y.To, StringComparison.Ordinal) &&
               string.Equals(x.Action, y.Action, StringComparison.Ordinal);

        public int GetHashCode((string From, string To, string Action) value)
            => HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.From),
                StringComparer.Ordinal.GetHashCode(value.To),
                StringComparer.Ordinal.GetHashCode(value.Action));
    }
}

public static class ResidentSkillCurveV1
{
    public static uint Learn(uint skillPpm, uint baseGainPpm)
    {
        if (skillPpm > ResidentPpmV1.Scale || baseGainPpm > ResidentPpmV1.Scale)
            throw new InvalidDataException("resident.ppm-out-of-range");

        var remaining = ResidentPpmV1.Scale - skillPpm;
        var increment = DivideRoundToEven(
            checked((Int128)baseGainPpm * remaining),
            ResidentPpmV1.Scale);
        var next = checked((ulong)skillPpm + increment);
        if (next > ResidentPpmV1.Scale) throw new InvalidDataException("resident.skill-overflow");
        return (uint)next;
    }

    private static ulong DivideRoundToEven(Int128 numerator, ulong denominator)
    {
        var divisor = (Int128)denominator;
        var quotient = numerator / divisor;
        var remainder = numerator % divisor;
        var twice = checked(remainder * 2);
        if (twice > divisor || (twice == divisor && (quotient & 1) != 0)) quotient++;
        return checked((ulong)quotient);
    }
}
