using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Resident;
using MachiVerse.Simulation.Core.Domains.ResidentParticipation;
using MachiVerse.Simulation.Core.Runtime;

internal static class Sim09ResidentCoreSmoke
{
    internal static void Run()
    {
        VerifyLifecycle();
        VerifyHealthBounds();
        VerifyDiseaseRandom();
        VerifyGoalTie();
        VerifyGoap();
        VerifySkillCurve();
        VerifyParticipationOneToOne();
        VerifyParticipationControlAndPolicy();
        VerifyParticipationDeathAndDetailFloor();
        VerifyDeliveryPerceptionBeliefSeparation();
        VerifyPhysicalIntentBoundary();
    }

    private static void VerifyLifecycle()
    {
        var residentId = Id("0000000000000000000000000000a001");
        var developing = new ResidentLifecycleStateV1(
            residentId,
            ResidentLifecycleKindV1.Developing,
            BirthStep: 10,
            DeathStep: null);
        developing.Validate();
        var alive = developing.TransitionTo(ResidentLifecycleKindV1.Alive, 10);
        var deceased = alive.TransitionTo(ResidentLifecycleKindV1.Deceased, 20);

        Require(alive.State == ResidentLifecycleKindV1.Alive && deceased.DeathStep == 20,
            "domain.resident.lifecycle: monotonic lifecycle transition mismatch.");
        RequireReject(
            () => deceased.TransitionTo(ResidentLifecycleKindV1.Alive, 30),
            "resident.lifecycle-transition-invalid");
    }

    private static void VerifyHealthBounds()
    {
        var health = new ResidentHealthStateV1(
            ResidentPpmV1.Create(800_000),
            ResidentPpmV1.Create(100_000),
            ResidentPpmV1.Create(200_000),
            ResidentPpmV1.Create(300_000));
        var next = health.ApplyDelta(-100_000, 50_000, -25_000, 100_000);
        Require(next.HealthCapacity.Value == 700_000 &&
                next.Pain.Value == 150_000 &&
                next.Stress.Value == 175_000 &&
                next.Fatigue.Value == 400_000,
            "domain.resident.health.bounds: checked ppm update mismatch.");
        RequireReject(() => _ = ResidentPpmV1.Create(1_000_001), "resident.ppm-out-of-range");
        RequireReject(() => _ = health.ApplyDelta(300_000, 0, 0, 0), "resident.ppm-out-of-range");
    }

    private static void VerifyDiseaseRandom()
    {
        var worldId = Id("0000000000000000000000000000a010");
        var residentId = Id("0000000000000000000000000000a011");
        var conditionA = Id("0000000000000000000000000000a012");
        var conditionB = Id("0000000000000000000000000000a013");
        var seed = new WorldSeed256(Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray());
        var eventKind = new StableToken("disease-progress");

        var first = ResidentConditionRandomV1.Occurs(seed, worldId, 77, residentId, conditionA, eventKind, 500_000);
        var repeat = ResidentConditionRandomV1.Occurs(seed, worldId, 77, residentId, conditionA, eventKind, 500_000);
        Require(first == repeat,
            "domain.resident.disease.random: identical addressable random context must repeat exactly.");

        var forward = new[] { conditionA, conditionB }.ToDictionary(
            static condition => condition,
            condition => ResidentConditionRandomV1.Occurs(seed, worldId, 77, residentId, condition, eventKind, 500_000));
        var reverse = new[] { conditionB, conditionA }.ToDictionary(
            static condition => condition,
            condition => ResidentConditionRandomV1.Occurs(seed, worldId, 77, residentId, condition, eventKind, 500_000));
        Require(forward.OrderBy(static pair => pair.Key).SequenceEqual(reverse.OrderBy(static pair => pair.Key)),
            "domain.resident.disease.random: condition iteration order changed per-condition random result.");
        Require(!ResidentConditionRandomV1.Occurs(seed, worldId, 77, residentId, conditionA, eventKind, 0) &&
                ResidentConditionRandomV1.Occurs(seed, worldId, 77, residentId, conditionA, eventKind, 1_000_000),
            "domain.resident.disease.random: probability boundary mismatch.");
    }

