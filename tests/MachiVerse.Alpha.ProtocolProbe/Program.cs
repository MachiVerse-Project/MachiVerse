using System.Net.WebSockets;
using System.Security.Cryptography;
using Google.Protobuf;
using MachiVerse.Protocol.V1;

await ProbeWrongDomainAsync(new Uri("ws://127.0.0.1:5520/ws/v1/view"), "mv.gateway-view", (AuthDomainWireV1)2);
await ProbeWrongDomainAsync(new Uri("ws://127.0.0.1:5520/ws/v1/admin"), "mv.gateway-admin-view", (AuthDomainWireV1)1);
Console.WriteLine("Alpha Gateway wrong-domain probes passed.");

static async Task ProbeWrongDomainAsync(Uri endpoint, string protocolId, AuthDomainWireV1 wrongDomain)
{
    using var socket = new ClientWebSocket();
    await socket.ConnectAsync(endpoint, CancellationToken.None);

    var hello = new ProtocolHelloV1 { ProtocolId = protocolId };
    hello.SupportedVersions.Add(new SupportedVersionRangeV1 { Major = 1, MinMinor = 0, MaxMinor = 0 });
    hello.ProvidedCapabilities.Add("protocol.protobuf.v1");
    hello.RequiredCapabilities.Add("protocol.protobuf.v1");
    await SendAsync(socket, Envelope(
        protocolId,
        "protocol.hello",
        "protocol.hello.v1",
        hello,
        major: 0,
        negotiationGeneration: 0));

    var acceptEnvelope = await ReceiveEnvelopeAsync(socket);
    if (!string.Equals(acceptEnvelope.MessageType, "protocol.accept", StringComparison.Ordinal))
        throw new InvalidOperationException($"{protocolId}: expected protocol.accept, got {acceptEnvelope.MessageType}.");
    var accept = ProtocolAcceptV1.Parser.ParseFrom(acceptEnvelope.Payload);
    if (accept.NegotiatedVersion is null || accept.NegotiatedVersion.Major != 1 || accept.NegotiationGeneration != 1)
        throw new InvalidOperationException($"{protocolId}: negotiation did not establish v1 generation 1.");

    await SendAsync(socket, Envelope(
        protocolId,
        "auth.login",
        "protocol.auth-login-request",
        new AuthLoginBeginV1 { AuthDomain = wrongDomain },
        major: 1,
        negotiationGeneration: 1));

    var buffer = new byte[4096];
    var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
    if (result.MessageType != WebSocketMessageType.Close)
        throw new InvalidOperationException($"{protocolId}: wrong-domain login was not closed fail-closed.");
    if (result.CloseStatus != WebSocketCloseStatus.ProtocolError)
        throw new InvalidOperationException($"{protocolId}: wrong-domain close status was {result.CloseStatus}, expected ProtocolError.");
    if (result.CloseStatusDescription is null || !result.CloseStatusDescription.Contains("auth.unauthorized", StringComparison.Ordinal))
        throw new InvalidOperationException($"{protocolId}: wrong-domain close reason did not preserve auth.unauthorized.");
}

static WireEnvelopeV1 Envelope(
    string protocolId,
    string messageType,
    string schema,
    IMessage payload,
    uint major,
    uint negotiationGeneration)
    => new()
    {
        EnvelopeVersion = 1,
        ProtocolId = protocolId,
        ProtocolVersion = new ProtocolVersionV1 { Major = major, Minor = 0 },
        NegotiationGeneration = negotiationGeneration,
        MessageType = messageType,
        MessageId = RandomId128(),
        CorrelationId = RandomId128(),
        SenderInstanceId = RandomId128(),
        PayloadSchemaId = schema,
        PayloadSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
        PayloadCompression = (CompressionKindV1)1,
        Payload = payload.ToByteString(),
    };

static async Task SendAsync(ClientWebSocket socket, WireEnvelopeV1 envelope)
{
    var bytes = envelope.ToByteArray();
    await socket.SendAsync(bytes, WebSocketMessageType.Binary, true, CancellationToken.None);
}

static async Task<WireEnvelopeV1> ReceiveEnvelopeAsync(ClientWebSocket socket)
{
    var buffer = new byte[64 * 1024];
    using var stream = new MemoryStream();
    while (true)
    {
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Close)
            throw new InvalidOperationException($"Gateway closed during negotiation: {result.CloseStatusDescription}");
        if (result.MessageType != WebSocketMessageType.Binary)
            throw new InvalidOperationException("Gateway returned a non-binary protocol frame.");
        stream.Write(buffer, 0, result.Count);
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
