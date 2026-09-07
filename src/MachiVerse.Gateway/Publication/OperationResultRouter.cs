using MachiVerse.Gateway.State;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Publication;

public enum ResultRouteEnqueueStatusV1
{
    Enqueued = 1,
    Duplicate = 2,
    Backpressured = 3,
}

public sealed record OperationResultDeliveryV1(
    byte[] RouteId,
    byte[] OperationId,
    ResultV1 Result);

public sealed record ResultRouteEnqueueResultV1(
    ResultRouteEnqueueStatusV1 Status,
    string ReasonCode,
    int PendingCount);

/// <summary>
/// Bounded terminal-result route queue. Unlike state publication, entries are never dropped or
/// coalesced. Capacity exhaustion is surfaced to the caller so the custody record remains the
/// retry authority until this queue can accept the terminal result.
/// </summary>
public sealed class OperationResultRouterV1
{
    private readonly int _capacity;
    private readonly Queue<OperationResultDeliveryV1> _pending = new();
    private readonly HashSet<string> _queuedKeys = new(StringComparer.Ordinal);

    public OperationResultRouterV1(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int PendingCount => _pending.Count;
    public int Capacity => _capacity;

    public ResultRouteEnqueueResultV1 TryEnqueue(
        ReadOnlySpan<byte> routeId,
        OperationCustodyRecord custody)
    {
        var route = RequireId128(routeId, nameof(routeId));
        ArgumentNullException.ThrowIfNull(custody);
        if (custody.State != GatewayCustodyState.Terminal || custody.TerminalResult is null)
            throw new InvalidDataException("result.non-terminal-custody");
        if (custody.OperationId.Length != 16 || custody.OperationId.AsSpan().IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException("protocol.invalid-id:operation_id");

        var key = Key(route, custody.OperationId);
        if (_queuedKeys.Contains(key))
            return new ResultRouteEnqueueResultV1(
                ResultRouteEnqueueStatusV1.Duplicate,
                "result.already-queued",
                _pending.Count);

        if (_pending.Count >= _capacity)
            return new ResultRouteEnqueueResultV1(
                ResultRouteEnqueueStatusV1.Backpressured,
                "gateway.result-backpressure",
                _pending.Count);

        _pending.Enqueue(new OperationResultDeliveryV1(
            route,
            custody.OperationId.ToArray(),
            custody.TerminalResult.Clone()));
        _queuedKeys.Add(key);
        return new ResultRouteEnqueueResultV1(
            ResultRouteEnqueueStatusV1.Enqueued,
            "result.queued",
            _pending.Count);
    }

    public OperationResultDeliveryV1? TryDequeue()
    {
        if (_pending.Count == 0) return null;
        var delivery = _pending.Dequeue();
        _queuedKeys.Remove(Key(delivery.RouteId, delivery.OperationId));
        return delivery;
    }

    private static string Key(byte[] routeId, byte[] operationId)
        => Convert.ToHexStringLower(routeId) + ":" + Convert.ToHexStringLower(operationId);

    private static byte[] RequireId128(ReadOnlySpan<byte> value, string field)
    {
        if (value.Length != 16 || value.IndexOfAnyExcept((byte)0) < 0)
            throw new InvalidDataException($"protocol.invalid-id:{field}");
        return value.ToArray();
    }
}
