using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Domains;

public static class PhysicalBuiltCrossDomainIntentFactoryV1
{
    private static readonly StableToken SourceDomain = new("physical_built");
    private static readonly StableToken SpatialDomain = new("spatial");
    private static readonly StableToken TerrainPartition = new("spatial.terrain_geometry");
    private static readonly IReadOnlyList<DomainIntentCapabilityV1> Capabilities = Array.AsReadOnly(new[]
    {
        new DomainIntentCapabilityV1(SourceDomain, SpatialDomain, TerrainPartition, new StableToken("spatial.intent.geometry-carve")),
        new DomainIntentCapabilityV1(SourceDomain, SpatialDomain, TerrainPartition, new StableToken("spatial.intent.geometry-deform")),
        new DomainIntentCapabilityV1(SourceDomain, SpatialDomain, TerrainPartition, new StableToken("spatial.intent.geometry-fill")),
    });
    private static readonly HashSet<string> AllowedTerrainMutationKinds = Capabilities
        .Select(static value => value.IntentKind.Value)
        .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<DomainIntentCapabilityV1> EmittedCapabilities => Capabilities;

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
