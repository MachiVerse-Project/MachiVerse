using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim10RuntimeGateSmoke
{
    internal static async Task RunAsync()
    {
        var state = CreateWorldState();
        var scheduler = new OperationSchedulerStateV1(state.Header.Step, null);
        var frozen = StepInputFreezerV1.Freeze(state, scheduler);
        var plan = StandardDomainExecutionPlanV1.Create();
        var societyPartitionIds = StandardDomainPartitionRegistry.Entries
            .Where(static entry => entry.OwnerDomain.Value == "society_economy")
            .Select(static entry => entry.PartitionId.Value)
            .OrderBy(static partitionId => partitionId, StringComparer.Ordinal)
            .ToArray();
        Require(societyPartitionIds.Length == 16,
            "SIM-10 component gate: standard Society/Economy partition count must remain 16.");

        var runtimes = plan.Entries.Select(entry => entry.DomainToken.Value switch
        {
            "society_economy" => (IDomainRuntimeV1)new SocietyEconomyDomainRuntimeV1(
                static (_, _) => ValueTask.FromResult<IReadOnlyList<MutationIntentCandidateV1>>([]),
                (context, _) => ValueTask.FromResult<IReadOnlyList<PartitionCandidateV1>>(
                    societyPartitionIds.Select(partitionId =>
                        SocietyEconomyPartitionCandidateFactoryV1.Create(
                            context.State,
                            partitionId,
                            SHA256.HashData(System.Text.Encoding.ASCII.GetBytes("sim10:" + partitionId))))
                    .ToArray())),
            _ => new NoOpRuntime(entry.DomainToken),
        }).ToArray();

        string[]? baseline = null;
        foreach (var workerCount in new[] { 1, 4, 8, 16 })
        {
            var outputs = await DomainRuntimeExecutorV1.ExecuteAsync(
                plan,
                state,
                frozen,
                runtimes,
                workerCount);
            var society = outputs.Single(static output => output.DomainToken.Value == "society_economy");
            Require(society.Intents.Count == 0,
                "SIM-10 component gate: Society/Economy fixture must not mutate foreign owner state directly.");
            Require(society.LocalPartitionCandidates.Count == societyPartitionIds.Length &&
                    society.LocalPartitionCandidates.All(static candidate => candidate.OwnerDomain.Value == "society_economy") &&
                    society.LocalPartitionCandidates.Select(static candidate => candidate.PartitionId.Value)
                        .OrderBy(static partitionId => partitionId, StringComparer.Ordinal)
                        .SequenceEqual(societyPartitionIds),
                "SIM-10 component gate: all 16 Society/Economy partitions must use owner-local candidates.");

            var snapshot = outputs.Select(output =>
                output.DomainToken.Value + ":" +
                string.Join(',', output.Intents.Select(static intent =>
                    intent.MutationKind.Value + "->" + intent.TargetPartitionId.Value + "=" + Convert.ToHexString(intent.SemanticPayloadDigest))) + ":" +
                string.Join(',', output.LocalPartitionCandidates.Select(static candidate =>
                    candidate.PartitionId.Value + "=" + Convert.ToHexString(candidate.CandidateDigest))))
                .ToArray();
            baseline ??= snapshot;
            Require(baseline.SequenceEqual(snapshot),
                "SIM-10 component gate: worker 1/4/8/16 changed Society/Economy semantic output.");
        }

        RequireReject(
            () => SocietyEconomyPartitionCandidateFactoryV1.Create(
                state,
                "physical.presence",
                SHA256.HashData("sim10-foreign"u8)),
            "domain.partition-candidate-foreign-owner");
        RequireReject(
            () => SocietyEconomyPartitionCandidateFactoryV1.Create(
                state,
                "resident.knowledge_belief",
                SHA256.HashData("sim10-resident-foreign"u8)),
            "domain.partition-candidate-foreign-owner");
    }

    private static WorldStateV1 CreateWorldState()
    {
        var worldId = OpaqueId128.Parse("00000000000000000000000000011600");
        var configDigest = SHA256.HashData("sim10-config"u8);
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
        throw new InvalidOperationException($"SIM-10 component gate expected rejection: {expected}");
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
