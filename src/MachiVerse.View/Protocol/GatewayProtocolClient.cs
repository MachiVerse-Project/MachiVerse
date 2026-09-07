using System.Net.WebSockets;
using MachiVerse.Protocol.V1;

namespace MachiVerse.View.Protocol;

public sealed class GatewayProtocolClient : IAsyncDisposable
{
    private ClientWebSocket? _socket;

    public ViewLifecycleState State { get; private set; } = ViewLifecycleState.Starting;
    public event Action<ViewLifecycleState>? StateChanged;

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeWss, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Gateway endpoint must use wss://.", nameof(endpoint));

        var reconnecting = State is ViewLifecycleState.Closed or ViewLifecycleState.Faulted;
        _socket?.Dispose();
        _socket = new ClientWebSocket();
        SetState(reconnecting ? ViewLifecycleState.Reconnecting : ViewLifecycleState.Connecting);
        try
        {
            await _socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            SetState(ViewLifecycleState.Negotiating);
        }
        catch
        {
            SetState(ViewLifecycleState.Faulted);
            _socket.Dispose();
            _socket = null;
            throw;
        }
    }

    public async Task SendAsync(WireEnvelopeV1 envelope, CancellationToken cancellationToken = default)
    {
        var socket = RequireOpenSocket();
        var bytes = GatewayEnvelopeCodec.Encode(envelope);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WireEnvelopeV1> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var socket = RequireOpenSocket();
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                SetState(ViewLifecycleState.Closed);
                throw new WebSocketException("Gateway closed the WebSocket connection.");
            }
            if (result.MessageType != WebSocketMessageType.Binary)
                throw new InvalidDataException("Protocol requires binary WebSocket messages.");

            message.Write(buffer, 0, result.Count);
            if (message.Length > GatewayEnvelopeCodec.MaxSerializedEnvelopeBytes)
                throw new InvalidDataException("protocol.limit-exceeded: envelope exceeds 8 MiB.");
            if (result.EndOfMessage) break;
        }

        return GatewayEnvelopeCodec.Decode(message.ToArray());
    }

    public void MarkAuthenticating() => SetState(ViewLifecycleState.Authenticating);
    public void MarkSyncing() => SetState(ViewLifecycleState.Syncing);
    public void MarkReady() => SetState(ViewLifecycleState.Ready);
    public void MarkResyncing() => SetState(ViewLifecycleState.Resyncing);
    public void MarkDegraded() => SetState(ViewLifecycleState.Degraded);

    private ClientWebSocket RequireOpenSocket()
        => _socket is { State: WebSocketState.Open } socket
            ? socket
            : throw new InvalidOperationException("Gateway WebSocket is not open.");

    private void SetState(ViewLifecycleState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket is { State: WebSocketState.Open or WebSocketState.CloseReceived } socket)
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "view-dispose", CancellationToken.None).ConfigureAwait(false);
        }
        _socket?.Dispose();
        _socket = null;
    }
}
