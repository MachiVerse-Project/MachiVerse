using System.Globalization;
using MachiVerse.View.State;

namespace MachiVerse.View.Rendering;

public sealed record SceneProjectionRecord(
    string RecordSchemaId,
    string RecordIdHex,
    string RecordRevision);

public sealed record SceneProjectionModel(
    string BasisStep,
    string ContinuityTokenHex,
    string ProjectionSchemaDigestHex,
    IReadOnlyList<SceneProjectionRecord> Records,
    IReadOnlyList<ScenePrimitive> Primitives)
{
    public static SceneProjectionModel FromConfirmed(
        ConfirmedWorldSnapshot snapshot,
        SceneProjectionAdapterRegistry? adapters = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ContinuityToken.Length == 0)
            throw new InvalidDataException("view.scene-projection.missing-continuity-token");
        if (snapshot.ProjectionSchemaDigest.Length != 32)
            throw new InvalidDataException("view.scene-projection.invalid-schema-digest");

        var records = snapshot.Records
            .OrderBy(static item => item.Key.SchemaId, StringComparer.Ordinal)
            .ThenBy(static item => item.Key.RecordIdHex, StringComparer.Ordinal)
            .Select(static item => new SceneProjectionRecord(
                item.Value.SchemaId,
                item.Key.RecordIdHex,
                item.Value.Revision.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        var primitives = (adapters ?? SceneProjectionAdapterRegistry.Empty).Project(snapshot.Records);

        return new SceneProjectionModel(
            snapshot.BasisStep.ToString(CultureInfo.InvariantCulture),
            Convert.ToHexStringLower(snapshot.ContinuityToken),
            Convert.ToHexStringLower(snapshot.ProjectionSchemaDigest),
            records,
            primitives);
    }
}
