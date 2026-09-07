using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Domains;

public static class PhysicalBuiltCrossDomainIntentFactoryV1
{
    private static readonly StableToken SourceDomain = new("physical_built");
    private static readonly StableToken SpatialDomain = new("spatial");
    private static readonly StableToken TerrainPartition = new("spatial.terrain_geometry");
    private static readonly HashSet<string> AllowedTerrainMutationKinds = new(StringComparer.Ordinal)
    {
        "spatial.intent.geometry-carve",
        "spatial.intent.geometry-fill",
        "spatial.intent.geometry-deform",
    };

    public static MutationIntentCandidateV1 CreateSpatialTerrainMutation(
        OpaqueId128 intentId,
        byte phase,
        ulong basisStep,
        StableToken mutationKind,
        ConflictScopeV1 targetScope,
        int semanticPriority,
        ReadOnlySpan<byte> semanticPayloadDigest)
    {
        ArgumentNullException.ThrowIfNull(targetScope);
        if (!AllowedTerrainMutationKinds.Contains(mutationKind.Value))
            throw new InvalidDataException("physical.spatial-intent-kind-invalid");
        if (targetScope.Domain != SpatialDomain || targetScope.TargetKind != TerrainPartition)
            throw new InvalidDataException("physical.spatial-intent-scope-mismatch");

        return new MutationIntentCandidateV1(
            intentId,
            phase,
            SourceDomain,
            SpatialDomain,
            TerrainPartition,
            basisStep,
            mutationKind,
            targetScope,
            semanticPriority,
            ConflictResolutionModeV1.CustomDeterministic,
            semanticPayloadDigest);
    }
}
