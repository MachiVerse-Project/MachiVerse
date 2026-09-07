using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim12RuntimeGateSmoke
{
    internal static async Task RunAsync()
    {
        var state = CreateWorldState();
        var scheduler = new OperationSchedulerStateV1(state.Header.Step, null);
        var frozen = StepInputFreezerV1.Freeze(state, scheduler);
        var plan = StandardDomainExecutionPlanV1.Create();
        var infrastructurePartitionIds = StandardDomainPartitionRegistry.Entries
            .Where(static entry => entry.OwnerDomain.Value == "infrastructure_information")
            .Select(static entry => entry.PartitionId.Value)
            .OrderBy(static partitionId => partitionId, StringComparer.Ordinal)
            .ToArray();

        Require(infrastructurePartitionIds.Length == 14,
            "SIM-12 component gate: standard Infrastructure/Information partition count must remain 14.");

        var runtimes = plan.Entries.Select(entry => entry.DomainToken.Value switch
        {
            "infrastructure_information" => (IDomainRuntimeV1)new InfrastructureInformationDomainRuntimeV1(
                static (_, _) => ValueTask.FromResult<IReadOnlyList<MutationIntentCandidateV1>>([]),
                (context, _) => ValueTask.FromResult<IReadOnlyList<PartitionCandidateV1>>(
                    infrastructurePartitionIds.Select(partitionId =>
                        InfrastructureInformationPartitionCandidateFactoryV1.Create(
                            context.State,
                            partitionId,
                            SHA256.HashData(System.Text.Encoding.ASCII.GetBytes("sim12:" + partitionId))))
                    .ToArray())),
            _ => new NoOpRuntime(entry.DomainToken),
        }).ToArray();

        string[]? baseline = null;
        foreach (var workerCount in new[] { 1, 4, 8, 16 })
        {
            var outputs = await DomainRuntimeExecutorV1.ExecuteAsync(plan, state, frozen, runtimes, workerCount);
            var infrastructure = outputs.Single(static output => output.DomainToken.Value == "infrastructure_information");
            Require(infrastructure.Intents.Count == 0,
                "SIM-12 component gate: Infrastructure/Information fixture must not directly mutate foreign owner state.");
            Require(infrastructure.LocalPartitionCandidates.Count == infrastructurePartitionIds.Length &&
                    infrastructure.LocalPartitionCandidates.All(static candidate => candidate.OwnerDomain.Value == "infrastructure_information") &&
                    infrastructure.LocalPartitionCandidates.Select(static candidate => candidate.PartitionId.Value)
                        .OrderBy(static partitionId => partitionId, StringComparer.Ordinal)
                        .SequenceEqual(infrastructurePartitionIds),
                "SIM-12 component gate: all 14 Infrastructure/Information partitions must use owner-local candidates.");

            var snapshot = outputs.Select(output =>
                output.DomainToken.Value + ":" +
                string.Join(',', output.Intents.Select(static intent =>
                    intent.MutationKind.Value + "->" + intent.TargetPartitionId.Value + "=" + Convert.ToHexString(intent.SemanticPayloadDigest))) + ":" +
                string.Join(',', output.LocalPartitionCandidates.Select(static candidate =>
                    candidate.PartitionId.Value + "=" + Convert.ToHexString(candidate.CandidateDigest))))
                .ToArray();
            baseline ??= snapshot;
            Require(baseline.SequenceEqual(snapshot),
                "SIM-12 component gate: worker 1/4/8/16 changed Infrastructure/Information semantic output.");
        }

        RequireReject(
            () => InfrastructureInformationPartitionCandidateFactoryV1.Create(
                state, "physical.presence", SHA256.HashData("sim12-physical-foreign"u8)),
            "domain.partition-candidate-foreign-owner");
        RequireReject(
            () => InfrastructureInformationPartitionCandidateFactoryV1.Create(
                state, "resident.knowledge_belief", SHA256.HashData("sim12-resident-foreign"u8)),
            "domain.partition-candidate-foreign-owner");
    }

    private static WorldStateV1 CreateWorldState()
    {
        var worldId = OpaqueId128.Parse("00000000000000000000000000012c00");
        var configDigest = SHA256.HashData("sim12-config"u8);
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

        throw new InvalidOperationException($"SIM-12 component gate expected rejection: {expected}");
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
