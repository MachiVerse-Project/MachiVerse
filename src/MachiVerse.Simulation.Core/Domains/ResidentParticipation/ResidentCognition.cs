using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.ResidentParticipation;

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

public static class DeterministicResidentGoalSelectorV1
{
    public static ResidentGoalCandidateV1 Select(IEnumerable<ResidentGoalCandidateV1> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var ordered = candidates.ToArray();
        foreach (var candidate in ordered) candidate.Validate();
        if (ordered.Length == 0) throw new InvalidDataException("resident.goal-candidate-empty");
        if (ordered.Select(static item => item.GoalId).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("resident.goal-id-duplicate");

        return ordered
            .OrderByDescending(static item => item.Utility)
            .ThenBy(static item => item.SemanticPriority)
            .ThenBy(static item => item.GoalId)
            .First();
    }
}

public sealed record ResidentGoapTransitionV1(
    byte[] FromStateDigest,
    byte[] ToStateDigest,
    StableToken ActionToken,
    ulong Cost)
{
    public void Validate()
    {
        ValidateDigest(FromStateDigest, nameof(FromStateDigest));
        ValidateDigest(ToStateDigest, nameof(ToStateDigest));
        if (FromStateDigest.AsSpan().SequenceEqual(ToStateDigest))
            throw new InvalidDataException("resident.plan-self-transition");
        if (Cost == 0) throw new InvalidDataException("resident.plan-zero-cost");
    }

    internal static void ValidateDigest(byte[] digest, string name)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (digest.Length != 32) throw new ArgumentException("Stable state digest must be 32 bytes.", name);
    }
}

public sealed class ResidentGoapGraphV1
{
    private readonly IReadOnlyDictionary<string, ResidentGoapTransitionV1[]> _outgoing;

    public ResidentGoapGraphV1(IEnumerable<ResidentGoapTransitionV1> transitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        var materialized = transitions.ToArray();
        foreach (var transition in materialized) transition.Validate();

        if (materialized.GroupBy(static item =>
                (From: Convert.ToHexString(item.FromStateDigest), item.ActionToken.Value, To: Convert.ToHexString(item.ToStateDigest)))
            .Any(static group => group.Count() != 1))
            throw new InvalidDataException("resident.plan-transition-duplicate");

        _outgoing = materialized
            .GroupBy(static item => Convert.ToHexString(item.FromStateDigest), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static item => item.ActionToken.Value, StringComparer.Ordinal)
                    .ThenBy(static item => Convert.ToHexString(item.ToStateDigest), StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
    }

    public IReadOnlyList<ResidentGoapTransitionV1> Outgoing(byte[] stateDigest)
    {
        ResidentGoapTransitionV1.ValidateDigest(stateDigest, nameof(stateDigest));
        return _outgoing.TryGetValue(Convert.ToHexString(stateDigest), out var transitions)
            ? transitions
            : Array.Empty<ResidentGoapTransitionV1>();
    }
}

public sealed class ResidentFallbackBehaviorRegistryV1
{
    private readonly StableToken[] _behaviors;

    public ResidentFallbackBehaviorRegistryV1(IEnumerable<StableToken> behaviors)
    {
        ArgumentNullException.ThrowIfNull(behaviors);
        _behaviors = behaviors
            .OrderBy(static token => token.Value, StringComparer.Ordinal)
            .ToArray();
        if (_behaviors.Length == 0) throw new InvalidDataException("resident.fallback-registry-empty");
        if (_behaviors.Select(static token => token.Value).Distinct(StringComparer.Ordinal).Count() != _behaviors.Length)
            throw new InvalidDataException("resident.fallback-registry-duplicate");
    }

    public StableToken Select() => _behaviors[0];
}

public enum ResidentPlanStatusV1 : byte
{
    Found = 1,
    NoPlan = 2,
    BudgetExceeded = 3,
}

public sealed record ResidentPlanResultV1(
    ResidentPlanStatusV1 Status,
    IReadOnlyList<StableToken> Actions,
    ulong TotalCost,
    uint ExpandedNodeCount,
    StableToken? FallbackBehavior);

public static class DeterministicResidentGoapPlannerV1
{
    public const uint StandardMaxExpandedNodes = 256;

