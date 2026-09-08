using MachiVerse.View.State;

namespace MachiVerse.View.Rendering;

public static class ScenePrimitiveKinds
{
    public const string Terrain = "terrain";
    public const string Built = "built";
    public const string Presence = "presence";

    public static bool IsKnown(string value)
        => value is Terrain or Built or Presence;
}

public static class SceneMaterialProfiles
{
    public const string Terrain = "terrain";
    public const string Built = "built";
    public const string Presence = "presence";

    public static bool IsKnown(string value)
        => value is Terrain or Built or Presence;
}

public sealed record SceneVector3(double X, double Y, double Z);

public sealed record ScenePrimitive(
    string PrimitiveId,
    string Kind,
    SceneVector3 Position,
    SceneVector3 Scale,
    double LodMinDistance,
    double LodMaxDistance,
    string MaterialProfile);

public interface ISceneProjectionAdapter
{
    string RecordSchemaId { get; }

    IEnumerable<ScenePrimitive> Project(ConfirmedProjectionRecord record);
}

public sealed class SceneProjectionAdapterRegistry
{
    private readonly IReadOnlyDictionary<string, ISceneProjectionAdapter> _adapters;

    public static SceneProjectionAdapterRegistry Empty { get; } = new(Array.Empty<ISceneProjectionAdapter>());

    public SceneProjectionAdapterRegistry(IEnumerable<ISceneProjectionAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        var dictionary = new Dictionary<string, ISceneProjectionAdapter>(StringComparer.Ordinal);
        foreach (var adapter in adapters)
        {
            ArgumentNullException.ThrowIfNull(adapter);
            if (string.IsNullOrWhiteSpace(adapter.RecordSchemaId))
                throw new ArgumentException("Scene projection adapter schema id is required.", nameof(adapters));
            if (!dictionary.TryAdd(adapter.RecordSchemaId, adapter))
                throw new InvalidDataException($"view.scene-projection.duplicate-adapter:{adapter.RecordSchemaId}");
        }

        _adapters = dictionary;
    }

    public IReadOnlyList<ScenePrimitive> Project(IReadOnlyDictionary<ProjectionRecordKey, ConfirmedProjectionRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var primitives = new List<ScenePrimitive>();
        var primitiveIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in records
                     .OrderBy(static item => item.Key.SchemaId, StringComparer.Ordinal)
                     .ThenBy(static item => item.Key.RecordIdHex, StringComparer.Ordinal))
        {
            if (!_adapters.TryGetValue(item.Value.SchemaId, out var adapter))
                continue;

            var projected = adapter.Project(item.Value)
                ?? throw new InvalidDataException($"view.scene-projection.adapter-null:{item.Value.SchemaId}");

            foreach (var primitive in projected)
            {
                ValidatePrimitive(primitive);
                if (!primitiveIds.Add(primitive.PrimitiveId))
                    throw new InvalidDataException($"view.scene-projection.duplicate-primitive:{primitive.PrimitiveId}");
                primitives.Add(primitive);
            }
        }

        return primitives
            .OrderBy(static primitive => primitive.Kind, StringComparer.Ordinal)
            .ThenBy(static primitive => primitive.PrimitiveId, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidatePrimitive(ScenePrimitive primitive)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        if (string.IsNullOrWhiteSpace(primitive.PrimitiveId))
            throw new InvalidDataException("view.scene-projection.primitive-id-empty");
        if (!ScenePrimitiveKinds.IsKnown(primitive.Kind))
            throw new InvalidDataException($"view.scene-projection.unknown-primitive-kind:{primitive.Kind}");
        if (!SceneMaterialProfiles.IsKnown(primitive.MaterialProfile))
            throw new InvalidDataException($"view.scene-projection.unknown-material-profile:{primitive.MaterialProfile}");

        ValidateVector(primitive.Position, allowZero: true, "position");
        ValidateVector(primitive.Scale, allowZero: false, "scale");

        if (!double.IsFinite(primitive.LodMinDistance) || primitive.LodMinDistance < 0)
            throw new InvalidDataException("view.scene-projection.invalid-lod-min");
        if (!double.IsFinite(primitive.LodMaxDistance) || primitive.LodMaxDistance <= primitive.LodMinDistance)
            throw new InvalidDataException("view.scene-projection.invalid-lod-max");
    }

    private static void ValidateVector(SceneVector3 value, bool allowZero, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Z))
            throw new InvalidDataException($"view.scene-projection.invalid-{field}");
        if (!allowZero && (value.X <= 0 || value.Y <= 0 || value.Z <= 0))
            throw new InvalidDataException($"view.scene-projection.invalid-{field}");
    }
}
