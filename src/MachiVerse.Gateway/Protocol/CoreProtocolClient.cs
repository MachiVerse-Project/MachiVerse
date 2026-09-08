using Grpc.Core;
using Grpc.Net.Client;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Protocol;

public sealed class CoreProtocolClient : IAsyncDisposable
{
    private readonly GrpcChannel _channel;
    private readonly MachiVerseInternalProtocolV1.MachiVerseInternalProtocolV1Client _client;

    public CoreProtocolClient(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri) throw new ArgumentException("Core endpoint must be absolute.", nameof(endpoint));
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

public sealed class ProtocolNegotiationState
{
    public const string ProtocolId = "mv.core-gateway";

    public uint NegotiationGeneration { get; private set; }
    public uint Major { get; private set; }
    public uint Minor { get; private set; }
    public IReadOnlySet<string> EffectiveCapabilities { get; private set; } = new HashSet<string>(StringComparer.Ordinal);
    public bool IsNegotiated => NegotiationGeneration != 0;

    public void Accept(ProtocolAcceptV1 accepted)
    {
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

public sealed record SchedulingPolicySnapshot(
    ulong OwnerConfigGeneration,
    uint MinLeadSteps,
    uint? DefaultDeadlineWindowSteps,
    uint GraceSteps,
    LatePolicyWireV1 LatePolicy);

public sealed class SchedulingPolicyProjection
{
    private SchedulingPolicySnapshot? _current;

    public SchedulingPolicySnapshot? Current => Volatile.Read(ref _current);

    public SchedulingPolicySnapshot Apply(OperationSchedulingPolicyWireV1 wire)
    {
        ArgumentNullException.ThrowIfNull(wire);
        if (wire.OwnerConfigGeneration == 0)
            throw new InvalidDataException("protocol.invalid-scheduling-policy-generation");
        if ((int)wire.LatePolicy is not (1 or 2))
            throw new InvalidDataException("protocol.invalid-scheduling-late-policy");

        var current = Current;
        if (current is not null && wire.OwnerConfigGeneration < current.OwnerConfigGeneration)
            throw new InvalidDataException("protocol.stale-scheduling-policy-generation");

        var next = new SchedulingPolicySnapshot(
            wire.OwnerConfigGeneration,
            wire.MinLeadSteps,
            wire.HasDefaultDeadlineWindowSteps ? wire.DefaultDeadlineWindowSteps : null,
            wire.GraceSteps,
            wire.LatePolicy);
        Volatile.Write(ref _current, next);
        return next;
    }
}
