using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using MachiVerse.Protocol.V1;

const string ProtocolId = "mv.gateway-view";
var gatewayHttp = new Uri(args.Length > 0 ? args[0] : "http://127.0.0.1:5520");
var viewWs = new Uri(args.Length > 1 ? args[1] : "ws://127.0.0.1:5520/ws/v1/view");

using var http = new HttpClient { BaseAddress = gatewayHttp };
using var healthResponse = await http.GetAsync("/healthz");
healthResponse.EnsureSuccessStatusCode();
using var healthDocument = JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync());
var worldIdHex = healthDocument.RootElement.GetProperty("core").GetProperty("worldId").GetString()
    ?? throw new InvalidDataException("Gateway health did not expose Core worldId.");
var worldId = ByteString.CopyFrom(Convert.FromHexString(worldIdHex));
RequireId128(worldId, "world_id");

using var socket = new ClientWebSocket();
await socket.ConnectAsync(viewWs, CancellationToken.None);
var senderInstanceId = RandomId128();

var hello = new ProtocolHelloV1 { ProtocolId = ProtocolId };
hello.SupportedVersions.Add(new SupportedVersionRangeV1 { Major = 1, MinMinor = 0, MaxMinor = 0 });
hello.ProvidedCapabilities.Add("protocol.protobuf.v1");
hello.RequiredCapabilities.Add("protocol.protobuf.v1");
var helloEnvelope = Bootstrap("protocol.hello", "protocol.hello.v1", hello, senderInstanceId);
await SendAsync(socket, helloEnvelope);
var acceptEnvelope = await ReceiveAsync(socket);
if (acceptEnvelope.MessageType != "protocol.accept" || acceptEnvelope.NegotiationGeneration != 0)
    throw new InvalidDataException("Gateway did not return bootstrap protocol.accept.");
var accept = ProtocolAcceptV1.Parser.ParseFrom(acceptEnvelope.Payload);
if (accept.NegotiatedVersion?.Major != 1 || accept.NegotiatedVersion.Minor != 0 || accept.NegotiationGeneration != 1)
    throw new InvalidDataException("Gateway negotiated unexpected mv.gateway-view version.");

var login = new AuthLoginBeginV1 { AuthDomain = (AuthDomainWireV1)1 };
var loginEnvelope = Normal("auth.login", "protocol.auth-login-request", login, senderInstanceId);
await SendAsync(socket, loginEnvelope);
var loginResultEnvelope = await ReceiveNormalAsync(socket, "auth.login.result");
var loginResult = AuthLoginResultV1.Parser.ParseFrom(loginResultEnvelope.Payload);
if (loginResult.Result is null || (int)loginResult.Result.Status != 1 || !loginResult.HasSessionId || !loginResult.HasSessionGeneration)
    throw new InvalidDataException("Gateway local Alpha login did not establish a session.");
RequireId128(loginResult.SessionId, "session_id");

var sessionChangedEnvelope = await ReceiveNormalAsync(socket, "auth.session.changed");
var session = AuthSessionStateV1.Parser.ParseFrom(sessionChangedEnvelope.Payload);
if (!session.SessionId.Equals(loginResult.SessionId) || (int)session.AuthDomain != 1 || (int)session.Status != 1)
    throw new InvalidDataException("Gateway session projection does not match login result.");

var subscriptionId = RandomId128();
var subscribe = new ViewSubscriptionRequestV1
{
    SubscriptionId = subscriptionId,
    ProjectionProfile = "standard",
    PreferDelta = true,
};
var subscribeEnvelope = Normal(
    "world.subscribe",
    "protocol.view-subscription-request",
    subscribe,
    senderInstanceId,
    new WorldContextWireV1 { WorldId = worldId });
await SendAsync(socket, subscribeEnvelope);

var beginEnvelope = await ReceiveNormalAsync(socket, "world.state.begin");
if (beginEnvelope.WorldContext is null || !beginEnvelope.WorldContext.HasBasisStep || !beginEnvelope.WorldContext.WorldId.Equals(worldId))
    throw new InvalidDataException("Confirmed publication missing expected WorldContext.");
var publication = StatePublicationV1.Parser.ParseFrom(beginEnvelope.Payload);
if ((int)publication.Kind != 1 || publication.ChunkCount == 0 || publication.HasBaseStateContinuityToken)
    throw new InvalidDataException("INT-01 View bootstrap requires a FULL publication.");
if (publication.StateContinuityToken.Length != 32 || publication.ProjectionSchemaDigest.Length != 32)
    throw new InvalidDataException("Confirmed publication digest shape invalid.");

