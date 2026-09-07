using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.ResidentParticipation;

internal static class Sim09ResidentCoreSmoke
{
    internal static void Run()
    {
        VerifyLifecycle();
        VerifyHealthBounds();
        VerifyDiseaseRandom();
        VerifyGoalTie();
        VerifySkillCurve();
    }

    private static void VerifyLifecycle()
    {
        var residentId = Id("0000000000000000000000000000a001");
        var developing = ResidentLifecycleStateV1.CreateDeveloping(residentId);
        var alive = developing.MarkAlive(10).Validate();
        var deceased = alive.MarkDeceased(20).Validate();

        Require(alive.Lifecycle.Value == "alive" && alive.BirthStep == 10 && alive.DeathStep is null,
            "domain.resident.lifecycle: developing -> alive transition mismatch.");
        Require(deceased.Lifecycle.Value == "deceased" && deceased.BirthStep == 10 && deceased.DeathStep == 20,
            "domain.resident.lifecycle: alive -> deceased transition mismatch.");
        RequireReject(() => deceased.MarkAlive(30), "resident.lifecycle-transition-invalid");
        RequireReject(() => developing.MarkDeceased(5), "resident.lifecycle-transition-invalid");
    }

    private static void VerifyHealthBounds()
    {
        Require(new ResidentPpmV1(0).Value == 0 && new ResidentPpmV1(1_000_000).Value == 1_000_000,
            "domain.resident.health.bounds: ppm endpoints must be representable.");
        Require(new ResidentPpmV1(500_000).AddChecked(250_000).Value == 750_000,
            "domain.resident.health.bounds: checked ppm update mismatch.");
        RequireReject(() => _ = new ResidentPpmV1(1_000_001), "resident.ppm-out-of-range");
        RequireReject(() => _ = new ResidentPpmV1(1_000_000).AddChecked(1), "resident.ppm-out-of-range");
        RequireReject(() => _ = new ResidentPpmV1(0).AddChecked(-1), "resident.ppm-out-of-range");
    }

    private static void VerifyDiseaseRandom()
    {
        var worldId = Id("0000000000000000000000000000a010");
        var residentId = Id("0000000000000000000000000000a011");
        var conditionA = Id("0000000000000000000000000000a012");
        var conditionB = Id("0000000000000000000000000000a013");
        var seed = new WorldSeed256(Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray());
        var eventKind = new StableToken("disease-progress");

        var first = ResidentDiseaseRandomV1.Occurs(seed, worldId, residentId, conditionA, 77, eventKind, 500_000);
        var repeat = ResidentDiseaseRandomV1.Occurs(seed, worldId, residentId, conditionA, 77, eventKind, 500_000);
        Require(first == repeat,
            "domain.resident.disease.random: identical addressable random context must repeat exactly.");

        var forward = new[] { conditionA, conditionB }.ToDictionary(
            static condition => condition,
            condition => ResidentDiseaseRandomV1.Occurs(seed, worldId, residentId, condition, 77, eventKind, 500_000));
        var reverse = new[] { conditionB, conditionA }.ToDictionary(
            static condition => condition,
            condition => ResidentDiseaseRandomV1.Occurs(seed, worldId, residentId, condition, 77, eventKind, 500_000));
        Require(forward.OrderBy(static pair => pair.Key).SequenceEqual(reverse.OrderBy(static pair => pair.Key)),
            "domain.resident.disease.random: condition iteration order changed per-condition random result.");
        Require(!ResidentDiseaseRandomV1.Occurs(seed, worldId, residentId, conditionA, 77, eventKind, 0) &&
                ResidentDiseaseRandomV1.Occurs(seed, worldId, residentId, conditionA, 77, eventKind, 1_000_000),
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

    private static void VerifySkillCurve()
    {
        Require(ResidentSkillProgressionV1.ApplyPractice(0, 100_000) == 100_000,
            "domain.resident.skill.curve: zero-skill learning vector mismatch.");
        Require(ResidentSkillProgressionV1.ApplyPractice(500_000, 100_000) == 550_000,
            "domain.resident.skill.curve: midpoint learning vector mismatch.");
        Require(ResidentSkillProgressionV1.ApplyPractice(999_995, 500_000) == 999_997,
            "domain.resident.skill.curve: round-ties-to-even learning vector mismatch.");
        Require(ResidentSkillProgressionV1.ApplyPractice(999_999, 1_000_000) == 1_000_000,
            "domain.resident.skill.curve: final bounded learning increment mismatch.");
        RequireReject(() => _ = ResidentSkillProgressionV1.ApplyPractice(1_000_001, 1), "resident.ppm-out-of-range");
    }

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
