using System.Net.WebSockets;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Administration.View.Protocol;

public sealed class AdminGatewayProtocolClient : IAsyncDisposable
{
    private ClientWebSocket? _socket;

    public AdminViewLifecycleState State { get; private set; } = AdminViewLifecycleState.Starting;
    public event Action<AdminViewLifecycleState>? StateChanged;

    public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
        => ConnectAsync(endpoint, allowInsecureLoopbackAlpha: false, cancellationToken);

    public async Task ConnectAsync(
        Uri endpoint,
        bool allowInsecureLoopbackAlpha,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var secure = string.Equals(endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase);
        var localAlpha = string.Equals(endpoint.Scheme, "ws", StringComparison.OrdinalIgnoreCase) &&
                         allowInsecureLoopbackAlpha && endpoint.IsLoopback;
        if (!secure && !localAlpha)
            throw new ArgumentException(
                "Admin Gateway endpoint must use wss:// unless an explicit loopback Alpha ws:// endpoint is enabled.",
                nameof(endpoint));

        var reconnecting = State is AdminViewLifecycleState.Closed or AdminViewLifecycleState.Faulted;
        _socket?.Dispose();
        _socket = new ClientWebSocket();
        SetState(reconnecting ? AdminViewLifecycleState.Reconnecting : AdminViewLifecycleState.Connecting);
        try
        {
            await _socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            SetState(AdminViewLifecycleState.Negotiating);
        }
        catch
        {
            SetState(AdminViewLifecycleState.Faulted);
            _socket.Dispose();
            _socket = null;
            throw;
        }
    }

    public Task SendBootstrapAsync(WireEnvelopeV1 envelope, CancellationToken cancellationToken = default)
        => SendBytesAsync(AdminGatewayBootstrapEnvelopeCodec.Encode(envelope), cancellationToken);

    public async Task<WireEnvelopeV1> ReceiveBootstrapAsync(CancellationToken cancellationToken = default)
        => AdminGatewayBootstrapEnvelopeCodec.Decode(await ReceiveBytesAsync(cancellationToken).ConfigureAwait(false));

    public Task SendAsync(WireEnvelopeV1 envelope, CancellationToken cancellationToken = default)
        => SendBytesAsync(AdminGatewayEnvelopeCodec.Encode(envelope), cancellationToken);

    public async Task<WireEnvelopeV1> ReceiveAsync(CancellationToken cancellationToken = default)
        => AdminGatewayEnvelopeCodec.Decode(await ReceiveBytesAsync(cancellationToken).ConfigureAwait(false));

    public void MarkAuthenticating() => SetState(AdminViewLifecycleState.Authenticating);
    public void MarkSyncing() => SetState(AdminViewLifecycleState.Syncing);
    public void MarkReady() => SetState(AdminViewLifecycleState.Ready);
    public void MarkDegraded() => SetState(AdminViewLifecycleState.Degraded);

    private async Task SendBytesAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        var socket = RequireOpenSocket();
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> ReceiveBytesAsync(CancellationToken cancellationToken)
    {
        var socket = RequireOpenSocket();
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                SetState(AdminViewLifecycleState.Closed);
                throw new WebSocketException("Gateway closed the Admin View WebSocket connection.");
            }
            if (result.MessageType != WebSocketMessageType.Binary)
                throw new InvalidDataException("Protocol requires binary WebSocket messages.");

            message.Write(buffer, 0, result.Count);
            if (message.Length > AdminGatewayEnvelopeCodec.MaxSerializedEnvelopeBytes)
                throw new InvalidDataException("protocol.limit-exceeded: envelope exceeds 8 MiB.");
            if (result.EndOfMessage) break;
        }
        return message.ToArray();
    }

    private ClientWebSocket RequireOpenSocket()
        => _socket is { State: WebSocketState.Open } socket
            ? socket
            : throw new InvalidOperationException("Admin Gateway WebSocket is not open.");

    private void SetState(AdminViewLifecycleState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket is { State: WebSocketState.Open or WebSocketState.CloseReceived } socket)
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "admin-view-dispose", CancellationToken.None).ConfigureAwait(false);
        }
        _socket?.Dispose();
        _socket = null;
    }
}
