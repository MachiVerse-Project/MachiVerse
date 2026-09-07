using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Domains.Environment;
using MachiVerse.Simulation.Core.Domains.Spatial;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim07AdvancedSmoke
{
    internal static async Task RunAsync()
    {
        VerifySpatialIndexRebuild();
        VerifyGroundwaterJacobi();
        VerifyOceanConservation();
        VerifyErosionMaterialBoundary();
        VerifyEcologyAddressableRandom();
        VerifyContaminantMass();
        await VerifyDomainRuntimeIntegrationAsync();
    }

    private static void VerifySpatialIndexRebuild()
    {
        var idA = OpaqueId128.Parse("00000000000000000000000000007101");
        var idB = OpaqueId128.Parse("00000000000000000000000000007102");
        var a = new SpatialIndexEntryV1(
            idA,
            new AabbMmV1(new Vec3MmV1(-500, -500, -500), new Vec3MmV1(500, 500, 500)));
        var b = new SpatialIndexEntryV1(
            idB,
            new AabbMmV1(new Vec3MmV1(10_000, 0, 0), new Vec3MmV1(10_500, 500, 500)));

        var forward = HierarchicalAabbGridV1.Rebuild([a, b]);
        var reverse = HierarchicalAabbGridV1.Rebuild([b, a]);
        Require(CanonicalGridSnapshot(forward).SequenceEqual(CanonicalGridSnapshot(reverse)),
            "domain.spatial.index.rebuild: rebuilt index changed under source permutation.");

        var query = new AabbMmV1(new Vec3MmV1(-750, -750, -750), new Vec3MmV1(750, 750, 750));
        var candidates = forward.QueryCandidates(query);
        Require(candidates.SequenceEqual([idA]),
            "domain.spatial.index.rebuild: canonical query returned the wrong candidate set/order.");
        Require(HierarchicalAabbGridV1.StandardCellEdgesMm.SequenceEqual([1_000L, 8_000L, 64_000L, 512_000L, 4_096_000L]),
            "domain.spatial.index.rebuild: standard hierarchical grid levels changed.");
    }

    private static void VerifyGroundwaterJacobi()
    {
        var a = new SpatialCellKeyV1(2, 0, 0, 0);
        var b = new SpatialCellKeyV1(2, 1, 0, 0);
        var initial = new Dictionary<SpatialCellKeyV1, long> { [b] = 100, [a] = 0 };
        var edge = new GroundwaterConductanceEdgeV1(a, b, 500_000);

        var forward = GroundwaterJacobiV1.Solve(initial, [edge]);
        var reversedEndpoint = GroundwaterJacobiV1.Solve(initial, [new GroundwaterConductanceEdgeV1(b, a, 500_000)]);
        Require(forward[a] == 50 && forward[b] == 50,
            "domain.environment.groundwater.jacobi: 16-iteration golden vector mismatch.");
        Require(forward.SequenceEqual(reversedEndpoint),
            "domain.environment.groundwater.jacobi: edge orientation changed the result.");
    }

    private static void VerifyOceanConservation()
    {
        var a = new SpatialCellKeyV1(0, 0, 0, 0);
        var b = new SpatialCellKeyV1(0, 1, 0, 0);
        var frozen = new Dictionary<SpatialCellKeyV1, OceanCellStockV1>
        {
            [b] = new OceanCellStockV1(0, 0, 0, 0),
            [a] = new OceanCellStockV1(100, 10, 20, 30),
        };
        var flux = new OceanFluxEdgeV1(a, b, 25, 3, 5, 7);
        var result = OceanConservativeFluxV1.Apply(frozen, [flux]);
        Require(result[a] == new OceanCellStockV1(75, 7, 15, 23) &&
                result[b] == new OceanCellStockV1(25, 3, 5, 7),
            "domain.environment.ocean.flux: conserved multi-stock transfer mismatch.");
    }

    private static void VerifyErosionMaterialBoundary()
    {
        var a = new SpatialCellKeyV1(0, 0, 0, 0);
        var b = new SpatialCellKeyV1(0, 1, 0, 0);
        var material = new Dictionary<SpatialCellKeyV1, long> { [a] = 100, [b] = 0 };
        var spatialIntentId = OpaqueId128.Parse("00000000000000000000000000007201");
        var result = ErosionMaterialBoundaryV1.ApplyMaterialTransfer(
            material,
            [new ErosionMaterialTransferV1(a, b, 40, spatialIntentId)]);
        Require(result[a] == 60 && result[b] == 40 && result.Values.Sum() == 100,
            "domain.environment.erosion.material: material mass was not conserved.");
        RequireReject(
            () => ErosionMaterialBoundaryV1.ApplyMaterialTransfer(
                material,
                [new ErosionMaterialTransferV1(a, b, 1, OpaqueId128.Zero)]),
            "environment.erosion-spatial-intent-required");
    }

    private static void VerifyEcologyAddressableRandom()
    {
        var seed = new WorldSeed256(Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray());
        var worldId = OpaqueId128.Parse("00000000000000000000000000007300");
        var cohortA = new EcologyCohortStateV1(
            OpaqueId128.Parse("00000000000000000000000000007301"), 101, 1_000);
        var cohortB = new EcologyCohortStateV1(
            OpaqueId128.Parse("00000000000000000000000000007302"), 83, 800);

        var firstA = EcologyCohortTransitionV1.Advance(cohortA, 123_456, 12_345, 6_789, seed, worldId, 50);
        _ = EcologyCohortTransitionV1.Advance(cohortB, 123_456, 12_345, 6_789, seed, worldId, 50);
        var secondA = EcologyCohortTransitionV1.Advance(cohortA, 123_456, 12_345, 6_789, seed, worldId, 50);
        Require(firstA == secondA,
            "domain.environment.ecology.random: unrelated cohort evaluation changed addressable random outcome.");
    }

    private static void VerifyContaminantMass()
    {
        var a = new SpatialCellKeyV1(0, 0, 0, 0);
        var b = new SpatialCellKeyV1(0, 1, 0, 0);
        var c = new SpatialCellKeyV1(0, 2, 0, 0);
        var mass = new Dictionary<SpatialCellKeyV1, long> { [a] = 90, [b] = 10, [c] = 0 };
        var fluxes = new[]
        {
            new EnvironmentFluxEdgeV1(a, b, 20),
            new EnvironmentFluxEdgeV1(b, c, 5),
        };
        var forward = ContaminantTransportV1.ApplyMassFlux(mass, fluxes);
        var reverse = ContaminantTransportV1.ApplyMassFlux(mass, fluxes.Reverse());
        Require(forward.Values.Sum() == 100 && forward.OrderBy(static pair => pair.Key).SequenceEqual(reverse.OrderBy(static pair => pair.Key)),
            "domain.environment.contaminant.mass: contaminant mass/order invariance failed.");
    }

    private static async Task VerifyDomainRuntimeIntegrationAsync()
    {
        var state = CreateWorldState();
        var scheduler = new OperationSchedulerStateV1(state.Header.Step, null);
        var frozen = StepInputFreezerV1.Freeze(state, scheduler);
        var spatialToken = new StableToken("spatial");
        var environmentToken = new StableToken("environment");
        var scope = new ConflictScopeV1(
            spatialToken,
            new StableToken("spatial.terrain_geometry"),
            OpaqueId128.Parse("00000000000000000000000000007401").ToBytes(),
            new StableToken("geometry"));
        var environmentIntent = new MutationIntentCandidateV1(
            OpaqueId128.Parse("00000000000000000000000000007402"),
            phase: 3,
            sourceDomain: environmentToken,
            targetDomain: spatialToken,
            targetPartitionId: new StableToken("spatial.terrain_geometry"),
            basisStep: state.Header.Step,
            mutationKind: new StableToken("spatial.intent.geometry-deform"),
            targetScope: scope,
            semanticPriority: 0,
            resolutionMode: ConflictResolutionModeV1.CustomDeterministic,
            semanticPayloadDigest: SHA256.HashData("sim07-environment-to-spatial"u8));

        var runtimes = StandardDomainExecutionPlanV1.Create().Entries
            .Select(entry => entry.DomainToken.Value switch
            {
                "spatial" => (IDomainRuntimeV1)new SpatialDomainRuntimeV1(
                    static (_, _) => ValueTask.FromResult<IReadOnlyList<MutationIntentCandidateV1>>([]),
                    (context, _) => ValueTask.FromResult<IReadOnlyList<PartitionCandidateV1>>([
                        DomainOwnedPartitionCandidateFactoryV1.CreateSpatial(
                            context.State,
                            "spatial.terrain_geometry",
                            SHA256.HashData("spatial-change"u8))
                    ])),
                "environment" => new EnvironmentDomainRuntimeV1(
                    (_, _) => ValueTask.FromResult<IReadOnlyList<MutationIntentCandidateV1>>([environmentIntent]),
                    (context, _) => ValueTask.FromResult<IReadOnlyList<PartitionCandidateV1>>([
                        DomainOwnedPartitionCandidateFactoryV1.CreateEnvironment(
                            context.State,
                            "environment.atmosphere",
                            SHA256.HashData("environment-change"u8))
                    ])),
                _ => new NoOpRuntime(entry.DomainToken),
            })
            .ToArray();
        var plan = StandardDomainExecutionPlanV1.Create([
            new DomainSameStepDependencyV1(spatialToken, environmentToken),
        ]);

        string[]? baseline = null;
        IReadOnlyList<DomainCandidateOutputV1>? finalOutputs = null;
        foreach (var workers in new[] { 1, 4, 8, 16 })
        {
            var outputs = await DomainRuntimeExecutorV1.ExecuteAsync(plan, state, frozen, runtimes, workers);
            var snapshot = outputs.Select(output =>
                output.DomainToken.Value + ":" +
                string.Join(',', output.Intents.Select(static intent => intent.IntentId.ToString())) + ":" +
                string.Join(',', output.LocalPartitionCandidates.Select(static candidate => candidate.PartitionId.Value + "=" + Convert.ToHexString(candidate.CandidateDigest))))
                .ToArray();
            baseline ??= snapshot;
            Require(baseline.SequenceEqual(snapshot),
                "SIM-07 component gate: worker count changed Spatial/Environment domain output order/content.");
            finalOutputs = outputs;
        }

        Require(finalOutputs is not null, "SIM-07 runtime output missing.");
        var spatialOutput = finalOutputs.Single(output => output.DomainToken == spatialToken);
        var environmentOutput = finalOutputs.Single(output => output.DomainToken == environmentToken);
        Require(environmentOutput.Intents.Single().TargetDomain == spatialToken &&
                environmentOutput.Intents.Single().MutationKind.Value == "spatial.intent.geometry-deform",
            "SIM-07 component gate: Environment geometry effect must cross owner boundary as canonical Spatial intent.");
        Require(spatialOutput.LocalPartitionCandidates.Single().OwnerDomain == spatialToken &&
                environmentOutput.LocalPartitionCandidates.Single().OwnerDomain == environmentToken,
            "SIM-07 component gate: owner partition candidates must be emitted by their owning DomainRuntime.");

        var foreignSpatialCandidate = DomainOwnedPartitionCandidateFactoryV1.CreateSpatial(
            state, "spatial.terrain_geometry", SHA256.HashData("foreign-output"u8));
        RequireReject(
            () => new DomainCandidateOutputV1(
                environmentToken,
                state.Header.Step,
                localPartitionCandidates: [foreignSpatialCandidate]),
            "domain-output.foreign-partition-candidate");
        RequireReject(
            () => DomainOwnedPartitionCandidateFactoryV1.CreateEnvironment(
                state, "spatial.terrain_geometry", SHA256.HashData("foreign"u8)),
            "domain.partition-candidate-foreign-owner");

        var groups = DeterministicIntentMergerV1.GroupByConflictScope(
            finalOutputs.SelectMany(static output => output.Intents), state.Header.Step);
        var resolutions = groups.Select(group => DeterministicIntentMergerV1.ResolveCustomDeterministic(
            group,
            canonical => new ConflictGroupResolutionV1(
                canonical,
                canonical.OrderedCandidates.Select(static intent =>
                    new ResolvedMutationIntentV1(intent, MutationIntentDispositionV1.Effective))))).ToArray();
        var candidate = StepCandidateV1.Build(
            OpaqueId128.Parse("00000000000000000000000000007403"),
            state,
            frozen,
            finalOutputs,
            resolutions);
        Require(candidate.PartitionCandidates.Count == 2 && candidate.CommitDecision.CanCommit && !candidate.IsPublishable,
            "SIM-07 component gate: DomainRuntime owner candidates did not enter non-authoritative StepCandidate correctly.");
        Require(candidate.PartitionCandidates.Select(static item => item.OwnerDomain.Value)
                .SequenceEqual(["environment", "spatial"]),
            "SIM-07 component gate: StepCandidate partition candidates must be canonical by PartitionId.");
    }

    private static WorldStateV1 CreateWorldState()
    {
        var worldId = OpaqueId128.Parse("00000000000000000000000000007400");
        var configDigest = SHA256.HashData("sim07-config"u8);
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

    private static string[] CanonicalGridSnapshot(HierarchicalAabbGridV1 grid)
        => grid.CanonicalCells.Select(pair =>
            pair.Key + ":" + string.Join(',', pair.Value.Select(static id => id.ToString()))).ToArray();

    private static void RequireReject(Action action, string expectedMessage)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expectedMessage)
        {
            return;
        }
        throw new InvalidOperationException($"Expected SIM-07 rejection: {expectedMessage}");
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
