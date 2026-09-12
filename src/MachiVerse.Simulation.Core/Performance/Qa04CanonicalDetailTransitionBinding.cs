using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Performance;

public sealed record Qa04CanonicalDetailTransitionRequirementV1(
    ulong BasisStep,
    ulong CadenceOrdinal,
    int LocalOrdinal,
    ulong RequestOrdinal,
    ushort TileIndex,
    OpaqueId128 SpatialScopeId,
    StableToken DomainToken,
    DetailLevelV1 CurrentLevel,
    DetailLevelV1 TargetLevel,
    uint EstimatedRecordCount)
{
    public DetailTransitionDirectionV1 Direction
        => (byte)TargetLevel < (byte)CurrentLevel
            ? DetailTransitionDirectionV1.Promotion
            : DetailTransitionDirectionV1.Demotion;
}

public sealed record Qa04CanonicalDetailTransitionBindingResultV1(
    Qa04CanonicalDetailTransitionRequirementV1 Requirement,
    DetailRegionStateV1 Region,
    DetailTransitionRequestV1 Request);

/// <summary>
/// Maps the exact perf.reference.v1 detail-transition cadence to production DetailTransitionRequestV1.
///
/// The workload design intentionally requires the actual canonical tile DetailRegion identity. That
/// identity is not derived here: callers must resolve the already-authoritative DetailRegionStateV1.
/// This keeps the workload mapping executable without inventing a benchmark-only DetailRegionId.
/// </summary>
public static class Qa04CanonicalDetailTransitionBindingV1
{
    public const int RequestsPerCadence = 16;
    public const int PromotionRequestsPerCadence = 6;
    public const int DemotionRequestsPerCadence = 10;
    public const uint PromotionEstimatedRecordCount = 5_000;
    public const uint DemotionEstimatedRecordCount = 8_000;
    public const ulong CanonicalCadenceCount = 89;
    public const ulong CanonicalRequestCount = CanonicalCadenceCount * RequestsPerCadence;

    private const ulong TilePermutationMultiplier = 4_051;
    private const ulong TilePermutationOffset = 17;

    private static readonly StableToken EnvironmentDomain = new("environment");
    private static readonly StableToken ResidentDomain = new("resident");
    private static readonly StableToken PhysicalBuiltDomain = new("physical_built");

    public static void ValidateCanonicalContract()
    {
        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        Qa04ReferenceScenariosV1.ValidateCanonicalContract();
        Qa04SpatialTileScopeAuthorityV1.ValidateCanonicalContract();

        if (Qa04ReferenceLoadV1.WarmupSteps + Qa04ReferenceLoadV1.MeasurementSteps != 27_000 ||
            Qa04ReferenceLoadV1.DetailTransitionEverySteps != 300 ||
            Qa04ReferenceScenariosV1.DetailTransitionEverySteps != 300 ||
            Qa04ReferenceScenariosV1.PromotionRegionCount != PromotionRequestsPerCadence ||
            Qa04ReferenceScenariosV1.DemotionRegionCount != DemotionRequestsPerCadence ||
            Qa04ReferenceScenariosV1.PromotionCandidateRecordCount !=
                (ulong)PromotionRequestsPerCadence * PromotionEstimatedRecordCount ||
            Qa04ReferenceScenariosV1.DemotionCandidateRecordCount !=
                (ulong)DemotionRequestsPerCadence * DemotionEstimatedRecordCount)
            throw new InvalidDataException("qa04.workload.detail-transition-canonical-count-drift");

        var all = CanonicalRequirements().ToArray();
        if ((ulong)all.Length != CanonicalRequestCount)
            throw new InvalidDataException("qa04.workload.detail-transition-request-count-drift");
        if ((ulong)all.Select(static requirement => requirement.TileIndex).Distinct().Count() != CanonicalRequestCount)
            throw new InvalidDataException("qa04.workload.detail-transition-tile-reuse");
        if ((ulong)all.Select(static requirement => requirement.RequestOrdinal).Distinct().Count() != CanonicalRequestCount)
            throw new InvalidDataException("qa04.workload.detail-transition-request-ordinal-duplicate");
        if (all.Any(static requirement => requirement.SpatialScopeId.IsZero))
            throw new InvalidDataException("qa04.workload.detail-transition-scope-zero");

        for (ulong cadence = 0; cadence < CanonicalCadenceCount; cadence++)
        {
            var step = checked((cadence + 1) * Qa04ReferenceLoadV1.DetailTransitionEverySteps);
            var batch = RequirementsForStep(step);
            if (batch.Count != RequestsPerCadence ||
                batch.Count(static requirement => requirement.Direction == DetailTransitionDirectionV1.Promotion) != PromotionRequestsPerCadence ||
                batch.Count(static requirement => requirement.Direction == DetailTransitionDirectionV1.Demotion) != DemotionRequestsPerCadence)
                throw new InvalidDataException("qa04.workload.detail-transition-cadence-mix-drift");
        }
    }

