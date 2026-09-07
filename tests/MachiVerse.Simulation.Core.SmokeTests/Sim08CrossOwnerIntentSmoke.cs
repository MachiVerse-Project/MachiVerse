using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Runtime;

internal static class Sim08CrossOwnerIntentSmoke
{
    internal static void Run()
    {
        var physicalBuilt = new StableToken("physical_built");
        var spatial = new StableToken("spatial");
        var scope = new ConflictScopeV1(
            spatial,
            new StableToken("spatial.terrain_geometry"),
            OpaqueId128.Parse("00000000000000000000000000009801").ToBytes(),
            new StableToken("geometry"));

        var intent = PhysicalBuiltCrossDomainIntentFactoryV1.CreateSpatialTerrainMutation(
            OpaqueId128.Parse("00000000000000000000000000009802"),
            phase: 3,
            basisStep: 42,
            mutationKind: new StableToken("spatial.intent.geometry-carve"),
            targetScope: scope,
            semanticPriority: 0,
            semanticPayloadDigest: SHA256.HashData("sim08-physical-built-to-spatial"u8));

        var output = new DomainCandidateOutputV1(
            physicalBuilt,
            basisStep: 42,
            intents: [intent]);

        var emitted = output.Intents.Single();
        Require(emitted.SourceDomain == physicalBuilt &&
                emitted.TargetDomain == spatial &&
                emitted.TargetPartitionId.Value == "spatial.terrain_geometry" &&
                emitted.MutationKind.Value == "spatial.intent.geometry-carve" &&
                emitted.ResolutionMode == ConflictResolutionModeV1.CustomDeterministic,
            "SIM-08 component gate: terrain geometry effects must cross the owner boundary as a canonical Spatial MutationIntent.");

        RequireReject(
            () => PhysicalBuiltCrossDomainIntentFactoryV1.CreateSpatialTerrainMutation(
                OpaqueId128.Parse("00000000000000000000000000009803"),
                phase: 3,
                basisStep: 42,
                mutationKind: new StableToken("spatial.intent.detail-promote"),
                targetScope: scope,
                semanticPriority: 0,
                semanticPayloadDigest: new byte[32]),
            "physical.spatial-intent-kind-invalid");
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
        throw new InvalidOperationException($"Expected SIM-08 rejection: {expected}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
