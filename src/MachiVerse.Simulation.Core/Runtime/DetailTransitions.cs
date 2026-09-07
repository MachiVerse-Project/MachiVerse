using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Runtime;

public enum DetailTransitionDirectionV1 : byte
{
    Promotion = 1,
    Demotion = 2,
}

public static class DetailTransitionGuardV1
{
    public static readonly StableToken BoundResident = new("detail.guard.bound-resident");
    public static readonly StableToken ActiveTransaction = new("detail.guard.active-transaction");
}

public sealed class DetailRegionStateV1
{
    private readonly IReadOnlyList<KeyValuePair<StableToken, DetailLevelV1>> _levelByDomain;
    private readonly IReadOnlyList<StableToken> _activeGuards;

    public DetailRegionStateV1(
        OpaqueId128 detailRegionId,
        OpaqueId128 spatialScopeRef,
        IEnumerable<KeyValuePair<StableToken, DetailLevelV1>> levelByDomain,
        uint lineageGeneration,
        ulong lastTransitionStep,
        IEnumerable<StableToken>? activeGuards = null)
    {
        if (detailRegionId.IsZero) throw new ArgumentException("DetailRegionId ZERO is invalid.", nameof(detailRegionId));
        if (spatialScopeRef.IsZero) throw new ArgumentException("SpatialScopeRef ZERO is invalid.", nameof(spatialScopeRef));
        ArgumentNullException.ThrowIfNull(levelByDomain);

        var standardDomains = StandardDomainExecutionPlanV1.Create().Entries
            .Select(static entry => entry.DomainToken)
            .ToHashSet();
        var levels = levelByDomain
            .OrderBy(static item => item.Key.Value, StringComparer.Ordinal)
            .ToArray();
        if (levels.Length == 0)
            throw new InvalidDataException("detail.region-domain-level-empty");
        if (levels.Select(static item => item.Key).Distinct().Count() != levels.Length)
            throw new InvalidDataException("detail.region-domain-level-duplicate");
        if (levels.Any(item => !standardDomains.Contains(item.Key) || !Enum.IsDefined(item.Value)))
            throw new InvalidDataException("detail.region-domain-level-invalid");

        var guards = (activeGuards ?? Array.Empty<StableToken>())
            .OrderBy(static guard => guard.Value, StringComparer.Ordinal)
            .ToArray();
        if (guards.Distinct().Count() != guards.Length)
            throw new InvalidDataException("detail.region-guard-duplicate");

        DetailRegionId = detailRegionId;
        SpatialScopeRef = spatialScopeRef;
        _levelByDomain = Array.AsReadOnly(levels);
        LineageGeneration = lineageGeneration;
        LastTransitionStep = lastTransitionStep;
        _activeGuards = Array.AsReadOnly(guards);
    }

    public OpaqueId128 DetailRegionId { get; }
    public OpaqueId128 SpatialScopeRef { get; }
    public IReadOnlyList<KeyValuePair<StableToken, DetailLevelV1>> LevelByDomain => _levelByDomain;
    public uint LineageGeneration { get; }
    public ulong LastTransitionStep { get; }
    public IReadOnlyList<StableToken> ActiveGuards => _activeGuards;

    public DetailLevelV1 GetLevel(StableToken domainToken)
        => _levelByDomain.FirstOrDefault(item => item.Key == domainToken) is { Key: var key, Value: var level } &&
           EqualityComparer<StableToken>.Default.Equals(key, domainToken)
            ? level
            : throw new KeyNotFoundException($"Detail domain is not present in region: {domainToken.Value}");

    public bool HasGuard(StableToken guard) => _activeGuards.Contains(guard);

    public DetailRegionStateV1 Apply(StableToken domainToken, DetailLevelV1 targetLevel, ulong transitionStep)
    {
        if (transitionStep < LastTransitionStep)
            throw new InvalidDataException("detail.transition-step-regression");
        if (LineageGeneration == uint.MaxValue)
            throw new OverflowException("Detail lineage generation cannot wrap.");
        if (!Enum.IsDefined(targetLevel))
            throw new ArgumentOutOfRangeException(nameof(targetLevel));

        var found = false;
        var next = _levelByDomain
            .Select(item =>
            {
                if (item.Key != domainToken) return item;
                found = true;
                return new KeyValuePair<StableToken, DetailLevelV1>(item.Key, targetLevel);
            })
            .ToArray();
        if (!found)
            throw new KeyNotFoundException($"Detail domain is not present in region: {domainToken.Value}");

        return new DetailRegionStateV1(
            DetailRegionId,
            SpatialScopeRef,
            next,
            checked(LineageGeneration + 1),
            transitionStep,
            _activeGuards);
    }
}

