using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim08StateRuntimeSmoke
{
    internal static async Task RunAsync()
    {
        VerifySemanticRegistry();
        VerifyStateValidation();
        VerifyExclusiveFirstValidTransfer();
        await VerifyDomainRuntimeAsync();
    }

    private static void VerifySemanticRegistry()
    {
        Require(PhysicalBuiltSemanticRegistryV1.OperationKinds.Count == 10,
            "SIM-08 semantic registry must contain exactly 10 Physical/Built OperationKinds.");
        Require(PhysicalBuiltSemanticRegistryV1.EventKinds.Count == 18,
            "SIM-08 semantic registry must contain exactly 18 Physical/Built EventKinds.");
        Require(PhysicalBuiltSemanticRegistryV1.TargetIntentKinds.Count == 11,
            "SIM-08 semantic registry must contain exactly 11 target Physical/Built IntentKinds.");
        Require(PhysicalBuiltSemanticRegistryV1.OperationKinds.Any(static token => token.Value == "physical.item.transfer") &&
                PhysicalBuiltSemanticRegistryV1.EventKinds.Any(static token => token.Value == "physical.material-handoff.committed") &&
                PhysicalBuiltSemanticRegistryV1.TargetIntentKinds.Any(static token => token.Value == "physical.intent.material-handoff"),
            "SIM-08 semantic registry canonical Physical/Built tokens are missing.");
    }

    private static void VerifyStateValidation()
    {
        new PhysicalPresenceStateV1(
            Id("00000000000000000000000000009501"),
            Id("00000000000000000000000000009502"),
            new PositionMmV1(1, 2, 3),
            new VelocityUmPerSecondV1(4, 5, 6),
            new StableToken("supported"),
            1).Validate();

        new BuiltOpeningStateV1(
            Id("00000000000000000000000000009503"),
            new StableToken("open"),
            new StableToken("unlocked"),
            1).Validate();

        new ConstructionWorksiteStateV1(
            Id("00000000000000000000000000009504"),
            500_000,
            [Id("00000000000000000000000000009505")],
            1).Validate();

        var handoff = new PhysicalMaterialHandoffStateV1(
            Id("00000000000000000000000000009506"),
            Id("00000000000000000000000000009507"),
            1_000,
            Id("00000000000000000000000000009508"),
            Id("00000000000000000000000000009509"),
            MaterialHandoffStateV1.Prepared,
            1);
        handoff.Validate();
        var committed = handoff.Commit();
        Require(committed.State == MaterialHandoffStateV1.Committed && committed.Revision == 2,
            "SIM-08 material handoff Prepared->Committed revision transition mismatch.");

        RequireReject(
            () => new PhysicalContainmentStateV1(
                Id("00000000000000000000000000009510"),
                Id("00000000000000000000000000009510"),
                1).Validate(),
            "physical.containment-self");
    }

    private static void VerifyExclusiveFirstValidTransfer()
    {
        var item = Id("00000000000000000000000000009601");
        var source = Id("00000000000000000000000000009602");
        var wrongSource = Id("00000000000000000000000000009603");
        var targetA = Id("00000000000000000000000000009604");
        var targetB = Id("00000000000000000000000000009605");
        var digest = new byte[32];

        var invalidEarlier = new PhysicalItemTransferRequestV1(
            item,
            wrongSource,
            targetB,
            new SameStepOrderKey(2, 30, digest, -10, Id("00000000000000000000000000009610")));
        var validWinner = new PhysicalItemTransferRequestV1(
            item,
            source,
            targetA,
            new SameStepOrderKey(2, 30, digest, 0, Id("00000000000000000000000000009611")));
        var validLoser = new PhysicalItemTransferRequestV1(
            item,
            source,
            targetB,
            new SameStepOrderKey(2, 30, digest, 1, Id("00000000000000000000000000009612")));
        var basis = new Dictionary<OpaqueId128, OpaqueId128> { [item] = source };

        var forward = PhysicalItemTransferResolverV1.Resolve(
            basis,
            [validLoser, invalidEarlier, validWinner]);
        var reverse = PhysicalItemTransferResolverV1.Resolve(
            basis,
            new[] { validWinner, invalidEarlier, validLoser }.Reverse());

        Require(forward.Decisions.Count == 1 &&
                forward.Decisions[0].WinningIntentId == validWinner.OrderKey.IntentId &&
                forward.FinalLocations[item] == targetA,
            "domain.physical.item-transfer-exclusive: exclusive_first_valid winner mismatch.");
        Require(reverse.Decisions.SequenceEqual(forward.Decisions) &&
                reverse.FinalLocations.OrderBy(static pair => pair.Key).SequenceEqual(
                    forward.FinalLocations.OrderBy(static pair => pair.Key)),
            "domain.physical.item-transfer-exclusive: input permutation changed transfer result.");
    }

    private static async Task VerifyDomainRuntimeAsync()
    {
        var state = CreateWorldState();
        var scheduler = new OperationSchedulerStateV1(state.Header.Step, null);
        var frozen = StepInputFreezerV1.Freeze(state, scheduler);
        var plan = StandardDomainExecutionPlanV1.Create();

        var runtimes = plan.Entries.Select(entry => entry.DomainToken.Value switch
        {
            "physical_built" => (IDomainRuntimeV1)new PhysicalBuiltDomainRuntimeV1(
                static (_, _) => ValueTask.FromResult<IReadOnlyList<MutationIntentCandidateV1>>([]),
                (context, _) => ValueTask.FromResult<IReadOnlyList<PartitionCandidateV1>>([
                    PhysicalBuiltPartitionCandidateFactoryV1.Create(
                        context.State,
                        "physical.container_location",
                        SHA256.HashData("sim08-physical-location-change"u8))
                ])),
            _ => new NoOpRuntime(entry.DomainToken),
        }).ToArray();

        string[]? baseline = null;
        foreach (var workers in new[] { 1, 4, 8, 16 })
        {
            var outputs = await DomainRuntimeExecutorV1.ExecuteAsync(plan, state, frozen, runtimes, workers);
            var physical = outputs.Single(static output => output.DomainToken.Value == "physical_built");
            Require(physical.LocalPartitionCandidates.Count == 1 &&
                    physical.LocalPartitionCandidates[0].PartitionId.Value == "physical.container_location" &&
                    physical.LocalPartitionCandidates[0].OwnerDomain.Value == "physical_built",
                "SIM-08 component gate: PhysicalBuilt DomainRuntime owner candidate mismatch.");

            var snapshot = outputs.Select(output =>
                output.DomainToken.Value + ":" +
                string.Join(',', output.LocalPartitionCandidates.Select(static candidate =>
                    candidate.PartitionId.Value + "=" + Convert.ToHexString(candidate.CandidateDigest))))
                .ToArray();
            baseline ??= snapshot;
            Require(baseline.SequenceEqual(snapshot),
                "SIM-08 component gate: worker count changed PhysicalBuilt semantic output.");
        }

        RequireReject(
            () => PhysicalBuiltPartitionCandidateFactoryV1.Create(
                state,
                "spatial.terrain_geometry",
                SHA256.HashData("sim08-foreign"u8)),
            "domain.partition-candidate-foreign-owner");
    }

    private static WorldStateV1 CreateWorldState()
    {
        var worldId = Id("00000000000000000000000000009700");
        var configDigest = SHA256.HashData("sim08-config"u8);
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

    private static OpaqueId128 Id(string text) => OpaqueId128.Parse(text);

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
        throw new InvalidOperationException($"Expected SIM-08 rejection: {expected}");
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
