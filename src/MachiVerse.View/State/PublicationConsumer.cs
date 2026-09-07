using System.IO.Compression;
using System.Security.Cryptography;
using Google.Protobuf;
using MachiVerse.Protocol.V1;
using MachiVerse.View.Protocol;

namespace MachiVerse.View.State;

public sealed class PublicationConsumer(ConfirmedWorldStore store)
{
    private const int MaxChunkPayloadBytes = 1024 * 1024;
    private const int PublicationFull = 1;
    private const int PublicationDelta = 2;
    private const int MutationUpsert = 1;
    private const int MutationDelete = 2;
    private const int CompressionNone = 1;
    private const int CompressionGzip = 2;

    public ViewLifecycleState LifecycleState { get; private set; } = ViewLifecycleState.Syncing;
    public string? ResyncReason { get; private set; }

    public ConfirmedWorldSnapshot Consume(
        StatePublicationV1 publication,
        ulong basisStep,
        IReadOnlyCollection<StatePublicationChunkV1> chunks)
    {
        try
        {
            var snapshot = BuildCandidate(publication, basisStep, chunks);
            store.Install(snapshot);
            LifecycleState = ViewLifecycleState.Ready;
            ResyncReason = null;
            return snapshot;
        }
        catch (ContinuityMismatchException ex)
        {
            LifecycleState = ViewLifecycleState.Resyncing;
            ResyncReason = ex.Message;
            throw;
        }
    }

    public void BeginReconnect() => LifecycleState = ViewLifecycleState.Reconnecting;
    public void BeginSync() => LifecycleState = ViewLifecycleState.Syncing;
    public void BeginResync(string reason)
    {
        LifecycleState = ViewLifecycleState.Resyncing;
        ResyncReason = reason;
    }

    public StateResyncRequestV1 CreateResyncRequest(ByteString worldId, bool forceFull)
    {
        ValidateId128(worldId, "world_id");
        var request = new StateResyncRequestV1
        {
            WorldId = worldId,
            Preference = (ResyncPreferenceV1)(forceFull ? 2 : 1)
        };
        if (!forceFull && store.Current is { } current)
        {
            request.ClientBasisStep = current.BasisStep;
            request.ClientContinuityToken = ByteString.CopyFrom(current.ContinuityToken);
        }
        return request;
    }

    private ConfirmedWorldSnapshot BuildCandidate(
        StatePublicationV1 publication,
        ulong basisStep,
        IReadOnlyCollection<StatePublicationChunkV1> chunks)
    {
        ValidatePublication(publication, chunks);
        var current = store.Current;
        var kind = (int)publication.Kind;
        if (kind == PublicationDelta)
        {
            if (current is null) throw new ContinuityMismatchException("protocol.continuity-mismatch:no-base-state");
            if (!publication.HasBaseStateContinuityToken || !current.ContinuityToken.AsSpan().SequenceEqual(publication.BaseStateContinuityToken.Span))
                throw new ContinuityMismatchException("protocol.continuity-mismatch:base-token");
        }

        var records = kind == PublicationFull
            ? new Dictionary<ProjectionRecordKey, ConfirmedProjectionRecord>()
            : new Dictionary<ProjectionRecordKey, ConfirmedProjectionRecord>(current!.Records);
        var seen = new HashSet<ProjectionRecordKey>();

        foreach (var chunk in chunks.OrderBy(static c => c.ChunkIndex))
        {
            var uncompressed = DecodeChunk(chunk);
            var payload = ProjectionChunkPayloadV1.Parser.ParseFrom(uncompressed);
            if (!payload.PublicationId.Equals(publication.PublicationId)) throw new InvalidDataException("protocol.publication-id-mismatch");
            if (payload.ChunkIndex != chunk.ChunkIndex) throw new InvalidDataException("protocol.chunk-index-mismatch");

            foreach (var record in payload.Records)
            {
                ValidateStableToken(record.RecordSchemaId, "record_schema_id");
                ValidateId128(record.RecordId, "record_id");
                var key = new ProjectionRecordKey(record.RecordSchemaId, Convert.ToHexStringLower(record.RecordId.Span));
                if (!seen.Add(key)) throw new InvalidDataException("protocol.duplicate-projection-record");

                switch ((int)record.MutationKind)
                {
                    case MutationUpsert:
                        records[key] = new ConfirmedProjectionRecord(
                            record.RecordSchemaId,
                            record.RecordId.ToByteArray(),
                            record.RecordRevision,
                            record.Payload.ToByteArray());
                        break;
                    case MutationDelete:
                        if (kind == PublicationFull) throw new InvalidDataException("protocol.full-publication-delete");
                        records.Remove(key);
                        break;
                    default:
                        throw new InvalidDataException("protocol.invalid-projection-mutation");
                }
            }
        }

        return new ConfirmedWorldSnapshot(
            basisStep,
            publication.StateContinuityToken.ToByteArray(),
            publication.ProjectionSchemaDigest.ToByteArray(),
            records);
    }