public sealed class DetailTransitionCandidateV1
{
    public DetailTransitionCandidateV1(
        OpaqueId128 detailRegionId,
        StableToken domainToken,
        DetailLevelV1 currentLevel,
        DetailLevelV1 targetLevel,
        ulong requiredEffectiveStep,
        int semanticPriority,
        OpaqueId128 triggerId,
        ulong triggerObservedStep,
        uint estimatedRecordCount)
    {
        if (detailRegionId.IsZero) throw new ArgumentException("DetailRegionId ZERO is invalid.", nameof(detailRegionId));
        if (triggerId.IsZero) throw new ArgumentException("TriggerId ZERO is invalid.", nameof(triggerId));
        if (!Enum.IsDefined(currentLevel) || !Enum.IsDefined(targetLevel))
            throw new ArgumentOutOfRangeException(nameof(targetLevel));
        if (currentLevel == targetLevel)
            throw new InvalidDataException("detail.transition-noop");
        if (estimatedRecordCount == 0)
            throw new ArgumentOutOfRangeException(nameof(estimatedRecordCount));
        if (!StandardDomainExecutionPlanV1.Create().Entries.Any(entry => entry.DomainToken == domainToken))
            throw new InvalidDataException("detail.transition-domain-unregistered");

        DetailRegionId = detailRegionId;
        DomainToken = domainToken;
        CurrentLevel = currentLevel;
        TargetLevel = targetLevel;
        RequiredEffectiveStep = requiredEffectiveStep;
        SemanticPriority = semanticPriority;
        TriggerId = triggerId;
        TriggerObservedStep = triggerObservedStep;
        EstimatedRecordCount = estimatedRecordCount;
        Direction = (byte)targetLevel < (byte)currentLevel
            ? DetailTransitionDirectionV1.Promotion
            : DetailTransitionDirectionV1.Demotion;
    }

    public OpaqueId128 DetailRegionId { get; }
    public StableToken DomainToken { get; }
    public DetailLevelV1 CurrentLevel { get; }
    public DetailLevelV1 TargetLevel { get; }
    public ulong RequiredEffectiveStep { get; }
    public int SemanticPriority { get; }
    public OpaqueId128 TriggerId { get; }
    public ulong TriggerObservedStep { get; }
    public uint EstimatedRecordCount { get; }
    public DetailTransitionDirectionV1 Direction { get; }
}

public sealed class DetailDirectoryV1
{
    private readonly SortedDictionary<OpaqueId128, DetailRegionStateV1> _regions;
    private readonly IReadOnlyList<DetailTransitionCandidateV1> _pendingTransitions;

    public DetailDirectoryV1(
        IEnumerable<DetailRegionStateV1> regions,
        IEnumerable<DetailTransitionCandidateV1>? pendingTransitions = null)
    {
        ArgumentNullException.ThrowIfNull(regions);
        _regions = new SortedDictionary<OpaqueId128, DetailRegionStateV1>();
        foreach (var region in regions)
        {
            ArgumentNullException.ThrowIfNull(region);
            if (!_regions.TryAdd(region.DetailRegionId, region))
                throw new InvalidDataException("detail.region-duplicate");
        }

        _pendingTransitions = Array.AsReadOnly(
            DetailTransitionCanonicalOrderV1.Order(pendingTransitions ?? Array.Empty<DetailTransitionCandidateV1>()).ToArray());
    }

    public IReadOnlyCollection<DetailRegionStateV1> Regions => _regions.Values;
    public IReadOnlyList<DetailTransitionCandidateV1> PendingTransitions => _pendingTransitions;

    public DetailRegionStateV1 GetRegion(OpaqueId128 detailRegionId)
        => _regions.TryGetValue(detailRegionId, out var region)
            ? region
            : throw new KeyNotFoundException("Detail region is not present.");

