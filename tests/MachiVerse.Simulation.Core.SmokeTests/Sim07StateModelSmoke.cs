using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Domains.Spatial;

internal static class Sim07StateModelSmoke
{
    internal static void Run()
    {
        VerifyFrameAndScope();
        VerifyAtmospherePayload();
        VerifySemanticRegistry();
    }

    private static void VerifyFrameAndScope()
    {
        var root = new SpatialFrameNodeV1(
            new StableToken("world"),
            null,
            new Vec3MmV1(0, 0, 0),
            QuaternionQ30V1.Identity);
        var region = new SpatialFrameNodeV1(
            new StableToken("region.a"),
            new StableToken("world"),
            new Vec3MmV1(1_000, 2_000, 3_000),
            QuaternionQ30V1.Identity);
        var local = new SpatialFrameNodeV1(
            new StableToken("local.a"),
            new StableToken("region.a"),
            new Vec3MmV1(10, 20, 30),
            QuaternionQ30V1.Identity);

        var graphA = new SpatialFrameGraphV1([local, root, region]);
        var graphB = new SpatialFrameGraphV1([region, local, root]);
        Require(graphA.CanonicalFrames.Select(static frame => frame.FrameId.Value)
                .SequenceEqual(graphB.CanonicalFrames.Select(static frame => frame.FrameId.Value)),
            "domain.spatial.containment: frame registry order must be input-permutation independent.");
        Require(graphA.ResolveRootToLeaf(new StableToken("local.a"))
                .Select(static frame => frame.FrameId.Value)
                .SequenceEqual(["world", "region.a", "local.a"]),
            "SIM-07 frame/scope: root-to-local frame chain mismatch.");

        var scope = new SpatialScopeDescriptorV1(
            OpaqueId128.Parse("00000000000000000000000000007501"),
            new StableToken("local.a"),
            null,
            3);
        scope.Validate();

        RequireReject(
            () => new SpatialFrameGraphV1([
                new SpatialFrameNodeV1(new StableToken("a"), new StableToken("b"), default, QuaternionQ30V1.Identity),
                new SpatialFrameNodeV1(new StableToken("b"), new StableToken("a"), default, QuaternionQ30V1.Identity),
            ]),
            "spatial.frame-cycle");
        RequireReject(
            () => new QuaternionQ30V1(0, 0, 0, -QuaternionQ30V1.One).ValidateCanonical(),
            "spatial.frame-quaternion-sign-noncanonical");
    }

    private static void VerifyAtmospherePayload()
    {
        var cell = new AtmosphereCellStateV1(
            new SpatialCellKeyV1(0, 0, 0, 0),
            PressurePascal: 101_325,
            TemperatureMilliKelvin: 293_150,
            HumidityPpm: 500_000,
            WindMicrometrePerSecond: new Vec3MmV1(1_000, 0, 0),
            WaterVaporMassGram: 50,
            LiquidWaterMassGram: 5,
            GasCompositionPpb: new Dictionary<StableToken, uint>
            {
                [new StableToken("nitrogen")] = 780_000_000,
                [new StableToken("oxygen")] = 209_000_000,
            });
        cell.Validate();

        RequireReject(
            () => (cell with { HumidityPpm = 1_000_001 }).Validate(),
            "environment.atmosphere-humidity-range");
    }

    private static void VerifySemanticRegistry()
    {
        Require(SpatialEnvironmentKindRegistryV1.SpatialEvents.Count == 8,
            "SIM-07 registry: Spatial EventKind count mismatch.");
        Require(SpatialEnvironmentKindRegistryV1.EnvironmentEvents.Count == 15,
            "SIM-07 registry: Environment EventKind count mismatch.");
        Require(SpatialEnvironmentKindRegistryV1.SpatialTargetIntents.Count == 5,
            "SIM-07 registry: target-Spatial IntentKind count mismatch.");
        Require(SpatialEnvironmentKindRegistryV1.EnvironmentTargetIntents.Count == 7,
            "SIM-07 registry: target-Environment IntentKind count mismatch.");
        Require(SpatialEnvironmentKindRegistryV1.SpatialTargetIntents.Any(static token => token.Value == "spatial.intent.geometry-deform"),
            "SIM-07 registry: canonical Spatial geometry-deform intent missing.");
        Require(SpatialEnvironmentKindRegistryV1.EnvironmentEvents.Any(static token => token.Value == "environment.hazard.started"),
            "SIM-07 registry: canonical Environment hazard event missing.");
    }

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
        throw new InvalidOperationException($"Expected SIM-07 state-model rejection: {expectedMessage}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
