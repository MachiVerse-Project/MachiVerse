using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Domains.ResidentParticipation;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim09RuntimeSmoke
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize()
        => RunAsync().GetAwaiter().GetResult();

    internal static async Task RunAsync()
    {
        var state = CreateWorldState();
        var frozen = StepInputFreezerV1.Freeze(state, new OperationSchedulerStateV1(state.Header.Step, null));
        var decision = new ResidentPhysicalActionDecisionV1(
            Id("0000000000000000000000000000f101"),
            Id("0000000000000000000000000000f102"),
            state.Header.Step,
            new StableToken("physical.intent.move"),
            SHA256.HashData("sim09-resident-move"u8));
        var scope = new ConflictScopeV1(
            new StableToken("physical_built"),
            new StableToken("physical.presence"),
            decision.ResidentId.ToBytes(),
            new StableToken("pose"));
        var intent = ResidentPhysicalIntentFactoryV1.CreateMoveIntent(decision, 2, scope, 0);

        IReadOnlyList<string>? baseline = null;
        foreach (var workers in new[] { 1, 4, 8, 16 })
        {
            var snapshot = await ResidentParticipationRuntimeGateV1.ExecuteSemanticSnapshotAsync(
                state,
                frozen,
                intent,
                workers);
            baseline ??= snapshot;
            Require(baseline.SequenceEqual(snapshot),
                "SIM-09 component gate: worker count changed Resident/Participation semantic output.");
        }

        RequireReject(
            () => ResidentParticipationPartitionCandidateFactoryV1.CreateResident(
                state,
                "participation.binding",
                SHA256.HashData("sim09-resident-foreign"u8)),
            "domain.partition-candidate-foreign-owner");
        RequireReject(
            () => ResidentParticipationPartitionCandidateFactoryV1.CreateParticipation(
                state,
                "resident.goal_plan",
                SHA256.HashData("sim09-participation-foreign"u8)),
            "domain.partition-candidate-foreign-owner");
    }

    private static WorldStateV1 CreateWorldState()
    {
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
                Id("0000000000000000000000000000f100"),
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
        try { action(); }
        catch (InvalidDataException ex) when (ex.Message == expected) { return; }
        throw new InvalidOperationException($"Expected SIM-09 rejection: {expected}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
