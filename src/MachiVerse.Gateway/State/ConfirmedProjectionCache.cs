using System.IO.Compression;
using System.Security.Cryptography;
using Google.Protobuf;
using MachiVerse.Gateway.Protocol;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.State;

public readonly record struct ProjectionRecordKey(string SchemaId, string RecordIdHex);

public sealed record ConfirmedProjectionRecord(
    string SchemaId,
    byte[] RecordId,
    ulong Revision,
    byte[] Payload);

public sealed record ConfirmedStateSnapshot(
    ulong BasisStep,
    byte[] ContinuityToken,
    byte[] ProjectionSchemaDigest,
    IReadOnlyDictionary<ProjectionRecordKey, ConfirmedProjectionRecord> Records);

public sealed class ContinuityMismatchException(string message) : Exception(message);

public sealed class ConfirmedProjectionCache
{
    private const int MaxChunkPayloadBytes = 1024 * 1024;
    private const int ContinuityTokenBytes = 32;
    private const int PublicationFull = 1;
    private const int PublicationDelta = 2;
    private const int MutationUpsert = 1;
    private const int MutationDelete = 2;
    private const int CompressionNone = 1;
    private const int CompressionGzip = 2;
    private ConfirmedStateSnapshot? _current;

    public ConfirmedStateSnapshot? Current => Volatile.Read(ref _current);

    public ConfirmedStateSnapshot Apply(
        StatePublicationV1 publication,
        ulong basisStep,
        IReadOnlyCollection<StatePublicationChunkV1> chunks)
    {
        ValidatePublication(publication, chunks);
        var current = Current;
        var kind = (int)publication.Kind;

        if (kind == PublicationDelta)
        {
            if (current is null) throw new ContinuityMismatchException("protocol.continuity-mismatch:no-base-state");
            if (!current.ContinuityToken.AsSpan().SequenceEqual(publication.BaseStateContinuityToken.Span))
                throw new ContinuityMismatchException("protocol.continuity-mismatch:base-token");
        }

        var records = kind == PublicationFull
            ? new Dictionary<ProjectionRecordKey, ConfirmedProjectionRecord>()
            : new Dictionary<ProjectionRecordKey, ConfirmedProjectionRecord>(current!.Records);

        var seen = new HashSet<ProjectionRecordKey>();
        foreach (var chunk in chunks.OrderBy(static x => x.ChunkIndex))
        {
            var uncompressed = DecodeChunk(chunk);
            var payload = ProjectionChunkPayloadV1.Parser.ParseFrom(uncompressed);
            if (!payload.PublicationId.Equals(publication.PublicationId)) throw new InvalidDataException("protocol.publication-id-mismatch");
            if (payload.ChunkIndex != chunk.ChunkIndex) throw new InvalidDataException("protocol.chunk-index-mismatch");

            foreach (var record in payload.Records)
            {
                WireEnvelopeValidator.ValidateStableToken(record.RecordSchemaId, nameof(record.RecordSchemaId));
                WireEnvelopeValidator.ValidateId128(record.RecordId, "record_id", allowZero: false);
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

        var next = new ConfirmedStateSnapshot(
            basisStep,
            publication.StateContinuityToken.ToByteArray(),
            publication.ProjectionSchemaDigest.ToByteArray(),
            records);
        Volatile.Write(ref _current, next);
        return next;
    }

    private static void ValidatePublication(StatePublicationV1 publication, IReadOnlyCollection<StatePublicationChunkV1> chunks)
    {
        WireEnvelopeValidator.ValidateId128(publication.PublicationId, "publication_id", allowZero: false);
        if (publication.StateContinuityToken.Length != ContinuityTokenBytes)
            throw new InvalidDataException("protocol.invalid-continuity-token-length");
        if (publication.ProjectionSchemaDigest.Length != 32) throw new InvalidDataException("protocol.invalid-projection-schema-digest");
        if (publication.ChunkCount is 0 or > 65535) throw new InvalidDataException("protocol.invalid-chunk-count");
        if (chunks.Count != publication.ChunkCount) throw new InvalidDataException("protocol.incomplete-publication");

        var kind = (int)publication.Kind;
        if (kind == PublicationFull && publication.HasBaseStateContinuityToken)
            throw new InvalidDataException("protocol.full-publication-has-base");
        if (kind == PublicationDelta && !publication.HasBaseStateContinuityToken)
            throw new InvalidDataException("protocol.delta-publication-missing-base");
        if (kind == PublicationDelta && publication.BaseStateContinuityToken.Length != ContinuityTokenBytes)
            throw new InvalidDataException("protocol.invalid-base-continuity-token-length");
        if (kind is not (PublicationFull or PublicationDelta))
            throw new InvalidDataException("protocol.publication-kind-unspecified");

        var indices = new HashSet<uint>();
        foreach (var chunk in chunks)
        {
            if (!chunk.PublicationId.Equals(publication.PublicationId)) throw new InvalidDataException("protocol.publication-id-mismatch");
            if (chunk.ChunkCount != publication.ChunkCount || chunk.ChunkIndex >= chunk.ChunkCount) throw new InvalidDataException("protocol.invalid-chunk-index");
            if (!indices.Add(chunk.ChunkIndex)) throw new InvalidDataException("protocol.duplicate-chunk-index");
            if (chunk.UncompressedPayloadDigest.Length != 32) throw new InvalidDataException("protocol.invalid-chunk-digest");
        }
    }

    private static byte[] DecodeChunk(StatePublicationChunkV1 chunk)
    {
        byte[] uncompressed;
        switch ((int)chunk.Compression)
        {
            case CompressionNone:
                uncompressed = chunk.Payload.ToByteArray();
                break;
            case CompressionGzip:
                using (var source = new MemoryStream(chunk.Payload.ToByteArray(), writable: false))
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
                    uncompressed = output.ToArray();
                }
                break;
            default:
                throw new InvalidDataException("protocol.unsupported-compression");
        }

        if (uncompressed.Length > MaxChunkPayloadBytes) throw new InvalidDataException("protocol.limit-exceeded:publication-chunk");
        var digest = SHA256.HashData(uncompressed);
        if (!CryptographicOperations.FixedTimeEquals(digest, chunk.UncompressedPayloadDigest.Span))
            throw new InvalidDataException("protocol.chunk-digest-mismatch");
        return uncompressed;
    }
}