    public static ResidentPlanResultV1 Find(
        ResidentGoapGraphV1 graph,
        byte[] startStateDigest,
        byte[] goalStateDigest,
        Func<byte[], ulong> heuristic,
        ResidentFallbackBehaviorRegistryV1 fallbackRegistry,
        uint expansionBudget = StandardMaxExpandedNodes)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(heuristic);
        ArgumentNullException.ThrowIfNull(fallbackRegistry);
        ResidentGoapTransitionV1.ValidateDigest(startStateDigest, nameof(startStateDigest));
        ResidentGoapTransitionV1.ValidateDigest(goalStateDigest, nameof(goalStateDigest));
        if (expansionBudget == 0 || expansionBudget > StandardMaxExpandedNodes)
            throw new ArgumentOutOfRangeException(nameof(expansionBudget));

        var startKey = Convert.ToHexString(startStateDigest);
        var goalKey = Convert.ToHexString(goalStateDigest);
        if (startKey == goalKey)
            return new ResidentPlanResultV1(ResidentPlanStatusV1.Found, Array.Empty<StableToken>(), 0, 0, null);

        var open = new SortedSet<OpenEntryV1>(OpenEntryComparerV1.Instance);
        var bestG = new Dictionary<string, ulong>(StringComparer.Ordinal) { [startKey] = 0 };
        var parent = new Dictionary<string, ParentEntryV1>(StringComparer.Ordinal);
        open.Add(new OpenEntryV1(heuristic(startStateDigest), 0, string.Empty, startKey));

        uint expanded = 0;
        while (open.Count != 0)
        {
            var current = open.Min!;
            open.Remove(current);
            if (!bestG.TryGetValue(current.StateKey, out var knownG) || knownG != current.GCost)
                continue;

            if (current.StateKey == goalKey)
                return BuildFound(goalKey, startKey, parent, current.GCost, expanded);

            if (expanded >= expansionBudget)
                return Fallback(ResidentPlanStatusV1.BudgetExceeded, expanded, fallbackRegistry);
            expanded++;

            var currentDigest = Convert.FromHexString(current.StateKey);
            foreach (var transition in graph.Outgoing(currentDigest))
            {
                var nextKey = Convert.ToHexString(transition.ToStateDigest);
                var nextG = checked(current.GCost + transition.Cost);
                if (bestG.TryGetValue(nextKey, out var existing) && existing <= nextG)
                    continue;

                bestG[nextKey] = nextG;
                parent[nextKey] = new ParentEntryV1(current.StateKey, transition.ActionToken);
                var fCost = checked(nextG + heuristic(transition.ToStateDigest));
                open.Add(new OpenEntryV1(fCost, nextG, transition.ActionToken.Value, nextKey));
            }
        }

        return Fallback(ResidentPlanStatusV1.NoPlan, expanded, fallbackRegistry);
    }

    private static ResidentPlanResultV1 BuildFound(
        string goalKey,
        string startKey,
        IReadOnlyDictionary<string, ParentEntryV1> parent,
        ulong totalCost,
        uint expanded)
    {
        var actions = new List<StableToken>();
        var cursor = goalKey;
        while (cursor != startKey)
        {
            if (!parent.TryGetValue(cursor, out var step))
                throw new InvalidDataException("resident.plan-parent-missing");
            actions.Add(step.ActionToken);
            cursor = step.ParentStateKey;
        }
        actions.Reverse();
        return new ResidentPlanResultV1(
            ResidentPlanStatusV1.Found,
            Array.AsReadOnly(actions.ToArray()),
            totalCost,
            expanded,
            null);
    }

    private static ResidentPlanResultV1 Fallback(
        ResidentPlanStatusV1 status,
        uint expanded,
        ResidentFallbackBehaviorRegistryV1 registry)
        => new(status, Array.Empty<StableToken>(), 0, expanded, registry.Select());

    private sealed record ParentEntryV1(string ParentStateKey, StableToken ActionToken);
    private sealed record OpenEntryV1(ulong FCost, ulong GCost, string ActionToken, string StateKey);

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
            return string.CompareOrdinal(left.StateKey, right.StateKey);
        }
    }
}
