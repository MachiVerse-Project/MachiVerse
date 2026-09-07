using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Google.Protobuf;
using MachiVerse.Protocol.V1;
using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Protocol;

public enum CoreGatewayMessageDirectionV1 : byte
{
    GatewayToCore = 1,
    CoreToGateway = 2,
}

public enum WorldContextPolicyV1 : byte
{
    None = 0,
    Optional = 1,
    Required = 2,
    RequiredWithBasisStep = 3,
    RequiredWithMasterGeneration = 4,
    RequiredWithConfigGeneration = 5,
}

public enum OperationContextPolicyV1 : byte
{
    None = 0,
    Optional = 1,
    BatchRequired = 2,
}

public sealed record CoreGatewayMessageRegistryEntryV1(
    StableToken MessageType,
    StableToken PayloadSchemaId,
    CoreGatewayMessageDirectionV1 Direction,
    WorldContextPolicyV1 WorldContextPolicy,
    OperationContextPolicyV1 OperationContextPolicy,
    bool Bootstrap = false);

public static class CoreGatewayProtocolRegistryV1
{
    public const string ProtocolId = "mv.core-gateway";
    public const uint ProtocolMajor = 1;
    public const uint ProtocolMinor = 0;
    public const int MaxSerializedEnvelopeBytes = 8 * 1024 * 1024;
    public const int MaxPublicationChunkBytes = 1024 * 1024;
    public const uint MaxPublicationChunks = 65535;

    private static readonly IReadOnlyDictionary<string, CoreGatewayMessageRegistryEntryV1> EntriesByType =
        new Dictionary<string, CoreGatewayMessageRegistryEntryV1>(StringComparer.Ordinal)
        {
            ["protocol.hello"] = Entry("protocol.hello", "protocol.hello", CoreGatewayMessageDirectionV1.GatewayToCore, WorldContextPolicyV1.None, OperationContextPolicyV1.None, bootstrap: true),
            ["protocol.accept"] = Entry("protocol.accept", "protocol.accept", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.None, OperationContextPolicyV1.None, bootstrap: true),
            ["protocol.reject"] = Entry("protocol.reject", "protocol.reject", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.None, OperationContextPolicyV1.None, bootstrap: true),
            ["operation.batch.submit"] = Entry("operation.batch.submit", "protocol.operation-batch", CoreGatewayMessageDirectionV1.GatewayToCore, WorldContextPolicyV1.RequiredWithMasterGeneration, OperationContextPolicyV1.BatchRequired),
            ["operation.batch.result"] = Entry("operation.batch.result", "protocol.operation-batch-result", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.Required, OperationContextPolicyV1.BatchRequired),
            ["operation.status.query"] = Entry("operation.status.query", "protocol.operation-status-query", CoreGatewayMessageDirectionV1.GatewayToCore, WorldContextPolicyV1.Required, OperationContextPolicyV1.None),
            ["operation.status.result"] = Entry("operation.status.result", "protocol.operation-status-result", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.Required, OperationContextPolicyV1.Optional),
            ["world.state.begin"] = Entry("world.state.begin", "protocol.state-publication", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.RequiredWithBasisStep, OperationContextPolicyV1.None),
            ["world.state.chunk"] = Entry("world.state.chunk", "protocol.state-publication-chunk", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.RequiredWithBasisStep, OperationContextPolicyV1.None),
            ["world.state.resync-request"] = Entry("world.state.resync-request", "protocol.state-resync-request", CoreGatewayMessageDirectionV1.GatewayToCore, WorldContextPolicyV1.Required, OperationContextPolicyV1.None),
            ["world.scheduling-policy"] = Entry("world.scheduling-policy", "protocol.scheduling-policy", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.RequiredWithConfigGeneration, OperationContextPolicyV1.None),
            ["master.generation.changed"] = Entry("master.generation.changed", "protocol.master-generation", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.RequiredWithMasterGeneration, OperationContextPolicyV1.None),
            ["component.health"] = Entry("component.health", "protocol.component-health", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.Optional, OperationContextPolicyV1.None),
        };

    public static IReadOnlyCollection<CoreGatewayMessageRegistryEntryV1> Entries => EntriesByType.Values;

