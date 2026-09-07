using System.Globalization;
using System.Security.Cryptography;
using Google.Protobuf;
using MachiVerse.Protocol.V1;
using MachiVerse.View.Protocol;
using MachiVerse.View.Rendering;
using MachiVerse.View.State;

static ByteString Id(byte value) => ByteString.CopyFrom(Enumerable.Repeat(value, 16).ToArray());
static ByteString Hash(byte value) => ByteString.CopyFrom(Enumerable.Repeat(value, 32).ToArray());
static StatePublicationChunkV1 Chunk(StatePublicationV1 publication, ProjectionChunkPayloadV1 payload)
{
    var bytes = payload.ToByteArray();
    return new StatePublicationChunkV1
    {
        PublicationId = publication.PublicationId,
        ChunkIndex = payload.ChunkIndex,
        ChunkCount = publication.ChunkCount,
        UncompressedPayloadDigest = ByteString.CopyFrom(SHA256.HashData(bytes)),
        Compression = (CompressionKindV1)1,
        Payload = ByteString.CopyFrom(bytes)
    };
}

var publicationId = Id(1);
var recordId = Id(2);
var payload = new ProjectionChunkPayloadV1
{
    SubscriptionId = Id(3),
    PublicationId = publicationId,
    ChunkIndex = 0
};
payload.Records.Add(new ProjectionRecordV1
{
    RecordSchemaId = "view.test-record.v1",
    RecordSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
    RecordId = recordId,
    RecordRevision = 1,
    MutationKind = (ProjectionMutationKindV1)1,
    Payload = ByteString.CopyFromUtf8("record-v1")
});
var publication = new StatePublicationV1
{
    PublicationId = publicationId,
    Kind = (PublicationKindV1)1,
    StateContinuityToken = Hash(4),
    ChunkCount = 1,
    ProjectionSchemaDigest = Hash(5)
};

var store = new ConfirmedWorldStore();
var confirmedChangeCount = 0;
store.Changed += () => confirmedChangeCount++;
var consumer = new PublicationConsumer(store);
var confirmed = consumer.Consume(publication, 20, [Chunk(publication, payload)]);
if (consumer.LifecycleState != ViewLifecycleState.Ready || confirmed.BasisStep != 20 || confirmed.Records.Count != 1)
    throw new InvalidOperationException("FULL publication must atomically produce a Ready confirmed snapshot.");
if (confirmedChangeCount != 1)
    throw new InvalidOperationException("Confirmed install must notify presentation consumers exactly once.");

var delta = new StatePublicationV1
{
    PublicationId = Id(6),
    Kind = (PublicationKindV1)2,
    StateContinuityToken = Hash(7),
    BaseStateContinuityToken = Hash(4),
    ChunkCount = 1,
    ProjectionSchemaDigest = Hash(5)
};
var deltaPayload = new ProjectionChunkPayloadV1
{
    SubscriptionId = Id(3),
    PublicationId = delta.PublicationId,
    ChunkIndex = 0
};
deltaPayload.Records.Add(new ProjectionRecordV1
{
    RecordSchemaId = "view.test-record.v1",
    RecordSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
    RecordId = recordId,
    RecordRevision = 2,
    MutationKind = (ProjectionMutationKindV1)1,
    Payload = ByteString.CopyFromUtf8("record-v2")
});
var deltaConfirmed = consumer.Consume(delta, 21, [Chunk(delta, deltaPayload)]);
var key = new ProjectionRecordKey("view.test-record.v1", Convert.ToHexStringLower(recordId.Span));
if (deltaConfirmed.BasisStep != 21 || deltaConfirmed.Records[key].Revision != 2 || consumer.LifecycleState != ViewLifecycleState.Ready)
    throw new InvalidOperationException("Matching DELTA must advance the confirmed snapshot atomically.");
if (confirmedChangeCount != 2)
    throw new InvalidOperationException("Confirmed DELTA swap must notify presentation consumers exactly once.");

var mismatch = new StatePublicationV1
{
    PublicationId = Id(8),
    Kind = (PublicationKindV1)2,
    StateContinuityToken = Hash(9),
    BaseStateContinuityToken = Hash(99),
    ChunkCount = 1,
    ProjectionSchemaDigest = Hash(5)
};
var mismatchPayload = new ProjectionChunkPayloadV1
{
    SubscriptionId = Id(3),
    PublicationId = mismatch.PublicationId,
    ChunkIndex = 0
};
var rejected = false;
try
{
    consumer.Consume(mismatch, 22, [Chunk(mismatch, mismatchPayload)]);
}
catch (ContinuityMismatchException)
{
    rejected = true;
}
if (!rejected || consumer.LifecycleState != ViewLifecycleState.Resyncing)
    throw new InvalidOperationException("Continuity mismatch must enter Resyncing and remain outside normal confirmed state.");
if (store.Current?.BasisStep != 21)
    throw new InvalidOperationException("Rejected DELTA must not replace the last confirmed snapshot.");
if (confirmedChangeCount != 2)
    throw new InvalidOperationException("Rejected DELTA must not notify presentation as a confirmed swap.");

var request = consumer.CreateResyncRequest(Id(10), forceFull: false);
if (!request.HasClientBasisStep || request.ClientBasisStep != 21 || !request.HasClientContinuityToken)
    throw new InvalidOperationException("Resync request must use last confirmed basis/token.");

