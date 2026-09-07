using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.ResidentParticipation;

public enum ResidentLifecyclePhaseV1 : byte
{
    Developing = 1,
    Alive = 2,
    Deceased = 3,
}

public sealed record ResidentLifecycleStateV1(
    OpaqueId128 ResidentId,
    ResidentLifecyclePhaseV1 Lifecycle,
    ulong? BirthStep,
    ulong? DeathStep,
    uint LineageGeneration,
    StableToken ProfileToken)
{
    public void Validate()
    {
        if (ResidentId.IsZero) throw new InvalidDataException("resident.lifecycle-resident-id-zero");
        if (!Enum.IsDefined(Lifecycle)) throw new InvalidDataException("resident.lifecycle-state-invalid");
        if (DeathStep is not null && BirthStep is not null && DeathStep.Value < BirthStep.Value)
            throw new InvalidDataException("resident.lifecycle-death-before-birth");
        if (Lifecycle == ResidentLifecyclePhaseV1.Deceased && DeathStep is null)
            throw new InvalidDataException("resident.lifecycle-death-step-required");
        if (Lifecycle != ResidentLifecyclePhaseV1.Deceased && DeathStep is not null)
            throw new InvalidDataException("resident.lifecycle-death-step-before-deceased");
    }

    public ResidentLifecycleStateV1 TransitionTo(ResidentLifecyclePhaseV1 next, ulong effectiveStep)
    {
        Validate();
        if (!Enum.IsDefined(next)) throw new InvalidDataException("resident.lifecycle-state-invalid");
        if (next == Lifecycle) return this;
        if ((byte)next < (byte)Lifecycle)
            throw new InvalidDataException("resident.lifecycle-nonmonotonic");
        if (Lifecycle == ResidentLifecyclePhaseV1.Deceased)
            throw new InvalidDataException("resident.lifecycle-terminal");
        if (BirthStep is not null && effectiveStep < BirthStep.Value)
            throw new InvalidDataException("resident.lifecycle-transition-before-birth");

        var candidate = this with
        {
            Lifecycle = next,
            DeathStep = next == ResidentLifecyclePhaseV1.Deceased ? effectiveStep : null,
        };
        candidate.Validate();
        return candidate;
    }
}

public static class ResidentPpmV1
{
    public const uint Maximum = 1_000_000;

    public static uint Require(uint value, string field)
    {
        if (value > Maximum) throw new InvalidDataException($"resident.ppm-out-of-range:{field}");
        return value;
    }
}

public sealed record ResidentHealthBoundsV1(
    uint DevelopmentPpm,
    uint HealthCapacityPpm,
    uint RecoveryPpm)
{
    public void Validate()
    {
        ResidentPpmV1.Require(DevelopmentPpm, "development_ppm");
        ResidentPpmV1.Require(HealthCapacityPpm, "health_capacity_ppm");
        ResidentPpmV1.Require(RecoveryPpm, "recovery_ppm");
    }
}

public sealed record ResidentPhysiologyBoundsV1(
    uint HungerPpm,
    uint ThirstPpm,
    uint FatiguePpm,
    uint SleepPressurePpm,
    uint ThermalStressPpm,
    uint HygienePpm)
{
    public void Validate()
    {
        ResidentPpmV1.Require(HungerPpm, "hunger_ppm");
        ResidentPpmV1.Require(ThirstPpm, "thirst_ppm");
        ResidentPpmV1.Require(FatiguePpm, "fatigue_ppm");
        ResidentPpmV1.Require(SleepPressurePpm, "sleep_pressure_ppm");
        ResidentPpmV1.Require(ThermalStressPpm, "thermal_stress_ppm");
        ResidentPpmV1.Require(HygienePpm, "hygiene_ppm");
    }
}

public static class ResidentDiseaseRandomV1
{
    private static readonly StableToken ResidentDomain = new("resident");

