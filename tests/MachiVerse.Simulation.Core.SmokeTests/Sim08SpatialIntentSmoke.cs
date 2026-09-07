using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.Runtime;

internal static class Sim08SpatialIntentSmoke
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Run()
    {
        VerifyCanonicalTerrainIntent("spatial.intent.geometry-carve");
        VerifyCanonicalTerrainIntent("spatial.intent.geometry-fill");
        VerifyCanonicalTerrainIntent("spatial.intent.geometry-deform");
        VerifyNonTerrainIntentRejected();
        VerifyWrongScopeOwnerRejected();
    }

    private static void VerifyCanonicalTerrainIntent(string kind)
    {
        var intent = PhysicalSpatialIntentBoundaryV1.CreateTerrainMutationIntent(
            OpaqueId128.Parse("00000000000000000000000000009a01"),
            phase: 1,
            basisStep: 42,
            mutationKind: new StableToken(kind),
            targetScope: SpatialScope(),
            semanticPriority: 0,
            semanticPayloadDigest: SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(kind)));

        Require(intent.SourceDomain.Value == "physical_built",
            "SIM-08 Spatial boundary: source domain must remain physical_built.");
        Require(intent.TargetDomain.Value == "spatial" && intent.TargetPartitionId.Value == "spatial.terrain_geometry",
            "SIM-08 Spatial boundary: natural terrain authority must remain Spatial.");
        Require(intent.MutationKind.Value == kind && intent.ResolutionMode == ConflictResolutionModeV1.CustomDeterministic,
            "SIM-08 Spatial boundary: canonical geometry intent semantics mismatch.");
    }

    private static void VerifyNonTerrainIntentRejected()
    {
        RequireReject(
            () => PhysicalSpatialIntentBoundaryV1.CreateTerrainMutationIntent(
                OpaqueId128.Parse("00000000000000000000000000009a02"),
                1,
                42,
                new StableToken("spatial.intent.detail-promote"),
                SpatialScope(),
                0,
                new byte[32]),
            "physical.spatial-intent-kind-not-allowed");
    }

    private static void VerifyWrongScopeOwnerRejected()
    {
        var physicalScope = new ConflictScopeV1(
            new StableToken("physical_built"),
            new StableToken("fixture.scope"),
            OpaqueId128.Parse("00000000000000000000000000009aff").ToBytes(),
            new StableToken("fixture.resource"));
        RequireReject(
            () => PhysicalSpatialIntentBoundaryV1.CreateTerrainMutationIntent(
                OpaqueId128.Parse("00000000000000000000000000009a03"),
                1,
                42,
                new StableToken("spatial.intent.geometry-carve"),
                physicalScope,
                0,
                new byte[32]),
            "physical.spatial-intent-scope-domain-mismatch");
    }

    private static ConflictScopeV1 SpatialScope()
        => new(
            new StableToken("spatial"),
            new StableToken("fixture.terrain-scope"),
            OpaqueId128.Parse("00000000000000000000000000009aff").ToBytes(),
            new StableToken("fixture.geometry"));

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
