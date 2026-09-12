using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04CanonicalDetailTransitionBindingSmoke
{
    internal static void Run()
    {
        Qa04CanonicalDetailTransitionBindingV1.ValidateCanonicalContract();

        Require(Qa04CanonicalDetailTransitionBindingV1.RequirementsForStep(0).Count == 0 &&
                Qa04CanonicalDetailTransitionBindingV1.RequirementsForStep(299).Count == 0 &&
                Qa04CanonicalDetailTransitionBindingV1.RequirementsForStep(27_000).Count == 0,
            "QA-04 canonical detail workload must exist only at in-run nonzero 300-Step cadence points.");

        var all = Qa04CanonicalDetailTransitionBindingV1.CanonicalRequirements().ToArray();
        Require(all.Length == 1_424 &&
                all.Select(static requirement => requirement.RequestOrdinal).Distinct().Count() == 1_424 &&
                all.Select(static requirement => requirement.TileIndex).Distinct().Count() == 1_424 &&
                all.Select(static requirement => requirement.SpatialScopeId).Distinct().Count() == 1_424,
            "QA-04 canonical detail workload must consume exactly 1,424 unique tile authorities.");
        Require(all[0].BasisStep == 300 && all[0].RequestOrdinal == 0 && all[0].TileIndex == 17 &&
                all[^1].BasisStep == 26_700 && all[^1].RequestOrdinal == 1_423,
            "QA-04 canonical detail cadence/request ordinal boundary drifted.");

        var step = 300UL;
        var requirements = Qa04CanonicalDetailTransitionBindingV1.RequirementsForStep(step);
        var promotionRecords = requirements
            .Where(static requirement => requirement.Direction == DetailTransitionDirectionV1.Promotion)
            .Aggregate(0UL, static (total, requirement) => checked(total + requirement.EstimatedRecordCount));
        var demotionRecords = requirements
            .Where(static requirement => requirement.Direction == DetailTransitionDirectionV1.Demotion)
            .Aggregate(0UL, static (total, requirement) => checked(total + requirement.EstimatedRecordCount));
        Require(requirements.Count == 16 &&
                requirements.Count(static requirement => requirement.Direction == DetailTransitionDirectionV1.Promotion) == 6 &&
                requirements.Count(static requirement => requirement.Direction == DetailTransitionDirectionV1.Demotion) == 10 &&
                promotionRecords == 30_000 &&
                demotionRecords == 80_000,
            "QA-04 canonical detail cadence mix/budget drifted.");

        var configDigest = SHA256.HashData("qa04-canonical-detail-binding-config"u8);
        var regionByRequest = requirements.ToDictionary(
            static requirement => requirement.RequestOrdinal,
            static requirement => FixtureRegion(requirement));
        var bindings = Qa04CanonicalDetailTransitionBindingV1.BindForStep(
            step,
            activeConfigGeneration: 1,
            configDigest,
            requirement => regionByRequest[requirement.RequestOrdinal]);
        Require(bindings.Count == 16,
            "QA-04 canonical detail binding must produce one production request per cadence requirement.");

        var expectedTrigger = DetailTransitionTriggerAuthorityV1.ConfigPolicyTriggerId(1, configDigest);
        foreach (var binding in bindings)
        {
            var requirement = binding.Requirement;
            var request = binding.Request;
            Require(binding.Region.SpatialScopeRef == requirement.SpatialScopeId &&
                    request.DetailRegionId == binding.Region.DetailRegionId &&
                    request.DomainToken == requirement.DomainToken &&
                    request.CurrentLevel == requirement.CurrentLevel &&
                    request.TargetLevel == requirement.TargetLevel &&
                    request.RequiredEffectiveStep == step &&
                    request.SemanticPriority == 0 &&
                    request.TriggerSource == DetailTransitionTriggerSourceV1.ConfigPolicy &&
                    request.TriggerId == expectedTrigger &&
                    request.TriggerObservedStep == step &&
                    request.EstimatedRecordCount == requirement.EstimatedRecordCount,
                "QA-04 canonical detail production request field drifted.");
        }

        var frozen = new FrozenStepInputV1(
            Qa04ReferenceLoadV1.WorldId,
            step,
            configGeneration: 1,
            configDigest,
            Array.Empty<ScheduledOperationRefV1>());
        var triggerAuthority = DetailTransitionTriggerAuthorityV1.FromStep(frozen);
        var candidates = bindings
            .Select(binding => DetailTransitionAdmissionV1.Admit(binding.Request, triggerAuthority))
            .ToArray();
        Require(candidates.Length == 16,
            "QA-04 canonical detail requests must pass ordinary trigger-authority admission.");

        var directory = new DetailDirectoryV1(regionByRequest.Values);
        var policy = new DetailTransitionPolicyV1(
            PromotionHysteresisSteps: 30,
            DemotionQuietSteps: 300,
            MinimumResidenceSteps: 300,
            BoundResidentFloor: DetailLevelV1.D0Entity,
            ActiveTransactionFloor: DetailLevelV1.D0Entity,
            PromotionMaxRegionsPerStep: 4,
            PromotionMaxRecordsPerStep: 20_000,
            DemotionMaxRegionsPerStep: 8,
            DemotionMaxRecordsPerStep: 50_000);
        var plan = DetailTransitionPlannerV1.Plan(directory, candidates, step, policy);
        Require(plan.Selected.Count == 0 && plan.Deferred.Count == 0 && plan.NotYetEligible.Count == 16,
            "QA-04 detail requests injected at the cadence Step must remain pending until standard hysteresis/quiet policy permits them.");

        RequireThrows<InvalidDataException>(
            () => Qa04CanonicalDetailTransitionBindingV1.BindForStep(
                step,
                1,
                configDigest,
                _ => null!),
            "qa04.workload.detail-transition-region-authority-missing",
            "QA-04 canonical detail binding must fail closed when actual DetailRegion authority is absent.");

        var first = requirements[0];
        var wrongScope = new DetailRegionStateV1(
            FixtureId(first, "wrong-scope"),
            Qa04SpatialTileScopeAuthorityV1.ScopeId(checked((ushort)((first.TileIndex + 1) % Qa04ReferenceLoadV1.RegionalTileCount))),
            [new KeyValuePair<StableToken, DetailLevelV1>(first.DomainToken, first.CurrentLevel)],
            lineageGeneration: 1,
            lastTransitionStep: 0);
        RequireThrows<InvalidDataException>(
            () => Qa04CanonicalDetailTransitionBindingV1.BindForStep(
                step,
                1,
                configDigest,
                requirement => requirement.RequestOrdinal == first.RequestOrdinal
                    ? wrongScope
                    : regionByRequest[requirement.RequestOrdinal]),
            "qa04.workload.detail-transition-region-scope-mismatch",
            "QA-04 canonical detail binding must reject a region identity attached to the wrong TileScope.");
    }

    private static DetailRegionStateV1 FixtureRegion(Qa04CanonicalDetailTransitionRequirementV1 requirement)
        => new(
            FixtureId(requirement, "region"),
            requirement.SpatialScopeId,
            [new KeyValuePair<StableToken, DetailLevelV1>(requirement.DomainToken, requirement.CurrentLevel)],
            lineageGeneration: 1,
            lastTransitionStep: 0);

    private static OpaqueId128 FixtureId(
        Qa04CanonicalDetailTransitionRequirementV1 requirement,
        string suffix)
    {
        var id = HashSuite.Trunc128(HashSuite.DomainHash("mv.test.qa04-detail-region.v1", writer =>
        {
            writer.WriteArrayStart(3);
            writer.WriteBytes(requirement.SpatialScopeId.ToBytes());
            writer.WriteAsciiText(requirement.DomainToken.Value);
            writer.WriteAsciiText(suffix);
        }));
        if (id.IsZero) throw new InvalidOperationException("QA-04 detail smoke fixture id unexpectedly ZERO.");
        return id;
    }

    private static void RequireThrows<T>(Action action, string expectedMessage, string failureMessage)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T ex) when (ex.Message == expectedMessage)
        {
            return;
        }
        throw new InvalidOperationException(failureMessage);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