    private static void VerifyGoalTie()
    {
        var lowId = Id("0000000000000000000000000000a020");
        var highId = Id("0000000000000000000000000000a021");
        var lowerPriority = Id("0000000000000000000000000000a022");
        var highUtility = Id("0000000000000000000000000000a023");
        var candidates = new[]
        {
            new ResidentGoalCandidateV1(highId, 100, 2),
            new ResidentGoalCandidateV1(lowId, 100, 2),
            new ResidentGoalCandidateV1(lowerPriority, 100, 1),
            new ResidentGoalCandidateV1(highUtility, 101, 99),
        };

        Require(ResidentGoalSelectorV1.Select(candidates).GoalId == highUtility,
            "domain.resident.goal.tie: utility descending must dominate tie-breaks.");
        Require(ResidentGoalSelectorV1.Select(candidates.Where(candidate => candidate.GoalId != highUtility)).GoalId == lowerPriority,
            "domain.resident.goal.tie: semantic priority ascending tie-break mismatch.");
        Require(ResidentGoalSelectorV1.Select(candidates.Where(candidate => candidate.GoalId == lowId || candidate.GoalId == highId)).GoalId == lowId,
            "domain.resident.goal.tie: GoalId bytewise ascending final tie-break mismatch.");
        Require(ResidentGoalSelectorV1.Select(candidates.Reverse()).GoalId == highUtility,
            "domain.resident.goal.tie: input permutation changed goal selection.");
    }

    private static void VerifyGoap()
    {
        var start = State("start");
        var viaA = State("via-a");
        var viaB = State("via-b");
        var goal = State("goal");
        var edges = new[]
        {
            new ResidentGoapEdgeV1(start, viaB, new StableToken("resident.action.b"), 1),
            new ResidentGoapEdgeV1(viaB, goal, new StableToken("resident.action.finish"), 1),
            new ResidentGoapEdgeV1(start, viaA, new StableToken("resident.action.a"), 1),
            new ResidentGoapEdgeV1(viaA, goal, new StableToken("resident.action.finish"), 1),
        };
        var fallback = new StableToken("resident.fallback.safe");

        var result = ResidentGoapPlannerV1.Plan(start, goal, edges, static _ => 0, fallback);
        var reverse = ResidentGoapPlannerV1.Plan(start, goal, edges.Reverse(), static _ => 0, fallback);
        Require(result.Status == ResidentGoapStatusV1.Found &&
                result.TotalCost == 2 &&
                result.Actions.Select(static token => token.Value).SequenceEqual([
                    "resident.action.a", "resident.action.finish"
                ]),
            "domain.resident.goap: canonical equal-cost action-token tie mismatch.");
        Require(result.Actions.SequenceEqual(reverse.Actions) && result.TotalCost == reverse.TotalCost,
            "domain.resident.goap: edge input permutation changed plan.");
        Require(result.ExpandedNodes <= ResidentGoapPlannerV1.MaxExpandedNodes,
            "domain.resident.goap: expansion budget escaped the standard 256 bound.");

        var noPlan = ResidentGoapPlannerV1.Plan(
            start,
            goal,
            Array.Empty<ResidentGoapEdgeV1>(),
            static _ => 0,
            fallback);
        Require(noPlan.Status == ResidentGoapStatusV1.Fallback && noPlan.FallbackAction == fallback,
            "domain.resident.goap-fallback: no-plan path must use deterministic fallback action.");
    }

