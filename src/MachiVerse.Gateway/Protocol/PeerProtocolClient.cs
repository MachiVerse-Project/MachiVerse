using Grpc.Core;
using Grpc.Net.Client;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Protocol;

public sealed class PeerProtocolClient : IAsyncDisposable
{
    public const string ProtocolId = "mv.gateway-gateway";

    private readonly GrpcChannel _channel;
    private readonly MachiVerseInternalProtocolV1.MachiVerseInternalProtocolV1Client _client;

    public PeerProtocolClient(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri) throw new ArgumentException("Peer endpoint must be absolute.", nameof(endpoint));
        _channel = GrpcChannel.ForAddress(endpoint);
        _client = new MachiVerseInternalProtocolV1.MachiVerseInternalProtocolV1Client(_channel);
    }

    public AsyncDuplexStreamingCall<WireEnvelopeV1, WireEnvelopeV1> Connect(CancellationToken cancellationToken = default)
        => _client.Connect(cancellationToken: cancellationToken);

    public ValueTask DisposeAsync()
    {
        _channel.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class PeerProtocolNegotiationState
{
    public uint NegotiationGeneration { get; private set; }
    public uint Major { get; private set; }
    public uint Minor { get; private set; }
    public IReadOnlySet<string> EffectiveCapabilities { get; private set; } = new HashSet<string>(StringComparer.Ordinal);
    public bool IsNegotiated => NegotiationGeneration != 0;

    public void Accept(ProtocolAcceptV1 accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        if (accepted.NegotiatedVersion is null || accepted.NegotiatedVersion.Major == 0 || accepted.NegotiationGeneration == 0)
            throw new InvalidDataException("protocol.invalid-negotiation-accept");

        Major = accepted.NegotiatedVersion.Major;
        Minor = accepted.NegotiatedVersion.Minor;
        NegotiationGeneration = accepted.NegotiationGeneration;
        EffectiveCapabilities = accepted.EffectiveOptionalCapabilities.ToHashSet(StringComparer.Ordinal);
    }

    public void Reset()
    {
        Major = 0;
        Minor = 0;
        NegotiationGeneration = 0;
        EffectiveCapabilities = new HashSet<string>(StringComparer.Ordinal);
    }
}