    public static bool Occurs(
        WorldSeed256 worldSeed,
        OpaqueId128 worldId,
        ulong step,
        OpaqueId128 residentId,
        OpaqueId128 conditionId,
        StableToken processToken,
        uint probabilityPpm)
    {
        if (worldId.IsZero) throw new ArgumentException("WorldId ZERO is invalid.", nameof(worldId));
        if (residentId.IsZero) throw new ArgumentException("ResidentId ZERO is invalid.", nameof(residentId));
        if (conditionId.IsZero) throw new ArgumentException("ConditionId ZERO is invalid.", nameof(conditionId));
        ResidentPpmV1.Require(probabilityPpm, "disease_probability_ppm");
        if (probabilityPpm == 0) return false;
        if (probabilityPpm == ResidentPpmV1.Maximum) return true;

        var context = new RandomContextV1(
            worldId,
            step,
            ResidentDomain,
            processToken,
            residentId,
            conditionId,
            OpaqueId128.Zero,
            0);
        return DeterministicRandom.BoundedUInt64(worldSeed, context, 0, ResidentPpmV1.Maximum) < probabilityPpm;
    }
}

public sealed record ResidentGoalCandidateV1(
    OpaqueId128 GoalId,
    StableToken GoalToken,
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
    public static ResidentGoalCandidateV1 SelectBest(IEnumerable<ResidentGoalCandidateV1> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var ordered = candidates.ToArray();
        if (ordered.Length == 0) throw new InvalidDataException("resident.goal-empty-candidate-set");
        foreach (var candidate in ordered) candidate.Validate();
        if (ordered.Select(static candidate => candidate.GoalId).Distinct().Count() != ordered.Length)
            throw new InvalidDataException("resident.goal-duplicate-id");

        return ordered
            .OrderByDescending(static candidate => candidate.Utility)
            .ThenBy(static candidate => candidate.SemanticPriority)
            .ThenBy(static candidate => candidate.GoalId)
            .First();
    }
}

public static class ResidentSkillCurveV1
{
    public static uint ApplyPractice(uint skillPpm, uint baseGainPpm)
    {
        ResidentPpmV1.Require(skillPpm, "skill_ppm");
        ResidentPpmV1.Require(baseGainPpm, "base_gain_ppm");

        var numerator = checked((Int128)baseGainPpm * (ResidentPpmV1.Maximum - skillPpm));
        var increment = DivideRoundToEvenPositive(numerator, ResidentPpmV1.Maximum);
        var next = checked((UInt128)skillPpm + increment);
        if (next > ResidentPpmV1.Maximum)
            throw new OverflowException("simulation.numeric-overflow");
        return (uint)next;
    }

    private static UInt128 DivideRoundToEvenPositive(Int128 numerator, uint denominator)
    {
        if (numerator < 0) throw new ArgumentOutOfRangeException(nameof(numerator));
        var quotient = numerator / denominator;
        var remainder = numerator % denominator;
        var twiceRemainder = checked(remainder * 2);
        if (twiceRemainder > denominator ||
            (twiceRemainder == denominator && (quotient & (Int128)1) != 0))
        {
            quotient++;
        }
        return (UInt128)quotient;
    }
}

public sealed class ResidentGoapTransitionV1
{
    public ResidentGoapTransitionV1(
        ReadOnlySpan<byte> fromStateDigest,
        ReadOnlySpan<byte> toStateDigest,
        StableToken actionToken,
        ulong cost)
    {
        if (fromStateDigest.Length != 32) throw new ArgumentException("GOAP from-state digest must be 32 bytes.", nameof(fromStateDigest));
        if (toStateDigest.Length != 32) throw new ArgumentException("GOAP to-state digest must be 32 bytes.", nameof(toStateDigest));
        FromStateDigest = fromStateDigest.ToArray();
        ToStateDigest = toStateDigest.ToArray();
        ActionToken = actionToken;
        Cost = cost;
    }

    public byte[] FromStateDigest { get; }
    public byte[] ToStateDigest { get; }
    public StableToken ActionToken { get; }
    public ulong Cost { get; }
}