var terrainRecordId = Id(11).ToByteArray();
var builtRecordId = Id(12).ToByteArray();
var presenceRecordId = Id(13).ToByteArray();
var unknownRecordId = Id(14).ToByteArray();
var extremeRecords = new Dictionary<ProjectionRecordKey, ConfirmedProjectionRecord>
{
    [new ProjectionRecordKey("view.z-unknown.v1", Convert.ToHexStringLower(unknownRecordId))] =
        new("view.z-unknown.v1", unknownRecordId, ulong.MaxValue, [9]),
    [new ProjectionRecordKey("view.terrain-fixture.v1", Convert.ToHexStringLower(terrainRecordId))] =
        new("view.terrain-fixture.v1", terrainRecordId, ulong.MaxValue, [1]),
    [new ProjectionRecordKey("view.built-fixture.v1", Convert.ToHexStringLower(builtRecordId))] =
        new("view.built-fixture.v1", builtRecordId, 7, [2]),
    [new ProjectionRecordKey("view.presence-fixture.v1", Convert.ToHexStringLower(presenceRecordId))] =
        new("view.presence-fixture.v1", presenceRecordId, 8, [3])
};
var extremeSnapshot = new ConfirmedWorldSnapshot(
    ulong.MaxValue,
    Hash(15).ToByteArray(),
    Hash(16).ToByteArray(),
    extremeRecords);

var sceneAdapters = new SceneProjectionAdapterRegistry(
[
    new FixtureSceneAdapter("view.terrain-fixture.v1", ScenePrimitiveKinds.Terrain, SceneMaterialProfiles.Terrain, "terrain:fixture", new SceneVector3(0, -1, 0), new SceneVector3(20, 2, 20)),
    new FixtureSceneAdapter("view.built-fixture.v1", ScenePrimitiveKinds.Built, SceneMaterialProfiles.Built, "built:fixture", new SceneVector3(2, 1, 0), new SceneVector3(2, 2, 2)),
    new FixtureSceneAdapter("view.presence-fixture.v1", ScenePrimitiveKinds.Presence, SceneMaterialProfiles.Presence, "presence:fixture", new SceneVector3(0, 1, 2), new SceneVector3(1, 1, 1))
]);

var sceneProjection = SceneProjectionModel.FromConfirmed(extremeSnapshot, sceneAdapters);
if (sceneProjection.BasisStep != ulong.MaxValue.ToString(CultureInfo.InvariantCulture))
    throw new InvalidOperationException("SceneProjectionModel must preserve uint64 basis step across the JavaScript boundary.");
if (sceneProjection.Records.Count != 4 || sceneProjection.Records[^1].RecordSchemaId != "view.z-unknown.v1")
    throw new InvalidOperationException("SceneProjectionModel records must use stable schema/id order.");
if (sceneProjection.Records[^1].RecordRevision != ulong.MaxValue.ToString(CultureInfo.InvariantCulture))
    throw new InvalidOperationException("SceneProjectionModel must preserve uint64 record revision across the JavaScript boundary.");
if (sceneProjection.Primitives.Count != 3)
    throw new InvalidOperationException("Only explicitly registered presentation adapters may create scene primitives.");
if (sceneProjection.Primitives[0].Kind != ScenePrimitiveKinds.Built ||
    sceneProjection.Primitives[1].Kind != ScenePrimitiveKinds.Presence ||
    sceneProjection.Primitives[2].Kind != ScenePrimitiveKinds.Terrain)
    throw new InvalidOperationException("Scene primitives must use stable kind/id order.");
if (sceneProjection.Primitives.Any(static primitive => primitive.PrimitiveId.Contains("unknown", StringComparison.Ordinal)))
    throw new InvalidOperationException("Unknown projection schemas must remain confirmed metadata and must not be guessed into render objects.");

var invalidPrimitiveRejected = false;
try
{
    var invalidAdapters = new SceneProjectionAdapterRegistry(
    [
        new FixtureSceneAdapter(
            "view.terrain-fixture.v1",
            ScenePrimitiveKinds.Terrain,
            SceneMaterialProfiles.Terrain,
            "invalid-scale",
            new SceneVector3(0, 0, 0),
            new SceneVector3(1, 0, 1))
    ]);
    _ = SceneProjectionModel.FromConfirmed(extremeSnapshot, invalidAdapters);
}
catch (InvalidDataException ex) when (ex.Message == "view.scene-projection.invalid-scale")
{
    invalidPrimitiveRejected = true;
}
if (!invalidPrimitiveRejected)
    throw new InvalidOperationException("Invalid presentation primitive geometry must be rejected before the renderer boundary.");

Console.WriteLine("VIEW-02/VIEW-03 smoke tests passed.");

sealed class FixtureSceneAdapter(
    string recordSchemaId,
    string kind,
    string materialProfile,
    string primitiveId,
    SceneVector3 position,
    SceneVector3 scale) : ISceneProjectionAdapter
{
    public string RecordSchemaId { get; } = recordSchemaId;

    public IEnumerable<ScenePrimitive> Project(ConfirmedProjectionRecord record)
    {
        if (!string.Equals(record.SchemaId, RecordSchemaId, StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture adapter was dispatched for the wrong schema.");

        yield return new ScenePrimitive(
            primitiveId,
            kind,
            position,
            scale,
            LodMinDistance: 0,
            LodMaxDistance: 10000,
            materialProfile);
    }
}