    private static void VerifySkillCurve()
    {
        Require(ResidentSkillCurveV1.Learn(0, 100_000) == 100_000,
            "domain.resident.skill.curve: zero-skill learning vector mismatch.");
        Require(ResidentSkillCurveV1.Learn(500_000, 100_000) == 550_000,
            "domain.resident.skill.curve: midpoint learning vector mismatch.");
        Require(ResidentSkillCurveV1.Learn(999_995, 500_000) == 999_997,
            "domain.resident.skill.curve: round-ties-to-even learning vector mismatch.");
        Require(ResidentSkillCurveV1.Learn(999_999, 1_000_000) == 1_000_000,
            "domain.resident.skill.curve: final bounded learning increment mismatch.");
    }

    private static void VerifyParticipationOneToOne()
    {
        var diverA = Id("0000000000000000000000000000b001");
        var diverB = Id("0000000000000000000000000000b002");
        var residentA = Id("0000000000000000000000000000b003");
        var residentB = Id("0000000000000000000000000000b004");
        var existing = new ParticipationBindingV1(
            Id("0000000000000000000000000000b010"), diverA, residentA,
            ParticipationBindingStatusV1.Active, 10, null, 1);
        var digest = new byte[32];

        var blockedSameResident = BindRequest(
            Id("0000000000000000000000000000b011"), diverB, residentA, 1, 11,
            new SameStepOrderKey(2, 40, digest, 0, Id("0000000000000000000000000000b021")));
        var blockedSameDiver = BindRequest(
            Id("0000000000000000000000000000b012"), diverA, residentB, 2, 11,
            new SameStepOrderKey(2, 40, digest, 1, Id("0000000000000000000000000000b022")));
        var accepted = BindRequest(
            Id("0000000000000000000000000000b013"), diverB, residentB, 1, 11,
            new SameStepOrderKey(2, 40, digest, 2, Id("0000000000000000000000000000b023")));

        var resolution = ParticipationBindResolverV1.Resolve(
            [existing], [accepted, blockedSameDiver, blockedSameResident]);
        Require(resolution.Accepted.Count == 1 && resolution.Accepted[0].BindingId == accepted.Binding.BindingId,
            "domain.participation.binding: one-to-one active binding resolution mismatch.");
        Require(resolution.RejectedBindingIds.Count == 2,
            "domain.participation.binding: conflicting binding requests must be rejected.");
    }

    private static void VerifyParticipationControlAndPolicy()
    {
        var binding = new ParticipationBindingV1(
            Id("0000000000000000000000000000c001"),
            Id("0000000000000000000000000000c002"),
            Id("0000000000000000000000000000c003"),
            ParticipationBindingStatusV1.Active,
            20,
            null,
            7);
        var available = ParticipationControlContextFactoryV1.ForBinding(
            binding, ParticipationControlAvailabilityV1.Available, null, 21);
        var unavailable = ParticipationControlContextFactoryV1.ForBinding(
            binding, ParticipationControlAvailabilityV1.Unavailable, 3, 22);
        Require(available.Mode == ResidentControlModeV1.DiverControlAvailable &&
                unavailable.Mode == ResidentControlModeV1.DiverAbsentPolicy &&
                unavailable.BindingId == binding.BindingId && binding.IsActive,
            "domain.participation.disconnect: control unavailable must retain the active binding.");

        var policy = new ParticipationAbsencePolicyV1(
            binding.DiverRef,
            binding.BindingId,
            3,
            [new StableToken("safety"), new StableToken("routine")],
            20);
        var revised = policy.Revise(3, [new StableToken("safety"), new StableToken("work")], 23);
        Require(revised.PolicyGeneration == 4 && revised.EffectiveFromStep == 23,
            "domain.participation.absence-policy: generation/effective step transition mismatch.");
        RequireReject(
            () => revised.Revise(3, [new StableToken("safety")], 24),
            "participation.absence-policy-stale-generation");
    }

