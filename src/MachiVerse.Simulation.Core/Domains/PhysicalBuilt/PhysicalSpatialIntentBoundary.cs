using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Domains.PhysicalBuilt;

/// <summary>
/// Canonical cross-owner boundary for Physical/Built work that requires a natural-terrain mutation.
/// Physical/Built never emits an owner-local candidate for spatial.terrain_geometry; it emits one of
/// the Phase-4 Spatial geometry intents and lets the Spatial owner validate/conflict-resolve it.
/// </summary>
public static class PhysicalSpatialIntentBoundaryV1
{
    private static readonly StableToken SourceDomain = new("physical_built");
    private static readonly StableToken TargetDomain = new("spatial");
    private static readonly StableToken TargetPartition = new("spatial.terrain_geometry");

    private static readonly IReadOnlySet<string> AllowedMutationKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "spatial.intent.geometry-carve",
        "spatial.intent.geometry-fill",
        "spatial.intent.geometry-deform",
    };

    public static MutationIntentCandidateV1 CreateTerrainMutationIntent(
        OpaqueId128 intentId,
        byte phase,
        ulong basisStep,
        StableToken mutationKind,
        ConflictScopeV1 targetScope,
        int semanticPriority,
        ReadOnlySpan<byte> semanticPayloadDigest)
    {
        ArgumentNullException.ThrowIfNull(targetScope);
        if (!AllowedMutationKinds.Contains(mutationKind.Value))
            throw new InvalidDataException("physical.spatial-intent-kind-not-allowed");
        if (targetScope.Domain != TargetDomain)
            throw new InvalidDataException("physical.spatial-intent-scope-domain-mismatch");

        return new MutationIntentCandidateV1(
            intentId,
            phase,
            SourceDomain,
            TargetDomain,
            TargetPartition,
            basisStep,
            mutationKind,
            targetScope,
            semanticPriority,
            ConflictResolutionModeV1.CustomDeterministic,
            semanticPayloadDigest);
    }
}