    public static IReadOnlyList<Qa04CanonicalDetailTransitionRequirementV1> RequirementsForStep(ulong basisStep)
    {
        var totalSteps = checked(Qa04ReferenceLoadV1.WarmupSteps + Qa04ReferenceLoadV1.MeasurementSteps);
        if (basisStep == 0 || basisStep >= totalSteps ||
            basisStep % Qa04ReferenceLoadV1.DetailTransitionEverySteps != 0)
            return Array.Empty<Qa04CanonicalDetailTransitionRequirementV1>();

        var cadenceOrdinal = checked(basisStep / Qa04ReferenceLoadV1.DetailTransitionEverySteps - 1);
        if (cadenceOrdinal >= CanonicalCadenceCount)
            throw new InvalidDataException("qa04.workload.detail-transition-cadence-ordinal-drift");

        var requirements = new Qa04CanonicalDetailTransitionRequirementV1[RequestsPerCadence];
        for (var local = 0; local < RequestsPerCadence; local++)
            requirements[local] = CreateRequirement(basisStep, cadenceOrdinal, local);
        return Array.AsReadOnly(requirements);
    }

    public static IEnumerable<Qa04CanonicalDetailTransitionRequirementV1> CanonicalRequirements()
    {
        for (ulong cadence = 0; cadence < CanonicalCadenceCount; cadence++)
        {
            var step = checked((cadence + 1) * Qa04ReferenceLoadV1.DetailTransitionEverySteps);
            foreach (var requirement in RequirementsForStep(step))
                yield return requirement;
        }
    }

    public static IReadOnlyList<Qa04CanonicalDetailTransitionBindingResultV1> BindForStep(
        ulong basisStep,
        ulong activeConfigGeneration,
        byte[] activeConfigDigest,
        Func<Qa04CanonicalDetailTransitionRequirementV1, DetailRegionStateV1> resolveRegion)
    {
        if (activeConfigGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(activeConfigGeneration));
        ArgumentNullException.ThrowIfNull(activeConfigDigest);
        if (activeConfigDigest.Length != 32)
            throw new ArgumentException("Config digest must be exactly 32 bytes.", nameof(activeConfigDigest));
        ArgumentNullException.ThrowIfNull(resolveRegion);

        var requirements = RequirementsForStep(basisStep);
        if (requirements.Count == 0)
            return Array.Empty<Qa04CanonicalDetailTransitionBindingResultV1>();

        var triggerId = DetailTransitionTriggerAuthorityV1.ConfigPolicyTriggerId(
            activeConfigGeneration,
            activeConfigDigest);
        if (triggerId.IsZero)
            throw new InvalidDataException("qa04.workload.detail-transition-config-trigger-zero");

        var result = new Qa04CanonicalDetailTransitionBindingResultV1[requirements.Count];
        var seenRegions = new HashSet<OpaqueId128>();
        for (var index = 0; index < requirements.Count; index++)
        {
            var requirement = requirements[index];
            var region = resolveRegion(requirement)
                ?? throw new InvalidDataException("qa04.workload.detail-transition-region-authority-missing");
            if (region.DetailRegionId.IsZero)
                throw new InvalidDataException("qa04.workload.detail-transition-region-id-zero");
            if (region.SpatialScopeRef != requirement.SpatialScopeId)
                throw new InvalidDataException("qa04.workload.detail-transition-region-scope-mismatch");
            if (region.GetLevel(requirement.DomainToken) != requirement.CurrentLevel)
                throw new InvalidDataException("qa04.workload.detail-transition-region-level-mismatch");
            if (!seenRegions.Add(region.DetailRegionId))
                throw new InvalidDataException("qa04.workload.detail-transition-region-reused-within-cadence");

            var request = new DetailTransitionRequestV1(
                region.DetailRegionId,
                requirement.DomainToken,
                requirement.CurrentLevel,
                requirement.TargetLevel,
                requiredEffectiveStep: basisStep,
                semanticPriority: 0,
                DetailTransitionTriggerSourceV1.ConfigPolicy,
                triggerId,
                triggerObservedStep: basisStep,
                requirement.EstimatedRecordCount);
            result[index] = new Qa04CanonicalDetailTransitionBindingResultV1(requirement, region, request);
        }
        return Array.AsReadOnly(result);
    }

    private static Qa04CanonicalDetailTransitionRequirementV1 CreateRequirement(
        ulong basisStep,
        ulong cadenceOrdinal,
        int localOrdinal)
    {
        if (localOrdinal is < 0 or >= RequestsPerCadence)
            throw new ArgumentOutOfRangeException(nameof(localOrdinal));

        var requestOrdinal = checked(cadenceOrdinal * RequestsPerCadence + (ulong)localOrdinal);
        var tileIndex = checked((ushort)(
            (checked(requestOrdinal * TilePermutationMultiplier) + TilePermutationOffset) %
            (ulong)Qa04ReferenceLoadV1.RegionalTileCount));
        var promotion = localOrdinal < PromotionRequestsPerCadence;
        var domain = promotion
            ? ((cadenceOrdinal + (ulong)localOrdinal) & 1UL) == 0 ? EnvironmentDomain : ResidentDomain
            : ((cadenceOrdinal + (ulong)localOrdinal) & 1UL) == 0 ? EnvironmentDomain : PhysicalBuiltDomain;
        var current = promotion ? DetailLevelV1.D1LocalAggregate : DetailLevelV1.D0Entity;
        var target = promotion ? DetailLevelV1.D0Entity : DetailLevelV1.D1LocalAggregate;
        var estimated = promotion ? PromotionEstimatedRecordCount : DemotionEstimatedRecordCount;
        var scopeId = Qa04SpatialTileScopeAuthorityV1.ScopeId(tileIndex);

        return new Qa04CanonicalDetailTransitionRequirementV1(
            basisStep,
            cadenceOrdinal,
            localOrdinal,
            requestOrdinal,
            tileIndex,
            scopeId,
            domain,
            current,
            target,
            estimated);
    }
}