    public DetailDirectoryV1 Apply(
        IEnumerable<DetailTransitionCandidateV1> selected,
        IEnumerable<DetailTransitionCandidateV1> deferred,
        ulong transitionStep)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(deferred);
        var nextRegions = new SortedDictionary<OpaqueId128, DetailRegionStateV1>(_regions);
        foreach (var candidate in DetailTransitionCanonicalOrderV1.Order(selected))
        {
            if (!nextRegions.TryGetValue(candidate.DetailRegionId, out var region))
                throw new InvalidDataException("detail.transition-region-missing");
            if (region.GetLevel(candidate.DomainToken) != candidate.CurrentLevel)
                throw new InvalidDataException("detail.transition-stale-current-level");
            nextRegions[candidate.DetailRegionId] = region.Apply(candidate.DomainToken, candidate.TargetLevel, transitionStep);
        }

        return new DetailDirectoryV1(nextRegions.Values, deferred);
    }
}

public sealed record DetailTransitionPolicyV1(
    ulong PromotionHysteresisSteps,
    ulong DemotionQuietSteps,
    ulong MinimumResidenceSteps,
    DetailLevelV1 BoundResidentFloor,
    DetailLevelV1 ActiveTransactionFloor,
    uint PromotionMaxRegionsPerStep,
    ulong PromotionMaxRecordsPerStep,
    uint DemotionMaxRegionsPerStep,
    ulong DemotionMaxRecordsPerStep)
{
    public static DetailTransitionPolicyV1 FromConfig(EffectiveCoreConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new DetailTransitionPolicyV1(
            checked((ulong)config.Get<long>("detail.promotion-hysteresis-steps")),
            checked((ulong)config.Get<long>("detail.demotion-quiet-steps")),
            checked((ulong)config.Get<long>("detail.minimum-residence-steps")),
            ParseFloor(config.Get<string>("detail.bound-resident-floor")),
            ParseFloor(config.Get<string>("detail.active-transaction-floor")),
            checked((uint)config.Get<long>("detail.promotion-max-regions-per-step")),
            checked((ulong)config.Get<long>("detail.promotion-max-records-per-step")),
            checked((uint)config.Get<long>("detail.demotion-max-regions-per-step")),
            checked((ulong)config.Get<long>("detail.demotion-max-records-per-step")));
    }

    private static DetailLevelV1 ParseFloor(string value) => value switch
    {
        "d0-entity" => DetailLevelV1.D0Entity,
        "d1-local-aggregate" => DetailLevelV1.D1LocalAggregate,
        _ => throw new InvalidDataException("detail.floor-config-invalid"),
    };
}

public sealed record DetailTransitionPlanV1(
    IReadOnlyList<DetailTransitionCandidateV1> Selected,
    IReadOnlyList<DetailTransitionCandidateV1> Deferred,
    IReadOnlyList<DetailTransitionCandidateV1> NotYetEligible)
{
    public bool HasMaterializationPending => Deferred.Any(static candidate => candidate.Direction == DetailTransitionDirectionV1.Promotion);
}

public static class DetailTransitionPlannerV1
{
    public static DetailTransitionPlanV1 Plan(
        DetailDirectoryV1 directory,
        IEnumerable<DetailTransitionCandidateV1> requestedTransitions,
        ulong basisStep,
        DetailTransitionPolicyV1 policy)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(requestedTransitions);
        ArgumentNullException.ThrowIfNull(policy);

        var canonical = DetailTransitionCanonicalOrderV1.Order(
                directory.PendingTransitions.Concat(requestedTransitions))
            .ToArray();
        ValidateNoDuplicateTargets(canonical);

        var eligible = new List<DetailTransitionCandidateV1>();
        var notYetEligible = new List<DetailTransitionCandidateV1>();
        foreach (var candidate in canonical)
        {
            var region = directory.GetRegion(candidate.DetailRegionId);
            if (region.GetLevel(candidate.DomainToken) != candidate.CurrentLevel)
                throw new InvalidDataException("detail.transition-stale-current-level");
            if (!IsEligible(candidate, region, basisStep, policy))
            {
                notYetEligible.Add(candidate);
                continue;
            }
            if (ViolatesFloor(candidate, region, policy))
            {
                notYetEligible.Add(candidate);
                continue;
            }
            eligible.Add(candidate);
        }

