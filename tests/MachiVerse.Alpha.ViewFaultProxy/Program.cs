using System.Net;
using System.Net.WebSockets;
using Google.Protobuf;
using MachiVerse.Protocol.V1;

var builder = WebApplication.CreateBuilder(args);
var upstream = new Uri(Environment.GetEnvironmentVariable("MACHIVERSE_FAULT_PROXY_UPSTREAM")
    ?? "ws://127.0.0.1:5720/ws/v1/view");
var state = new FaultProxyState();
builder.Services.AddSingleton(state);

var app = builder.Build();
app.UseWebSockets();

app.MapGet("/healthz", (FaultProxyState proxyState) => Results.Ok(new
{
    component = "alpha-view-fault-proxy",
    status = "ready",
    upstream = upstream.ToString(),
    corruptedDelta = proxyState.CorruptedDelta,
    resyncObserved = proxyState.ResyncObserved,
    resyncReleased = proxyState.ResyncReleased,
}));

app.MapPost("/control/release-resync", (HttpContext context, FaultProxyState proxyState) =>
{
    if (context.Connection.RemoteIpAddress is null || !IPAddress.IsLoopback(context.Connection.RemoteIpAddress))
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!proxyState.ResyncObserved)
        return Results.Conflict(new { code = "test.resync-not-observed" });
    proxyState.ReleaseResync();
    return Results.Ok(new { released = true });
});

app.Map("/ws/v1/view", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    if (context.Connection.RemoteIpAddress is null || !IPAddress.IsLoopback(context.Connection.RemoteIpAddress))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    using var downstream = await context.WebSockets.AcceptWebSocketAsync();
    using var upstreamSocket = new ClientWebSocket();
    await upstreamSocket.ConnectAsync(upstream, context.RequestAborted);

    using var relayCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    var clientToGateway = RelayClientToGatewayAsync(downstream, upstreamSocket, state, relayCts.Token);
    var gatewayToClient = RelayGatewayToClientAsync(upstreamSocket, downstream, state, relayCts.Token);

    await Task.WhenAny(clientToGateway, gatewayToClient);
    relayCts.Cancel();
    try { await Task.WhenAll(clientToGateway, gatewayToClient); }
    catch (OperationCanceledException) when (relayCts.IsCancellationRequested) { }
    catch (WebSocketException) { }
});

await app.RunAsync();

static async Task RelayClientToGatewayAsync(
    WebSocket downstream,
    WebSocket upstream,
    FaultProxyState state,
    CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested &&
           downstream.State == WebSocketState.Open &&
           upstream.State == WebSocketState.Open)
    {
        var message = await ReceiveMessageAsync(downstream, cancellationToken);
        if (message is null)
        {
            await TryCloseOutputAsync(upstream, cancellationToken);
            return;
        }

        if (message.Value.Type == WebSocketMessageType.Binary)
        {
            WireEnvelopeV1 envelope;
            try
            {
                envelope = WireEnvelopeV1.Parser.ParseFrom(message.Value.Payload);
            }
            catch (InvalidProtocolBufferException)
            {
                envelope = null!;
            }

            if (envelope is not null &&
                string.Equals(envelope.MessageType, "world.state.resync-request", StringComparison.Ordinal) &&
                state.CorruptedDelta)
            {
                await state.HoldFirstResyncAsync(cancellationToken);
            }
        }

        await upstream.SendAsync(
            message.Value.Payload,
            message.Value.Type,
            endOfMessage: true,
            cancellationToken);
    }
}

static async Task RelayGatewayToClientAsync(
    WebSocket upstream,
    WebSocket downstream,
    FaultProxyState state,
    CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested &&
           downstream.State == WebSocketState.Open &&
           upstream.State == WebSocketState.Open)
    {
        var message = await ReceiveMessageAsync(upstream, cancellationToken);
        if (message is null)
        {
            await TryCloseOutputAsync(downstream, cancellationToken);
            return;
        }

        var payload = message.Value.Payload;
        if (message.Value.Type == WebSocketMessageType.Binary)
        {
            try
            {
                var envelope = WireEnvelopeV1.Parser.ParseFrom(payload);
                if (string.Equals(envelope.MessageType, "world.state.begin", StringComparison.Ordinal) &&
                    string.Equals(envelope.PayloadSchemaId, "protocol.state-publication.v1", StringComparison.Ordinal))
                {
                    var publication = StatePublicationV1.Parser.ParseFrom(envelope.Payload);
                    if ((int)publication.Kind == 2 && state.TryMarkCorruptedDelta())
                    {
                        if (!publication.HasBaseStateContinuityToken || publication.BaseStateContinuityToken.Length != 32)
                            throw new InvalidDataException("Test proxy expected DELTA with a valid 32-byte base token.");

                        var corrupted = publication.BaseStateContinuityToken.ToByteArray();
                        corrupted[0] ^= 0x80;
                        publication.BaseStateContinuityToken = ByteString.CopyFrom(corrupted);
                        envelope.Payload = publication.ToByteString();
                        payload = envelope.ToByteArray();
                        Console.WriteLine("Injected one test-owned DELTA base continuity mismatch.");
                    }
                }
            }
            catch (InvalidProtocolBufferException)
            {
                // Non-protocol binary frames are relayed unchanged; this proxy does not create authority.
            }
        }

        await downstream.SendAsync(payload, message.Value.Type, endOfMessage: true, cancellationToken);
    }
}

static async Task<(WebSocketMessageType Type, byte[] Payload)?> ReceiveMessageAsync(
    WebSocket socket,
    CancellationToken cancellationToken)
{
    var buffer = new byte[64 * 1024];
    using var stream = new MemoryStream();
    WebSocketMessageType? type = null;

    while (true)
    {
        var result = await socket.ReceiveAsync(buffer, cancellationToken);
        if (result.MessageType == WebSocketMessageType.Close)
            return null;

        type ??= result.MessageType;
        if (result.MessageType != type)
            throw new InvalidDataException("WebSocket message type changed across fragments.");

        stream.Write(buffer, 0, result.Count);
        if (stream.Length > 8 * 1024 * 1024)
            throw new InvalidDataException("Fault proxy refuses messages above 8 MiB.");
        if (result.EndOfMessage)
            return (type.Value, stream.ToArray());
    }
}

static async Task TryCloseOutputAsync(WebSocket socket, CancellationToken cancellationToken)
{
    if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        return;
    try
    {
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "peer closed", cancellationToken);
    }
    catch (WebSocketException)
    {
    }
}

sealed class FaultProxyState
{
    private int _corruptedDelta;
    private int _resyncObserved;
    private int _resyncReleased;
    private readonly TaskCompletionSource _releaseResync = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool CorruptedDelta => Volatile.Read(ref _corruptedDelta) != 0;
    public bool ResyncObserved => Volatile.Read(ref _resyncObserved) != 0;
    public bool ResyncReleased => Volatile.Read(ref _resyncReleased) != 0;

    public bool TryMarkCorruptedDelta()
        => Interlocked.CompareExchange(ref _corruptedDelta, 1, 0) == 0;

    public async Task HoldFirstResyncAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _resyncObserved, 1, 0) != 0)
            return;

        Console.WriteLine("Holding View force-FULL resync request until the test releases it.");
        await _releaseResync.Task.WaitAsync(cancellationToken);
        Console.WriteLine("Released View force-FULL resync request to the real Gateway.");
    }

    public void ReleaseResync()
    {
        Interlocked.Exchange(ref _resyncReleased, 1);
        _releaseResync.TrySetResult();
    }
}