    private static void ValidatePublication(StatePublicationV1 publication, IReadOnlyCollection<StatePublicationChunkV1> chunks)
    {
        ValidateId128(publication.PublicationId, "publication_id");
        if (publication.StateContinuityToken.IsEmpty) throw new InvalidDataException("protocol.missing-continuity-token");
        if (publication.ProjectionSchemaDigest.Length != 32) throw new InvalidDataException("protocol.invalid-projection-schema-digest");
        if (publication.ChunkCount is 0 or > 65535 || chunks.Count != publication.ChunkCount)
            throw new InvalidDataException("protocol.invalid-publication-chunks");

        var kind = (int)publication.Kind;
        if (kind == PublicationFull && publication.HasBaseStateContinuityToken)
            throw new InvalidDataException("protocol.full-publication-has-base");
        if (kind == PublicationDelta && !publication.HasBaseStateContinuityToken)
            throw new InvalidDataException("protocol.delta-publication-missing-base");
        if (kind is not (PublicationFull or PublicationDelta))
            throw new InvalidDataException("protocol.invalid-publication-kind");

        var indices = new HashSet<uint>();
        foreach (var chunk in chunks)
        {
            if (!chunk.PublicationId.Equals(publication.PublicationId)) throw new InvalidDataException("protocol.publication-id-mismatch");
            if (chunk.ChunkCount != publication.ChunkCount || chunk.ChunkIndex >= chunk.ChunkCount || !indices.Add(chunk.ChunkIndex))
                throw new InvalidDataException("protocol.invalid-chunk-index");
            if (chunk.UncompressedPayloadDigest.Length != 32) throw new InvalidDataException("protocol.invalid-chunk-digest");
        }
    }

    private static byte[] DecodeChunk(StatePublicationChunkV1 chunk)
    {
        byte[] bytes;
        switch ((int)chunk.Compression)
        {
            case CompressionNone:
                bytes = chunk.Payload.ToByteArray();
                break;
            case CompressionGzip:
                using (var source = new MemoryStream(chunk.Payload.ToByteArray(), false))
                using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    while (true)
                    {
                        var read = gzip.Read(buffer, 0, buffer.Length);
                        if (read == 0) break;
                        if (output.Length + read > MaxChunkPayloadBytes) throw new InvalidDataException("protocol.limit-exceeded:publication-chunk");
                        output.Write(buffer, 0, read);
                    }
                    bytes = output.ToArray();
                }
                break;
            default:
                throw new InvalidDataException("protocol.unsupported-compression");
        }

        if (bytes.Length > MaxChunkPayloadBytes) throw new InvalidDataException("protocol.limit-exceeded:publication-chunk");
        var digest = SHA256.HashData(bytes);
        if (!CryptographicOperations.FixedTimeEquals(digest, chunk.UncompressedPayloadDigest.Span))
            throw new InvalidDataException("protocol.chunk-digest-mismatch");
        return bytes;
    }

    private static void ValidateId128(ByteString value, string field)
    {
        if (value.Length != 16 || value.Span.ToArray().All(static b => b == 0))
            throw new InvalidDataException($"protocol.invalid-id128:{field}");
    }

    private static void ValidateStableToken(string value, string field)
    {
        if (value.Length is < 1 or > 64 || !IsTokenStart(value[0]) || value.Any(static c => !IsTokenChar(c)))
            throw new InvalidDataException($"protocol.invalid-stable-token:{field}");
    }

    private static bool IsTokenStart(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9';
    private static bool IsTokenChar(char c) => IsTokenStart(c) || c is '.' or '_' or '/' or '-';
}
