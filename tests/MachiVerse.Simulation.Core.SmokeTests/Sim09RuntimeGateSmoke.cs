using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Domains.Participation;
using MachiVerse.Simulation.Core.Domains.ResidentParticipation;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim09RuntimeGateSmoke
{
    internal static async Task RunAsync()
    {
        var state = CreateWorldState();
        var frozen = StepInputFreezerV1.Freeze(
            state,
            new OperationSchedulerStateV1(state.Header.Step, null));
        var plan = StandardDomainExecutionPlanV1.Create();
        var residentId = Id("00000000000000000000000000010101");
        var decision = new ResidentPhysicalActionDecisionV1(
            Id("00000000000000000000000000010102"),
            residentId,
            state.Header.Step,
            new StableToken("physical.intent.move"),
            SHA256.HashData("sim09-runtime-move"u8));
        var scope = new ConflictScopeV1(
            new StableToken("physical_built"),
            new StableToken("physical.presence"),
            residentId.ToBytes(),
            new StableToken("pose"));
        var moveIntent = ResidentPhysicalIntentFactoryV1.CreateMoveIntent(decision, 2, scope, 0);

        var runtimes = plan.Entries.Select(entry => entry.DomainToken.Value switch
        {
            "resident" => (IDomainRuntimeV1)new ResidentDomainRuntimeV1(
                (_, _) => ValueTask.FromResult<IReadOnlyList<MutationIntentCandidateV1>>([moveIntent]),
                (context, _) => ValueTask.FromResult<IReadOnlyList<PartitionCandidateV1>>([
                    ResidentParticipationPartitionCandidateFactoryV1.CreateResident(
                        context.State,
                        "resident.goal_plan",
                        SHA256.HashData("sim09-resident-goal"u8))
                ])),
            "participation" => new ParticipationDomainRuntimeV1(
                static (_, _) => ValueTask.FromResult<IReadOnlyList<MutationIntentCandidateV1>>([]),
                (context, _) => ValueTask.FromResult<IReadOnlyList<PartitionCandidateV1>>([
                    ResidentParticipationPartitionCandidateFactoryV1.CreateParticipation(
                        context.State,
                        "participation.binding",
                        SHA256.HashData("sim09-participation-binding"u8))
                ])),
            _ => new NoOpRuntime(entry.DomainToken),
        }).ToArray();

        string[]? baseline = null;
        foreach (var workers in new[] { 1, 4, 8, 16 })
        {
            var outputs = await DomainRuntimeExecutorV1.ExecuteAsync(
                plan,
                state,
                frozen,
                runtimes,
                workers);
            var resident = outputs.Single(static output => output.DomainToken.Value == "resident");
            var participation = outputs.Single(static output => output.DomainToken.Value == "participation");

            Require(resident.LocalPartitionCandidates.Count == 1 &&
                    resident.LocalPartitionCandidates[0].PartitionId.Value == "resident.goal_plan" &&
                    resident.LocalPartitionCandidates[0].OwnerDomain.Value == "resident",
                "SIM-09 component gate: Resident owner-local candidate mismatch.");
            Require(participation.LocalPartitionCandidates.Count == 1 &&
                    participation.LocalPartitionCandidates[0].PartitionId.Value == "participation.binding" &&
                    participation.LocalPartitionCandidates[0].OwnerDomain.Value == "participation",
                "SIM-09 component gate: Participation owner-local candidate mismatch.");
            Require(resident.Intents.Count == 1 &&
                    resident.Intents[0].SourceDomain.Value == "resident" &&
                    resident.Intents[0].TargetDomain.Value == "physical_built" &&
                    resident.Intents[0].TargetPartitionId.Value == "physical.presence" &&
                    resident.Intents[0].MutationKind.Value == "physical.intent.move",
                "SIM-09 component gate: Resident physical effect must remain a canonical Physical/Built intent.");

            var snapshot = outputs.Select(output =>
                output.DomainToken.Value + ":" +
                string.Join(',', output.Intents.Select(static intent =>
                    intent.MutationKind.Value + "->" + intent.TargetPartitionId.Value + "=" + Convert.ToHexString(intent.SemanticPayloadDigest))) + ":" +
                string.Join(',', output.LocalPartitionCandidates.Select(static candidate =>
                    candidate.PartitionId.Value + "=" + Convert.ToHexString(candidate.CandidateDigest))))
                .ToArray();
            baseline ??= snapshot;
            Require(baseline.SequenceEqual(snapshot),
                "SIM-09 component gate: worker 1/4/8/16 changed semantic output.");
        }

        RequireReject(
            () => ResidentParticipationPartitionCandidateFactoryV1.CreateResident(
                state,
                "physical.presence",
                SHA256.HashData("sim09-resident-foreign"u8)),
            "domain.partition-candidate-foreign-owner");
        RequireReject(
            () => ResidentParticipationPartitionCandidateFactoryV1.CreateParticipation(
                state,
                "resident.behavior_state",
                SHA256.HashData("sim09-participation-foreign"u8)),
            "domain.partition-candidate-foreign-owner");

        VerifyParticipationInputPermutation();
    }

    private static void VerifyParticipationInputPermutation()
    {
        var diverA = Id("00000000000000000000000000010301");
        var diverB = Id("00000000000000000000000000010302");
        var resident = Id("00000000000000000000000000010303");
        var scopeDigest = SHA256.HashData("sim09-participation-scope"u8);
        var requestA = new ParticipationBindRequestV1(
            Id("00000000000000000000000000010311"),
            diverA,
            resident,
            1,
            1,
            new SameStepOrderKey(2, 40, scopeDigest, 0, Id("00000000000000000000000000010321")));
        var requestB = new ParticipationBindRequestV1(
            Id("00000000000000000000000000010312"),
            diverB,
            resident,
            1,
            1,
            new SameStepOrderKey(2, 40, scopeDigest, 1, Id("00000000000000000000000000010322")));

        var forward = ParticipationBindResolverV1.Resolve([], [requestB, requestA]);
        var reverse = ParticipationBindResolverV1.Resolve([], [requestA, requestB]);
        Require(forward.Accepted.Count == 1 &&
                reverse.Accepted.Count == 1 &&
                forward.Accepted[0].BindingId == requestA.BindingId &&
                reverse.Accepted[0].BindingId == requestA.BindingId &&
                forward.RejectedBindingIds.SequenceEqual(reverse.RejectedBindingIds),
            "SIM-09 component gate: bind-request input permutation changed canonical resolution.");
    }

    private static WorldStateV1 CreateWorldState()
    {
        var worldId = Id("00000000000000000000000000010200");
        var configDigest = SHA256.HashData("sim09-config"u8);
        var partitions = StandardDomainPartitionRegistry.Entries.Select(entry => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                entry,
                revision: 1,
                basisStep: 0,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(entry.PartitionId.Value)))));
        return new WorldStateV1(
            new WorldStateHeaderV1(
                worldId,
                step: 0,
                worldSeedDigest: new byte[32],
                configGeneration: 1,
                masterGeneration: 1,
                rateGeneration: 1),
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            configDigest);
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

    private sealed class NoOpRuntime(StableToken domainToken) : IDomainRuntimeV1
    {
        public StableToken DomainToken { get; } = domainToken;

        public ValueTask<DomainCandidateOutputV1> ExecuteAsync(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new DomainCandidateOutputV1(DomainToken, context.FrozenInput.BasisStep));
        }
    }
}