var seen = new HashSet<uint>();
var recordCount = 0;
for (var i = 0; i < publication.ChunkCount; i++)
{
    var chunkEnvelope = await ReceiveNormalAsync(socket, "world.state.chunk");
    if (chunkEnvelope.WorldContext is null || !chunkEnvelope.WorldContext.HasBasisStep ||
        chunkEnvelope.WorldContext.BasisStep != beginEnvelope.WorldContext.BasisStep)
        throw new InvalidDataException("Publication chunk basis mismatch.");
    var chunk = StatePublicationChunkV1.Parser.ParseFrom(chunkEnvelope.Payload);
    if (!chunk.PublicationId.Equals(publication.PublicationId) || chunk.ChunkCount != publication.ChunkCount || !seen.Add(chunk.ChunkIndex))
        throw new InvalidDataException("Publication chunk identity mismatch.");
    if ((int)chunk.Compression != 1)
        throw new InvalidDataException("INT-01 wire probe expects baseline NONE compression.");
    var payloadBytes = chunk.Payload.ToByteArray();
    if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payloadBytes), chunk.UncompressedPayloadDigest.Span))
        throw new InvalidDataException("Publication chunk digest mismatch.");
    var payload = ProjectionChunkPayloadV1.Parser.ParseFrom(payloadBytes);
    if (!payload.SubscriptionId.Equals(subscriptionId) || !payload.PublicationId.Equals(publication.PublicationId) || payload.ChunkIndex != chunk.ChunkIndex)
        throw new InvalidDataException("Projection payload identity mismatch.");
    recordCount += payload.Records.Count;
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    status = "ok",
    worldId = worldIdHex,
    basisStep = beginEnvelope.WorldContext.BasisStep,
    publicationId = Convert.ToHexStringLower(publication.PublicationId.Span),
    chunks = publication.ChunkCount,
    records = recordCount,
}));

static WireEnvelopeV1 Bootstrap(string type, string schema, IMessage payload, ByteString sender)
    => new()
    {
        EnvelopeVersion = 1,
        ProtocolId = ProtocolId,
        ProtocolVersion = new ProtocolVersionV1 { Major = 0, Minor = 0 },
        NegotiationGeneration = 0,
        MessageType = type,
        MessageId = RandomId128(),
        CorrelationId = RandomId128(),
        SenderInstanceId = sender,
        PayloadSchemaId = schema,
        PayloadSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
        PayloadCompression = (CompressionKindV1)1,
        Payload = payload.ToByteString(),
    };

static WireEnvelopeV1 Normal(
    string type,
    string schema,
    IMessage payload,
    ByteString sender,
    WorldContextWireV1? world = null)
    => new()
    {
        EnvelopeVersion = 1,
        ProtocolId = ProtocolId,
        ProtocolVersion = new ProtocolVersionV1 { Major = 1, Minor = 0 },
        NegotiationGeneration = 1,
        MessageType = type,
        MessageId = RandomId128(),
        CorrelationId = RandomId128(),
        SenderInstanceId = sender,
        PayloadSchemaId = schema,
        PayloadSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
        PayloadCompression = (CompressionKindV1)1,
        Payload = payload.ToByteString(),
        WorldContext = world,
    };

static async Task SendAsync(ClientWebSocket socket, WireEnvelopeV1 envelope)
{
    var bytes = envelope.ToByteArray();
    if (bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("Envelope exceeds 8 MiB.");
    await socket.SendAsync(bytes, WebSocketMessageType.Binary, true, CancellationToken.None);
}

static async Task<WireEnvelopeV1> ReceiveNormalAsync(ClientWebSocket socket, string expectedType)
{
    var envelope = await ReceiveAsync(socket);
    if (envelope.EnvelopeVersion != 1 || envelope.ProtocolId != ProtocolId ||
        envelope.ProtocolVersion?.Major != 1 || envelope.ProtocolVersion.Minor != 0 || envelope.NegotiationGeneration != 1)
        throw new InvalidDataException("Normal envelope negotiation metadata invalid.");
    if (envelope.MessageType != expectedType)
        throw new InvalidDataException($"Expected {expectedType}, received {envelope.MessageType}.");
    RequireId128(envelope.MessageId, "message_id");
    RequireId128(envelope.CorrelationId, "correlation_id");
    RequireId128(envelope.SenderInstanceId, "sender_instance_id");
    return envelope;
}

static async Task<WireEnvelopeV1> ReceiveAsync(ClientWebSocket socket)
{
    var buffer = new byte[64 * 1024];
    using var stream = new MemoryStream();
    while (true)
    {
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Close)
            throw new WebSocketException($"Gateway closed socket: {socket.CloseStatusDescription}");
        if (result.MessageType != WebSocketMessageType.Binary)
            throw new InvalidDataException("Expected binary WebSocket message.");
        stream.Write(buffer, 0, result.Count);
        if (stream.Length > 8 * 1024 * 1024) throw new InvalidDataException("Envelope exceeds 8 MiB.");
        if (result.EndOfMessage) break;
    }
    return WireEnvelopeV1.Parser.ParseFrom(stream.ToArray());
}

static ByteString RandomId128()
{
    var bytes = new byte[16];
    do RandomNumberGenerator.Fill(bytes); while (bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0);
    return ByteString.CopyFrom(bytes);
}

static void RequireId128(ByteString value, string field)
{
    if (value.Length != 16 || value.Span.IndexOfAnyExcept((byte)0) < 0)
        throw new InvalidDataException($"Invalid Id128: {field}.");
}