public enum ResidentGoapStatusV1 : byte
{
    Found = 1,
    NoPlan = 2,
    BudgetExceeded = 3,
}

public enum ResidentGoapFallbackReasonV1 : byte
{
    NoPlan = 1,
    BudgetExceeded = 2,
}

public sealed class ResidentFallbackBehaviorRegistryV1
{
    private readonly IReadOnlyDictionary<ResidentGoapFallbackReasonV1, StableToken> _byReason;

    public ResidentFallbackBehaviorRegistryV1(IEnumerable<KeyValuePair<ResidentGoapFallbackReasonV1, StableToken>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var map = new Dictionary<ResidentGoapFallbackReasonV1, StableToken>();
        foreach (var pair in entries.OrderBy(static pair => pair.Key))
        {
            if (!Enum.IsDefined(pair.Key)) throw new InvalidDataException("resident.goap-fallback-reason-invalid");
            if (!map.TryAdd(pair.Key, pair.Value)) throw new InvalidDataException("resident.goap-fallback-duplicate-reason");
        }
        foreach (var reason in Enum.GetValues<ResidentGoapFallbackReasonV1>())
        {
            if (!map.ContainsKey(reason)) throw new InvalidDataException("resident.goap-fallback-incomplete-registry");
        }
        _byReason = map;
    }

    public StableToken Resolve(ResidentGoapFallbackReasonV1 reason)
        => _byReason.TryGetValue(reason, out var token)
            ? token
            : throw new InvalidDataException("resident.goap-fallback-reason-invalid");
}

public sealed record ResidentGoapPlanResultV1(
    ResidentGoapStatusV1 Status,
    IReadOnlyList<StableToken> Actions,
    ulong TotalCost,
    uint ExpandedNodeCount,
    StableToken? FallbackActionToken);

public static class ResidentGoapPlannerV1
{
    public const uint MaximumExpandedNodes = 256;

    public static ResidentGoapPlanResultV1 Plan(
        IEnumerable<ResidentGoapTransitionV1> transitions,
        ReadOnlySpan<byte> startStateDigest,
        ReadOnlySpan<byte> goalStateDigest,
        Func<ReadOnlyMemory<byte>, ulong> heuristicCost,
        uint expansionBudget,
        ResidentFallbackBehaviorRegistryV1 fallbackRegistry)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(heuristicCost);
        ArgumentNullException.ThrowIfNull(fallbackRegistry);
        if (startStateDigest.Length != 32) throw new ArgumentException("GOAP start-state digest must be 32 bytes.", nameof(startStateDigest));
        if (goalStateDigest.Length != 32) throw new ArgumentException("GOAP goal-state digest must be 32 bytes.", nameof(goalStateDigest));
        if (expansionBudget is 0 or > MaximumExpandedNodes)
            throw new InvalidDataException("resident.goap-expansion-budget-out-of-range");

        var materialized = transitions.ToArray();
        if (materialized.Any(static transition => transition is null))
            throw new InvalidDataException("resident.goap-null-transition");
        var duplicates = materialized
            .GroupBy(static transition => (
                From: Convert.ToHexString(transition.FromStateDigest),
                To: Convert.ToHexString(transition.ToStateDigest),
                Action: transition.ActionToken.Value))
            .Any(static group => group.Count() != 1);
        if (duplicates) throw new InvalidDataException("resident.goap-duplicate-transition");

