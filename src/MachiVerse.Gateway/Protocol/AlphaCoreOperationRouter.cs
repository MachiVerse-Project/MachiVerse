using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Protocol;

public sealed record AlphaCoreBatchSubmission(OperationBatchV1 Batch);

/// <summary>
/// Process-local rendezvous between browser-facing Alpha bridges and the one authenticated
/// Gateway→Core duplex stream. Operation identity remains the StandardOperationV1 identity;
/// BatchId is transport aggregation only and may change across a later retry.
/// </summary>
public sealed class AlphaCoreOperationRouter
{
    private const string BatchKind = "alpha.single-operation";
    private readonly Channel<AlphaCoreBatchSubmission> _outbound = Channel.CreateBounded<AlphaCoreBatchSubmission>(
        new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    private readonly ConcurrentDictionary<string, TaskCompletionSource<OperationBatchResultV1>> _pending = new(StringComparer.Ordinal);

    public ChannelReader<AlphaCoreBatchSubmission> Outbound => _outbound.Reader;

    public async Task<OperationBatchResultV1> SubmitAsync(
        StandardOperationV1 operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        RequireId128(operation.OperationId, "operation_id");
        if (operation.ImmutablePayloadDigest.Length != 32)
            throw new InvalidDataException("protocol.invalid-hash:immutable_payload_digest");

        var batchId = RandomId128();
        var batch = new OperationBatchV1
        {
            BatchId = batchId,
            BatchKind = BatchKind,
            BatchDigest = ByteString.CopyFrom(ComputeBatchDigest(operation)),
        };
        batch.Operations.Add(operation.Clone());

        var key = Convert.ToHexStringLower(batchId.Span);
        var completion = new TaskCompletionSource<OperationBatchResultV1>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(key, completion)) throw new InvalidOperationException("alpha.batch-id-collision");
        try
        {
            await _outbound.Writer.WriteAsync(new AlphaCoreBatchSubmission(batch), cancellationToken);
            return await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _pending.TryRemove(key, out _);
        }
    }

    public void Complete(OperationBatchResultV1 result)
    {
        ArgumentNullException.ThrowIfNull(result);
        RequireId128(result.BatchId, "batch_id");
        if (result.BatchDigest.Length != 32)
            throw new InvalidDataException("protocol.invalid-hash:batch_digest");
        var key = Convert.ToHexStringLower(result.BatchId.Span);
        if (!_pending.TryGetValue(key, out var pending))
            throw new InvalidDataException("protocol.unexpected-operation-batch-result");
        pending.TrySetResult(result.Clone());
    }

    public void FailPending(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        foreach (var pending in _pending.Values) pending.TrySetException(error);
    }

    private static byte[] ComputeBatchDigest(StandardOperationV1 operation)
    {
        var label = Encoding.ASCII.GetBytes("mv.operation-batch.v1");
        var kind = Encoding.ASCII.GetBytes(BatchKind);
        var preimage = new byte[label.Length + 1 + kind.Length + 1 + 16 + 32];
        var offset = 0;
        label.CopyTo(preimage, offset); offset += label.Length;
        preimage[offset++] = 0;
        kind.CopyTo(preimage, offset); offset += kind.Length;
        preimage[offset++] = 0;
        operation.OperationId.Span.CopyTo(preimage.AsSpan(offset, 16)); offset += 16;
        operation.ImmutablePayloadDigest.Span.CopyTo(preimage.AsSpan(offset, 32));
        return SHA256.HashData(preimage);
    }

    private static ByteString RandomId128()
    {
        Span<byte> bytes = stackalloc byte[16];
        do RandomNumberGenerator.Fill(bytes); while (bytes.IndexOfAnyExcept((byte)0) < 0);
        return ByteString.CopyFrom(bytes);
    }

    private static void RequireId128(ByteString value, string field)
    {
        if (value.Length != 16 || value.Span.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"protocol.invalid-id:{field}");
    }
}