    public static CoreGatewayMessageRegistryEntryV1 Get(string messageType)
        => EntriesByType.TryGetValue(messageType, out var entry)
            ? entry
            : throw new CoreGatewayProtocolException("protocol.unknown-message-type", $"Unknown mv.core-gateway message type: {messageType}");

    private static CoreGatewayMessageRegistryEntryV1 Entry(
        string messageType,
        string payloadSchemaId,
        CoreGatewayMessageDirectionV1 direction,
        WorldContextPolicyV1 worldContextPolicy,
        OperationContextPolicyV1 operationContextPolicy,
        bool bootstrap = false)
        => new(new StableToken(messageType), new StableToken(payloadSchemaId), direction, worldContextPolicy, operationContextPolicy, bootstrap);
}

public sealed class CoreGatewayProtocolException : InvalidDataException
{
    public CoreGatewayProtocolException(string code, string diagnostic, Exception? innerException = null)
        : base(diagnostic, innerException)
    {
        Code = new StableToken(code);
    }

    public StableToken Code { get; }
}

public static partial class CoreGatewayWireValidatorV1
{
    public static WireEnvelopeV1 DecodeAndValidate(
        ReadOnlySpan<byte> serialized,
        CoreGatewayMessageDirectionV1 expectedDirection,
        uint? expectedNegotiationGeneration = null)
    {
        if (serialized.Length > CoreGatewayProtocolRegistryV1.MaxSerializedEnvelopeBytes)
            throw Error("protocol.limit-exceeded", "WireEnvelope exceeds 8 MiB.");

        WireEnvelopeV1 envelope;
        try
        {
            envelope = WireEnvelopeV1.Parser.ParseFrom(serialized.ToArray());
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw Error("protocol.malformed", "WireEnvelope protobuf decode failed.", ex);
        }

        Validate(envelope, expectedDirection, expectedNegotiationGeneration);
        return envelope;
    }

    public static void Validate(
        WireEnvelopeV1 envelope,
        CoreGatewayMessageDirectionV1 expectedDirection,
        uint? expectedNegotiationGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.CalculateSize() > CoreGatewayProtocolRegistryV1.MaxSerializedEnvelopeBytes)
            throw Error("protocol.limit-exceeded", "WireEnvelope exceeds 8 MiB.");
        if (envelope.EnvelopeVersion != 1)
            throw Error("protocol.malformed", "Unsupported envelope_version.");
        ValidateStableToken(envelope.ProtocolId, "protocol_id");
        if (!string.Equals(envelope.ProtocolId, CoreGatewayProtocolRegistryV1.ProtocolId, StringComparison.Ordinal))
            throw Error("protocol.wrong-protocol", "ProtocolId does not match mv.core-gateway.");
        ValidateStableToken(envelope.MessageType, "message_type");
        ValidateStableToken(envelope.PayloadSchemaId, "payload_schema_id");
        ValidateId128(envelope.MessageId, "message_id", allowZero: false);
        ValidateId128(envelope.CorrelationId, "correlation_id", allowZero: false);
        ValidateId128(envelope.SenderInstanceId, "sender_instance_id", allowZero: false);
        if (envelope.HasCausationId) ValidateId128(envelope.CausationId, "causation_id", allowZero: false);

        var registry = CoreGatewayProtocolRegistryV1.Get(envelope.MessageType);
        if (registry.Direction != expectedDirection)
            throw Error("protocol.unknown-message-type", "Message direction is invalid for this endpoint.");
        if (!string.Equals(registry.PayloadSchemaId.Value, envelope.PayloadSchemaId, StringComparison.Ordinal))
            throw Error("protocol.payload-schema-mismatch", "MessageType and payload_schema_id do not match the standard registry.");
        if (envelope.PayloadSchemaVersion is null || envelope.PayloadSchemaVersion.Major != 1 || envelope.PayloadSchemaVersion.Minor != 0)
            throw Error("protocol.schema-unsupported", "Only payload schema 1.0 is supported by SIM-14.");