        var selected = new List<DetailTransitionCandidateV1>();
        var deferred = new List<DetailTransitionCandidateV1>();
        SelectWithinBudget(
            eligible.Where(static candidate => candidate.Direction == DetailTransitionDirectionV1.Promotion),
            policy.PromotionMaxRegionsPerStep,
            policy.PromotionMaxRecordsPerStep,
            selected,
            deferred);
        SelectWithinBudget(
            eligible.Where(static candidate => candidate.Direction == DetailTransitionDirectionV1.Demotion),
            policy.DemotionMaxRegionsPerStep,
            policy.DemotionMaxRecordsPerStep,
            selected,
            deferred);

        return new DetailTransitionPlanV1(
            Array.AsReadOnly(DetailTransitionCanonicalOrderV1.Order(selected).ToArray()),
            Array.AsReadOnly(DetailTransitionCanonicalOrderV1.Order(deferred).ToArray()),
            Array.AsReadOnly(DetailTransitionCanonicalOrderV1.Order(notYetEligible).ToArray()));
    }

    private static bool IsEligible(
        DetailTransitionCandidateV1 candidate,
        DetailRegionStateV1 region,
        ulong basisStep,
        DetailTransitionPolicyV1 policy)
    {
        if (candidate.RequiredEffectiveStep > basisStep || candidate.TriggerObservedStep > basisStep)
            return false;

        var triggerAge = basisStep - candidate.TriggerObservedStep;
        if (candidate.Direction == DetailTransitionDirectionV1.Promotion)
            return triggerAge >= policy.PromotionHysteresisSteps;

        var residenceAge = basisStep >= region.LastTransitionStep
            ? basisStep - region.LastTransitionStep
            : 0;
        return triggerAge >= policy.DemotionQuietSteps && residenceAge >= policy.MinimumResidenceSteps;
    }

    private static bool ViolatesFloor(
        DetailTransitionCandidateV1 candidate,
        DetailRegionStateV1 region,
        DetailTransitionPolicyV1 policy)
    {
        if (candidate.Direction != DetailTransitionDirectionV1.Demotion)
            return false;
        if (region.HasGuard(DetailTransitionGuardV1.BoundResident) && (byte)candidate.TargetLevel > (byte)policy.BoundResidentFloor)
            return true;
        if (region.HasGuard(DetailTransitionGuardV1.ActiveTransaction) && (byte)candidate.TargetLevel > (byte)policy.ActiveTransactionFloor)
            return true;
        return false;
    }

    private static void SelectWithinBudget(
        IEnumerable<DetailTransitionCandidateV1> candidates,
        uint maxRegions,
        ulong maxRecords,
        ICollection<DetailTransitionCandidateV1> selected,
        ICollection<DetailTransitionCandidateV1> deferred)
    {
        uint regions = 0;
        ulong records = 0;
        var selectedRegions = new HashSet<OpaqueId128>();
        foreach (var candidate in DetailTransitionCanonicalOrderV1.Order(candidates))
        {
            var addsRegion = selectedRegions.Add(candidate.DetailRegionId);
            var nextRegions = checked(regions + (addsRegion ? 1u : 0u));
            var nextRecords = checked(records + candidate.EstimatedRecordCount);
            if (nextRegions > maxRegions || nextRecords > maxRecords)
            {
                if (addsRegion) selectedRegions.Remove(candidate.DetailRegionId);
                deferred.Add(candidate);
                continue;
            }

            regions = nextRegions;
            records = nextRecords;
            selected.Add(candidate);
        }
    }

    private static void ValidateNoDuplicateTargets(IReadOnlyList<DetailTransitionCandidateV1> candidates)
    {
        var duplicate = candidates
            .GroupBy(static candidate => (candidate.DetailRegionId, candidate.DomainToken))
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException("detail.transition-duplicate-region-domain");
    }
}

public static class DetailTransitionCanonicalOrderV1
{
    public static IOrderedEnumerable<DetailTransitionCandidateV1> Order(IEnumerable<DetailTransitionCandidateV1> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var rankByDomain = StandardDomainExecutionPlanV1.Create().Entries
            .ToDictionary(static entry => entry.DomainToken, static entry => entry.DomainRank);
        return candidates
            .Select(candidate => candidate ?? throw new ArgumentNullException(nameof(candidates)))
            .OrderBy(static candidate => candidate.RequiredEffectiveStep)
            .ThenBy(static candidate => candidate.SemanticPriority)
            .ThenBy(static candidate => candidate.DetailRegionId)
            .ThenBy(candidate => rankByDomain[candidate.DomainToken])
            .ThenBy(static candidate => candidate.TriggerId);
    }
}
