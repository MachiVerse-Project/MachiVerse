using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.Spatial;

namespace MachiVerse.Simulation.Core.Domains;

public readonly record struct QuaternionQ30V1(int X, int Y, int Z, int W)
{
    public const int One = 1 << 30;

    public void ValidateCanonical()
    {
        if (X == 0 && Y == 0 && Z == 0 && W == 0)
            throw new InvalidDataException("spatial.frame-zero-quaternion");
        var firstNonZero = W != 0 ? W : X != 0 ? X : Y != 0 ? Y : Z;
        if (firstNonZero < 0)
            throw new InvalidDataException("spatial.frame-quaternion-sign-noncanonical");
    }

    public static QuaternionQ30V1 Identity => new(0, 0, 0, One);
}

public sealed record SpatialFrameNodeV1(
    StableToken FrameId,
    StableToken? ParentFrameId,
    Vec3MmV1 TranslationMm,
    QuaternionQ30V1 Rotation)
{
    public void Validate()
    {
        Rotation.ValidateCanonical();
        if (ParentFrameId is { } parent && parent == FrameId)
            throw new InvalidDataException("spatial.frame-self-parent");
    }
}

public sealed class SpatialFrameGraphV1
{
    private readonly SortedDictionary<string, SpatialFrameNodeV1> _byId;

    public SpatialFrameGraphV1(IEnumerable<SpatialFrameNodeV1> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        _byId = new SortedDictionary<string, SpatialFrameNodeV1>(StringComparer.Ordinal);
        foreach (var frame in frames)
        {
            ArgumentNullException.ThrowIfNull(frame);
            frame.Validate();
            if (!_byId.TryAdd(frame.FrameId.Value, frame))
                throw new InvalidDataException("spatial.frame-duplicate");
        }
        if (_byId.Count == 0)
            throw new ArgumentException("At least one Spatial frame is required.", nameof(frames));

        foreach (var frame in _byId.Values)
        {
            if (frame.ParentFrameId is { } parent && !_byId.ContainsKey(parent.Value))
                throw new InvalidDataException("spatial.frame-parent-missing");
            _ = ResolveRootToLeaf(frame.FrameId);
        }
    }

    public IReadOnlyList<SpatialFrameNodeV1> CanonicalFrames => _byId.Values.ToArray();

    public IReadOnlyList<SpatialFrameNodeV1> ResolveRootToLeaf(StableToken frameId)
    {
        if (!_byId.TryGetValue(frameId.Value, out var current))
            throw new KeyNotFoundException($"Unknown Spatial frame: {frameId.Value}");

        var reversed = new List<SpatialFrameNodeV1>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            if (!seen.Add(current.FrameId.Value))
                throw new InvalidDataException("spatial.frame-cycle");
            reversed.Add(current);
            if (current.ParentFrameId is not { } parent) break;
            current = _byId[parent.Value];
        }
        reversed.Reverse();
        return Array.AsReadOnly(reversed.ToArray());
    }
}

public sealed record SpatialScopeDescriptorV1(
    OpaqueId128 ScopeId,
    StableToken FrameId,
    OpaqueId128? ParentScopeId,
    ulong GeometryRevision)
{
    public void Validate()
    {
        if (ScopeId.IsZero) throw new InvalidDataException("spatial.scope-id-zero");
        if (ParentScopeId is { } parent && parent.IsZero)
            throw new InvalidDataException("spatial.parent-scope-id-zero");
        if (ParentScopeId == ScopeId)
            throw new InvalidDataException("spatial.scope-self-parent");
        if (GeometryRevision == 0)
            throw new InvalidDataException("spatial.scope-geometry-revision-zero");
    }
}

public sealed record AtmosphereCellStateV1(
    SpatialCellKeyV1 Cell,
    int PressurePascal,
    int TemperatureMilliKelvin,
    uint HumidityPpm,
    Vec3MmV1 WindMicrometrePerSecond,
    long WaterVaporMassGram,
    long LiquidWaterMassGram,
    IReadOnlyDictionary<StableToken, uint> GasCompositionPpb)
{
    public void Validate()
    {
        if (PressurePascal < 0 || TemperatureMilliKelvin < 0)
            throw new InvalidDataException("environment.atmosphere-scalar-range");
        if (HumidityPpm > 1_000_000)
            throw new InvalidDataException("environment.atmosphere-humidity-range");
        if (WaterVaporMassGram < 0 || LiquidWaterMassGram < 0)
            throw new InvalidDataException("environment.atmosphere-water-negative");
        ArgumentNullException.ThrowIfNull(GasCompositionPpb);
        foreach (var pair in GasCompositionPpb.OrderBy(static pair => pair.Key.Value, StringComparer.Ordinal))
        {
            if (pair.Value > 1_000_000_000)
                throw new InvalidDataException("environment.atmosphere-gas-ppb-range");
        }
    }
}

public static class SpatialEnvironmentKindRegistryV1
{
    public static IReadOnlyList<StableToken> SpatialEvents { get; } = Tokens(
        "spatial.geometry.carved", "spatial.geometry.filled", "spatial.geometry.deformed",
        "spatial.scope.created", "spatial.scope.retired", "spatial.containment.changed",
        "spatial.boundary.changed", "spatial.detail.changed");

    public static IReadOnlyList<StableToken> EnvironmentEvents { get; } = Tokens(
        "environment.resource.depleted", "environment.resource.extracted", "environment.weather.changed",
        "environment.precipitation.occurred", "environment.flood.started", "environment.flood.ended",
        "environment.ocean.condition-changed", "environment.erosion.occurred", "environment.deposition.occurred",
        "environment.ecosystem.population-changed", "environment.disease-vector.changed",
        "environment.contaminant.changed", "environment.hazard.started",
        "environment.hazard.intensity-changed", "environment.hazard.ended");

    public static IReadOnlyList<StableToken> SpatialTargetIntents { get; } = Tokens(
        "spatial.intent.geometry-carve", "spatial.intent.geometry-fill", "spatial.intent.geometry-deform",
        "spatial.intent.detail-promote", "spatial.intent.detail-demote");

    public static IReadOnlyList<StableToken> EnvironmentTargetIntents { get; } = Tokens(
        "environment.intent.resource-consume", "environment.intent.resource-return",
        "environment.intent.contaminant-add", "environment.intent.contaminant-remove",
        "environment.intent.hazard-driver-add", "environment.intent.water-exchange",
        "environment.intent.ecosystem-pressure");

    private static IReadOnlyList<StableToken> Tokens(params string[] values)
    {
        var tokens = values.Select(static value => new StableToken(value)).ToArray();
        if (tokens.Select(static token => token.Value).Distinct(StringComparer.Ordinal).Count() != tokens.Length)
            throw new InvalidOperationException("Spatial/Environment semantic registry contains duplicate token.");
        return Array.AsReadOnly(tokens);
    }
}