        if (registry.Bootstrap)
        {
            if (envelope.NegotiationGeneration != 0)
                throw Error("protocol.negotiation-stale", "Bootstrap messages require negotiation_generation=0.");
            if (envelope.ProtocolVersion is null || envelope.ProtocolVersion.Major != 0 || envelope.ProtocolVersion.Minor != 0)
                throw Error("protocol.version-incompatible", "Bootstrap messages require protocol_version=0.0.");
        }
        else
        {
            if (envelope.ProtocolVersion is null ||
                envelope.ProtocolVersion.Major != CoreGatewayProtocolRegistryV1.ProtocolMajor ||
                envelope.ProtocolVersion.Minor != CoreGatewayProtocolRegistryV1.ProtocolMinor)
                throw Error("protocol.version-incompatible", "Normal message protocol version does not match negotiated 1.0.");
            if (envelope.NegotiationGeneration == 0 ||
                expectedNegotiationGeneration is { } expected && envelope.NegotiationGeneration != expected)
                throw Error("protocol.negotiation-stale", "Negotiation generation is not current.");
        }

        if ((int)envelope.PayloadCompression != (int)CompressionKindV1.None)
            throw Error("protocol.capability-missing", "Only required compression NONE is enabled by the Core profile.");

        ValidateContext(envelope, registry);
    }

    public static void ValidateStableToken(string value, string field)
    {
        if (value is null || !StableTokenPattern().IsMatch(value))
            throw Error("protocol.malformed", $"Invalid StableToken in {field}.");
    }

    public static byte[] ValidateId128(ByteString value, string field, bool allowZero)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 16)
            throw Error("protocol.invalid-id", $"{field} must be exactly 16 bytes.");
        var bytes = value.ToByteArray();
        if (!allowZero && bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0)
            throw Error("protocol.invalid-id", $"{field} cannot be ZERO.");
        return bytes;
    }

    public static byte[] ValidateHash256(ByteString value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 32)
            throw Error("protocol.malformed", $"{field} must be exactly 32 bytes.");
        return value.ToByteArray();
    }

    private static void ValidateContext(WireEnvelopeV1 envelope, CoreGatewayMessageRegistryEntryV1 registry)
    {
        switch (registry.WorldContextPolicy)
        {
            case WorldContextPolicyV1.None:
                if (envelope.WorldContext is not null)
                    throw Error("protocol.malformed", "WorldContext is forbidden for this message.");
                break;
            case WorldContextPolicyV1.Optional:
                if (envelope.WorldContext is not null) ValidateWorldContext(envelope.WorldContext);
                break;
            case WorldContextPolicyV1.Required:
                RequireWorld(envelope);
                break;
            case WorldContextPolicyV1.RequiredWithBasisStep:
                RequireWorld(envelope);
                if (!envelope.WorldContext!.HasBasisStep)
                    throw Error("protocol.missing-required", "basis_step is required for confirmed publication.");
                break;
            case WorldContextPolicyV1.RequiredWithMasterGeneration:
                RequireWorld(envelope);
                if (!envelope.WorldContext!.HasMasterGeneration || envelope.WorldContext.MasterGeneration == 0)
                    throw Error("protocol.missing-required", "master_generation is required.");
                break;
            case WorldContextPolicyV1.RequiredWithConfigGeneration:
                RequireWorld(envelope);
                if (!envelope.WorldContext!.HasConfigGeneration || envelope.WorldContext.ConfigGeneration == 0)
                    throw Error("protocol.missing-required", "config_generation is required.");
                break;
            default:
                throw Error("protocol.malformed", "Unknown WorldContext policy.");
        }

        switch (registry.OperationContextPolicy)
        {
            case OperationContextPolicyV1.None:
                if (envelope.OperationContext is not null)
                    throw Error("protocol.malformed", "OperationContext is forbidden for this message.");
                break;
            case OperationContextPolicyV1.Optional:
                if (envelope.OperationContext is not null) ValidateOperationContext(envelope.OperationContext);
                break;
            case OperationContextPolicyV1.BatchRequired:
                if (envelope.OperationContext is null || !envelope.OperationContext.HasBatchId)
                    throw Error("protocol.missing-required", "Batch OperationContext is required.");
                ValidateOperationContext(envelope.OperationContext);
                break;
            default:
                throw Error("protocol.malformed", "Unknown OperationContext policy.");
        }
    }

    private static void RequireWorld(WireEnvelopeV1 envelope)
    {
        if (envelope.WorldContext is null)
            throw Error("protocol.missing-required", "WorldContext is required.");
        ValidateWorldContext(envelope.WorldContext);
    }

    private static void ValidateWorldContext(WorldContextWireV1 context)
    {
        ValidateId128(context.WorldId, "world_id", allowZero: false);
        if (context.HasMasterGeneration && context.MasterGeneration == 0)
            throw Error("protocol.field-out-of-range", "MasterGeneration starts at 1.");
        if (context.HasConfigGeneration && context.ConfigGeneration == 0)
            throw Error("protocol.field-out-of-range", "ConfigGeneration starts at 1.");
    }

    private static void ValidateOperationContext(OperationContextWireV1 context)
    {
        if (!context.HasOperationId && !context.HasBatchId)
            throw Error("protocol.missing-required", "OperationContext requires operation_id or batch_id.");
        if (context.HasOperationId)
        {
            ValidateId128(context.OperationId, "operation_id", allowZero: false);
            if (context.HasOperationPayloadDigest) ValidateHash256(context.OperationPayloadDigest, "operation_payload_digest");
        }
        else if (context.HasOperationPayloadDigest)
        {
            throw Error("protocol.malformed", "operation_payload_digest requires operation_id.");
        }
        if (context.HasBatchId) ValidateId128(context.BatchId, "batch_id", allowZero: false);
    }

    private static CoreGatewayProtocolException Error(string code, string diagnostic, Exception? inner = null)
        => new(code, diagnostic, inner);

    [GeneratedRegex("^[a-z0-9][a-z0-9._/-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableTokenPattern();
}

public sealed record SupportedProtocolRangeV1(uint Major, uint MinMinor, uint MaxMinor);

public sealed class CoreGatewayNegotiationProfileV1
{
    public CoreGatewayNegotiationProfileV1(
        IEnumerable<SupportedProtocolRangeV1>? supportedVersions = null,
        IEnumerable<string>? providedCapabilities = null,
        IEnumerable<string>? requiredCapabilities = null)
    {
        SupportedVersions = NormalizeRanges(supportedVersions ?? [new SupportedProtocolRangeV1(1, 0, 0)]);
        ProvidedCapabilities = NormalizeTokens(providedCapabilities ?? Array.Empty<string>(), "provided_capabilities");
        RequiredCapabilities = NormalizeTokens(requiredCapabilities ?? Array.Empty<string>(), "required_capabilities");
    }

    public IReadOnlyList<SupportedProtocolRangeV1> SupportedVersions { get; }
    public IReadOnlyList<string> ProvidedCapabilities { get; }
    public IReadOnlyList<string> RequiredCapabilities { get; }

    private static IReadOnlyList<SupportedProtocolRangeV1> NormalizeRanges(IEnumerable<SupportedProtocolRangeV1> source)
    {
        var ranges = source.OrderBy(static item => item.Major).ToArray();
        if (ranges.Length == 0) throw new ArgumentException("At least one protocol version range is required.", nameof(source));
        if (ranges.Select(static item => item.Major).Distinct().Count() != ranges.Length)
            throw new InvalidDataException("protocol.version-range-duplicate-major");
        foreach (var range in ranges)
        {
            if (range.Major == 0 || range.Major > ushort.MaxValue || range.MinMinor > range.MaxMinor || range.MaxMinor > ushort.MaxValue)
                throw new InvalidDataException("protocol.version-range-invalid");
        }
        return Array.AsReadOnly(ranges);
    }

    private static IReadOnlyList<string> NormalizeTokens(IEnumerable<string> source, string field)
    {
        var values = source.Order(StringComparer.Ordinal).ToArray();
        foreach (var value in values) CoreGatewayWireValidatorV1.ValidateStableToken(value, field);
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidDataException("protocol.capability-duplicate");
        return Array.AsReadOnly(values);
    }
}

public static class CoreGatewayNegotiatorV1
{
    public static ProtocolAcceptV1 Negotiate(ProtocolHelloV1 hello, CoreGatewayNegotiationProfileV1 profile)
    {
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(profile);
        CoreGatewayWireValidatorV1.ValidateStableToken(hello.ProtocolId, "hello.protocol_id");
        if (!string.Equals(hello.ProtocolId, CoreGatewayProtocolRegistryV1.ProtocolId, StringComparison.Ordinal))
            throw new CoreGatewayProtocolException("protocol.wrong-protocol", "Hello ProtocolId does not match mv.core-gateway.");

        var peerRanges = hello.SupportedVersions
            .Select(static item => new SupportedProtocolRangeV1(item.Major, item.MinMinor, item.MaxMinor))
            .OrderBy(static item => item.Major)
            .ToArray();
        if (peerRanges.Length == 0 || peerRanges.Select(static item => item.Major).Distinct().Count() != peerRanges.Length)
            throw new CoreGatewayProtocolException("protocol.version-incompatible", "Peer version ranges are empty or duplicate a major.");
        foreach (var range in peerRanges)
        {
            if (range.Major == 0 || range.Major > ushort.MaxValue || range.MinMinor > range.MaxMinor || range.MaxMinor > ushort.MaxValue)
                throw new CoreGatewayProtocolException("protocol.version-incompatible", "Peer version range is invalid.");
        }

        var peerProvided = NormalizePeerTokens(hello.ProvidedCapabilities, "provided_capabilities");
        var peerRequired = NormalizePeerTokens(hello.RequiredCapabilities, "required_capabilities");
        foreach (var required in peerRequired)
        {
            if (!profile.ProvidedCapabilities.Contains(required, StringComparer.Ordinal))
                throw new CoreGatewayProtocolException("protocol.capability-missing", $"Core does not provide required capability: {required}");
        }
        foreach (var required in profile.RequiredCapabilities)
        {
            if (!peerProvided.Contains(required, StringComparer.Ordinal))
                throw new CoreGatewayProtocolException("protocol.capability-missing", $"Gateway does not provide required capability: {required}");
        }

        (uint Major, uint Minor)? selected = null;
        foreach (var coreRange in profile.SupportedVersions.OrderByDescending(static item => item.Major))
        {
            var peer = peerRanges.FirstOrDefault(item => item.Major == coreRange.Major);
            if (peer is null) continue;
            var min = Math.Max(coreRange.MinMinor, peer.MinMinor);
            var max = Math.Min(coreRange.MaxMinor, peer.MaxMinor);
            if (min > max) continue;
            selected = (coreRange.Major, max);
            break;
        }
        if (selected is null)
            throw new CoreGatewayProtocolException("protocol.version-incompatible", "No common protocol version exists.");

        var effectiveOptional = profile.ProvidedCapabilities
            .Intersect(peerProvided, StringComparer.Ordinal)
            .Except(profile.RequiredCapabilities, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var result = new ProtocolAcceptV1
        {
            NegotiatedVersion = new ProtocolVersionV1 { Major = selected.Value.Major, Minor = selected.Value.Minor },
            NegotiationGeneration = 1,
        };
        result.EffectiveOptionalCapabilities.AddRange(effectiveOptional);
        return result;
    }

    private static IReadOnlyList<string> NormalizePeerTokens(IEnumerable<string> source, string field)
    {
        var values = source.Order(StringComparer.Ordinal).ToArray();
        foreach (var value in values) CoreGatewayWireValidatorV1.ValidateStableToken(value, field);
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new CoreGatewayProtocolException("protocol.capability-missing", $"Duplicate capability in {field}.");
        return values;
    }
}

public interface IProtocolMessageIdSourceV1
{
    OpaqueId128 Next();
}

public sealed class RandomProtocolMessageIdSourceV1 : IProtocolMessageIdSourceV1
{
    public OpaqueId128 Next()
    {
        Span<byte> bytes = stackalloc byte[16];
        do RandomNumberGenerator.Fill(bytes);
        while (bytes.IndexOfAnyExcept((byte)0) < 0);
        return OpaqueId128.FromBytes(bytes);
    }
}

public sealed class CoreGatewayEnvelopeFactoryV1
{
    private readonly OpaqueId128 _coreInstanceId;
    private readonly IProtocolMessageIdSourceV1 _messageIds;

    public CoreGatewayEnvelopeFactoryV1(OpaqueId128 coreInstanceId, IProtocolMessageIdSourceV1? messageIds = null)
    {
        if (coreInstanceId.IsZero) throw new ArgumentException("Core instance id cannot be ZERO.", nameof(coreInstanceId));
        _coreInstanceId = coreInstanceId;
        _messageIds = messageIds ?? new RandomProtocolMessageIdSourceV1();
    }

    public WireEnvelopeV1 BootstrapResponse(WireEnvelopeV1 request, string messageType, IMessage payload)
        => Create(request, messageType, payload, negotiationGeneration: 0, bootstrap: true, worldContext: null, operationContext: null);

    public WireEnvelopeV1 NormalResponse(
        WireEnvelopeV1 request,
        string messageType,
        IMessage payload,
        uint negotiationGeneration,
        WorldContextWireV1? worldContext = null,
        OperationContextWireV1? operationContext = null)
        => Create(request, messageType, payload, negotiationGeneration, bootstrap: false, worldContext, operationContext);

    public WireEnvelopeV1 Notification(
        string messageType,
        IMessage payload,
        uint negotiationGeneration,
        OpaqueId128 correlationId,
        WorldContextWireV1? worldContext = null,
        OperationContextWireV1? operationContext = null)
    {
        var registry = CoreGatewayProtocolRegistryV1.Get(messageType);
        if (registry.Direction != CoreGatewayMessageDirectionV1.CoreToGateway || registry.Bootstrap)
            throw new InvalidOperationException("Notification requires a normal Core-to-Gateway registry entry.");
        var envelope = Base(messageType, registry.PayloadSchemaId.Value, payload, negotiationGeneration, bootstrap: false, correlationId, causationId: null);
        envelope.WorldContext = worldContext;
        envelope.OperationContext = operationContext;
        CoreGatewayWireValidatorV1.Validate(envelope, CoreGatewayMessageDirectionV1.CoreToGateway, negotiationGeneration);
        return envelope;
    }

    private WireEnvelopeV1 Create(
        WireEnvelopeV1 request,
        string messageType,
        IMessage payload,
        uint negotiationGeneration,
        bool bootstrap,
        WorldContextWireV1? worldContext,
        OperationContextWireV1? operationContext)
    {
        ArgumentNullException.ThrowIfNull(request);
        var registry = CoreGatewayProtocolRegistryV1.Get(messageType);
        if (registry.Direction != CoreGatewayMessageDirectionV1.CoreToGateway || registry.Bootstrap != bootstrap)
            throw new InvalidOperationException("Response message registry direction/bootstrap mismatch.");
        var envelope = Base(
            messageType,
            registry.PayloadSchemaId.Value,
            payload,
            negotiationGeneration,
            bootstrap,
            OpaqueId128.FromBytes(request.CorrelationId.Span),
            OpaqueId128.FromBytes(request.MessageId.Span));
        envelope.WorldContext = worldContext;
        envelope.OperationContext = operationContext;
        CoreGatewayWireValidatorV1.Validate(envelope, CoreGatewayMessageDirectionV1.CoreToGateway, bootstrap ? null : negotiationGeneration);
        return envelope;
    }

    private WireEnvelopeV1 Base(
        string messageType,
        string payloadSchemaId,
        IMessage payload,
        uint negotiationGeneration,
        bool bootstrap,
        OpaqueId128 correlationId,
        OpaqueId128? causationId)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var envelope = new WireEnvelopeV1
        {
            EnvelopeVersion = 1,
            ProtocolId = CoreGatewayProtocolRegistryV1.ProtocolId,
            ProtocolVersion = new ProtocolVersionV1
            {
                Major = bootstrap ? 0u : CoreGatewayProtocolRegistryV1.ProtocolMajor,
                Minor = bootstrap ? 0u : CoreGatewayProtocolRegistryV1.ProtocolMinor,
            },
            NegotiationGeneration = negotiationGeneration,
            MessageType = messageType,
            MessageId = ByteString.CopyFrom(_messageIds.Next().ToBytes()),
            CorrelationId = ByteString.CopyFrom(correlationId.ToBytes()),
            SenderInstanceId = ByteString.CopyFrom(_coreInstanceId.ToBytes()),
            PayloadSchemaId = payloadSchemaId,
            PayloadSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 },
            PayloadCompression = CompressionKindV1.None,
            Payload = payload.ToByteString(),
        };
        if (causationId is { } cause) envelope.CausationId = ByteString.CopyFrom(cause.ToBytes());
        return envelope;
    }
}