    private static void VerifyParticipationDeathAndDetailFloor()
    {
        var binding = new ParticipationBindingV1(
            Id("0000000000000000000000000000d001"),
            Id("0000000000000000000000000000d002"),
            Id("0000000000000000000000000000d003"),
            ParticipationBindingStatusV1.Active,
            20,
            null,
            9);
        var deceased = binding.MarkResidentDeceased();
        var context = ParticipationControlContextFactoryV1.ForBinding(
            deceased, ParticipationControlAvailabilityV1.Unavailable, null, 30);
        Require(deceased.Status == ParticipationBindingStatusV1.ResidentDeceased &&
                deceased.ResidentId == binding.ResidentId &&
                context.Mode == ResidentControlModeV1.BoundResidentDeceased,
            "domain.participation.resident-death: death must stop control without resident reassignment.");

        var requirements = new[]
        {
            new ParticipationDetailRequirementV1(binding.ResidentId, 2, 10, null),
            new ParticipationDetailRequirementV1(binding.ResidentId, 0, 20, 40),
        };
        Require(ParticipationDetailFloorV1.Resolve(binding.ResidentId, 30, requirements) == 0,
            "domain.participation.detail-floor: strongest active detail floor mismatch.");
    }

    private static void VerifyDeliveryPerceptionBeliefSeparation()
    {
        var projection = new ResidentCognitionProjectionV1();
        var delivery = new ResidentInformationDeliveryV1(
            Id("0000000000000000000000000000e001"),
            Id("0000000000000000000000000000e002"),
            Id("0000000000000000000000000000e003"),
            new StableToken("claim.weather-rain"),
            40);
        projection.ReceiveDelivery(delivery);
        Require(projection.Beliefs.Count == 0,
            "domain.resident.belief.delivery-separation: delivery alone must not mutate belief.");

        var observation = projection.PerceiveDelivery(
            delivery.DeliveryId,
            Id("0000000000000000000000000000e004"),
            750_000,
            41);
        Require(projection.Beliefs.Count == 0,
            "domain.resident.belief.delivery-separation: perception construction alone must not mutate belief.");
        projection.ApplyPerception(observation);
        Require(projection.Beliefs.Count == 1 && projection.Beliefs[0].ConfidencePpm == 750_000,
            "domain.resident.belief.delivery-separation: explicit perception application mismatch.");
    }

    private static void VerifyPhysicalIntentBoundary()
    {
        var decision = new ResidentPhysicalActionDecisionV1(
            Id("0000000000000000000000000000f001"),
            Id("0000000000000000000000000000f002"),
            55,
            new StableToken("physical.intent.move"),
            Enumerable.Repeat((byte)7, 32).ToArray());
        var scope = new ConflictScopeV1(
            new StableToken("physical_built"),
            new StableToken("physical.presence"),
            decision.ResidentId.ToBytes(),
            new StableToken("pose"));
        var intent = ResidentPhysicalIntentFactoryV1.CreateMoveIntent(decision, 2, scope, 0);
        Require(intent.SourceDomain == new StableToken("resident") &&
                intent.TargetDomain == new StableToken("physical_built") &&
                intent.TargetPartitionId == new StableToken("physical.presence") &&
                intent.MutationKind == new StableToken("physical.intent.move"),
            "domain.resident.physical-separation: resident action must cross the PhysicalBuilt owner boundary as an intent.");
    }

    private static ParticipationBindRequestV1 BindRequest(
        OpaqueId128 bindingId,
        OpaqueId128 diverRef,
        OpaqueId128 residentId,
        uint generation,
        ulong effectiveStep,
        SameStepOrderKey orderKey)
        => new(
            new ParticipationBindingV1(
                bindingId,
                diverRef,
                residentId,
                ParticipationBindingStatusV1.Active,
                effectiveStep,
                null,
                generation),
            orderKey);

    private static ResidentGoapStateV1 State(string label)
        => new(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.ASCII.GetBytes(label))));

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void RequireReject(Action action, string expected)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expected)
        {
            return;
        }
        throw new InvalidOperationException($"Expected SIM-09 rejection: {expected}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