        var outgoing = materialized
            .GroupBy(static transition => Convert.ToHexString(transition.FromStateDigest), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static transition => transition.ActionToken.Value, StringComparer.Ordinal)
                    .ThenBy(static transition => Convert.ToHexString(transition.ToStateDigest), StringComparer.Ordinal)
                    .ThenBy(static transition => transition.Cost)
                    .ToArray(),
                StringComparer.Ordinal);

        var startHex = Convert.ToHexString(startStateDigest);
        var goalHex = Convert.ToHexString(goalStateDigest);
        if (startHex == goalHex)
        {
            return new ResidentGoapPlanResultV1(
                ResidentGoapStatusV1.Found,
                Array.Empty<StableToken>(),
                0,
                0,
                null);
        }

        var open = new SortedSet<OpenEntry>(OpenEntryComparer.Instance);
        var bestG = new Dictionary<string, ulong>(StringComparer.Ordinal) { [startHex] = 0 };
        var stateBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [startHex] = startStateDigest.ToArray() };
        var parent = new Dictionary<string, ParentLink>(StringComparer.Ordinal);
        open.Add(new OpenEntry(
            CheckedAdd(0, heuristicCost(startStateDigest.ToArray())),
            0,
            string.Empty,
            startHex));

        uint expanded = 0;
        while (open.Count != 0)
        {
            var current = open.Min!;
            open.Remove(current);
            if (!bestG.TryGetValue(current.StateHex, out var knownG) || knownG != current.GCost)
                continue;

            if (current.StateHex == goalHex)
                return Found(current.StateHex, current.GCost, expanded, parent);

            if (expanded >= expansionBudget)
                return Fallback(ResidentGoapStatusV1.BudgetExceeded, expanded, fallbackRegistry);
            expanded++;

            if (!outgoing.TryGetValue(current.StateHex, out var edges)) continue;
            foreach (var edge in edges)
            {
                var toHex = Convert.ToHexString(edge.ToStateDigest);
                var tentativeG = CheckedAdd(current.GCost, edge.Cost);
                if (bestG.TryGetValue(toHex, out var previousG) && tentativeG >= previousG)
                    continue;

                bestG[toHex] = tentativeG;
                stateBytes[toHex] = edge.ToStateDigest;
                parent[toHex] = new ParentLink(current.StateHex, edge.ActionToken);
                var fCost = CheckedAdd(tentativeG, heuristicCost(edge.ToStateDigest));
                open.Add(new OpenEntry(fCost, tentativeG, edge.ActionToken.Value, toHex));
            }
        }

        return Fallback(ResidentGoapStatusV1.NoPlan, expanded, fallbackRegistry);
    }

    private static ResidentGoapPlanResultV1 Found(
        string goalHex,
        ulong totalCost,
        uint expanded,
        IReadOnlyDictionary<string, ParentLink> parent)
    {
        var actions = new List<StableToken>();
        var current = goalHex;
        while (parent.TryGetValue(current, out var link))
        {
            actions.Add(link.ActionToken);
            current = link.ParentStateHex;
        }
        actions.Reverse();
        return new ResidentGoapPlanResultV1(
            ResidentGoapStatusV1.Found,
            Array.AsReadOnly(actions.ToArray()),
            totalCost,
            expanded,
            null);
    }

    private static ResidentGoapPlanResultV1 Fallback(
        ResidentGoapStatusV1 status,
        uint expanded,
        ResidentFallbackBehaviorRegistryV1 registry)
    {
        var reason = status == ResidentGoapStatusV1.BudgetExceeded
            ? ResidentGoapFallbackReasonV1.BudgetExceeded
            : ResidentGoapFallbackReasonV1.NoPlan;
        return new ResidentGoapPlanResultV1(
            status,
            Array.Empty<StableToken>(),
            0,
            expanded,
            registry.Resolve(reason));
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

    private readonly record struct ParentLink(string ParentStateHex, StableToken ActionToken);
    private readonly record struct OpenEntry(ulong FCost, ulong GCost, string ActionToken, string StateHex);

    private sealed class OpenEntryComparer : IComparer<OpenEntry>
    {
        public static OpenEntryComparer Instance { get; } = new();

        public int Compare(OpenEntry x, OpenEntry y)
        {
            var compare = x.FCost.CompareTo(y.FCost);
            if (compare != 0) return compare;
            compare = x.GCost.CompareTo(y.GCost);
            if (compare != 0) return compare;
            compare = string.CompareOrdinal(x.ActionToken, y.ActionToken);
            if (compare != 0) return compare;
            return string.CompareOrdinal(x.StateHex, y.StateHex);
        }
    }
}
