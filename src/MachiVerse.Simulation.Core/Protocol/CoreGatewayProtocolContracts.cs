using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Google.Protobuf;
using MachiVerse.Protocol.V1;
using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Protocol;

public enum CoreGatewayMessageDirectionV1 : byte { GatewayToCore = 1, CoreToGateway = 2 }
public enum WorldContextPolicyV1 : byte { None = 0, Optional = 1, Required = 2, RequiredWithBasisStep = 3, RequiredWithMasterGeneration = 4, RequiredWithConfigGeneration = 5 }
public enum OperationContextPolicyV1 : byte { None = 0, Optional = 1, BatchRequired = 2 }

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

    public static readonly IReadOnlyList<string> BaselineCapabilities = Array.AsReadOnly(new[]
    {
        "protocol.operation-batch.v1",
        "protocol.operation-status.v1",
        "protocol.protobuf.v1",
        "protocol.state-full.v1",
    });

    private static readonly IReadOnlyDictionary<string, CoreGatewayMessageRegistryEntryV1> EntriesByType =
        new Dictionary<string, CoreGatewayMessageRegistryEntryV1>(StringComparer.Ordinal)
        {
            ["protocol.hello"] = E("protocol.hello", "protocol.hello.v1", CoreGatewayMessageDirectionV1.GatewayToCore, WorldContextPolicyV1.None, OperationContextPolicyV1.None, true),
            ["protocol.accept"] = E("protocol.accept", "protocol.accept.v1", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.None, OperationContextPolicyV1.None, true),
            ["protocol.reject"] = E("protocol.reject", "protocol.reject.v1", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.None, OperationContextPolicyV1.None, true),
            ["gateway.register"] = E("gateway.register", "protocol.gateway-register.v1", CoreGatewayMessageDirectionV1.GatewayToCore, WorldContextPolicyV1.None, OperationContextPolicyV1.None),
            ["gateway.heartbeat"] = E("gateway.heartbeat", "protocol.gateway-heartbeat.v1", CoreGatewayMessageDirectionV1.GatewayToCore, WorldContextPolicyV1.Optional, OperationContextPolicyV1.None),
            ["gateway.role-state"] = E("gateway.role-state", "protocol.gateway-role-state.v1", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.RequiredWithMasterGeneration, OperationContextPolicyV1.None),
            ["master.generation.changed"] = E("master.generation.changed", "protocol.master-generation-state.v1", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.RequiredWithMasterGeneration, OperationContextPolicyV1.None),
            ["world.scheduling-policy"] = E("world.scheduling-policy", "protocol.scheduling-policy.v1", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.RequiredWithConfigGeneration, OperationContextPolicyV1.None),
            ["operation.batch.submit"] = E("operation.batch.submit", "protocol.operation-batch.v1", CoreGatewayMessageDirectionV1.GatewayToCore, WorldContextPolicyV1.RequiredWithMasterGeneration, OperationContextPolicyV1.BatchRequired),
            ["operation.batch.result"] = E("operation.batch.result", "protocol.operation-batch-result.v1", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.Required, OperationContextPolicyV1.BatchRequired),
            ["operation.status.query"] = E("operation.status.query", "protocol.operation-status-query.v1", CoreGatewayMessageDirectionV1.GatewayToCore, WorldContextPolicyV1.Required, OperationContextPolicyV1.None),
            ["operation.status.result"] = E("operation.status.result", "protocol.operation-status-result.v1", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.Required, OperationContextPolicyV1.None),
            ["world.state.begin"] = E("world.state.begin", "protocol.state-publication.v1", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.RequiredWithBasisStep, OperationContextPolicyV1.None),
            ["world.state.chunk"] = E("world.state.chunk", "protocol.state-publication-chunk.v1", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.RequiredWithBasisStep, OperationContextPolicyV1.None),
            ["world.state.resync-request"] = E("world.state.resync-request", "protocol.state-resync-request.v1", CoreGatewayMessageDirectionV1.GatewayToCore, WorldContextPolicyV1.Required, OperationContextPolicyV1.None),
            ["component.health"] = E("component.health", "protocol.component-health.v1", CoreGatewayMessageDirectionV1.CoreToGateway, WorldContextPolicyV1.Optional, OperationContextPolicyV1.None),
        };

    public static IReadOnlyCollection<CoreGatewayMessageRegistryEntryV1> Entries => EntriesByType.Values;
    public static CoreGatewayMessageRegistryEntryV1 Get(string type)
        => EntriesByType.TryGetValue(type, out var entry)
            ? entry
            : throw new CoreGatewayProtocolException("protocol.unknown-message-type", $"Unknown mv.core-gateway message type: {type}");

    private static CoreGatewayMessageRegistryEntryV1 E(string type, string schema, CoreGatewayMessageDirectionV1 direction, WorldContextPolicyV1 world, OperationContextPolicyV1 op, bool bootstrap = false)
        => new(new StableToken(type), new StableToken(schema), direction, world, op, bootstrap);
}

public sealed class CoreGatewayProtocolException : Exception
{
    public CoreGatewayProtocolException(string code, string diagnostic, Exception? inner = null) : base(diagnostic, inner)
        => Code = new StableToken(code);
    public StableToken Code { get; }
}

public static partial class CoreGatewayWireValidatorV1
{
    public static WireEnvelopeV1 DecodeAndValidate(ReadOnlySpan<byte> serialized, CoreGatewayMessageDirectionV1 direction, uint? generation = null)
    {
        if (serialized.Length > CoreGatewayProtocolRegistryV1.MaxSerializedEnvelopeBytes) throw Error("protocol.limit-exceeded", "WireEnvelope exceeds 8 MiB.");
        try
        {
            var envelope = WireEnvelopeV1.Parser.ParseFrom(serialized.ToArray());
            Validate(envelope, direction, generation);
            return envelope;
        }
        catch (InvalidProtocolBufferException ex) { throw Error("protocol.malformed", "WireEnvelope protobuf decode failed.", ex); }
    }

    public static void Validate(WireEnvelopeV1 envelope, CoreGatewayMessageDirectionV1 direction, uint? generation = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.CalculateSize() > CoreGatewayProtocolRegistryV1.MaxSerializedEnvelopeBytes) throw Error("protocol.limit-exceeded", "WireEnvelope exceeds 8 MiB.");
        if (envelope.EnvelopeVersion != 1) throw Error("protocol.malformed", "Unsupported envelope_version.");
        ValidateStableToken(envelope.ProtocolId, "protocol_id");
        if (!string.Equals(envelope.ProtocolId, CoreGatewayProtocolRegistryV1.ProtocolId, StringComparison.Ordinal)) throw Error("protocol.wrong-protocol", "ProtocolId does not match mv.core-gateway.");
        ValidateStableToken(envelope.MessageType, "message_type");
        ValidateStableToken(envelope.PayloadSchemaId, "payload_schema_id");
        ValidateId128(envelope.MessageId, "message_id", false);
        ValidateId128(envelope.CorrelationId, "correlation_id", false);
        ValidateId128(envelope.SenderInstanceId, "sender_instance_id", false);
        if (envelope.HasCausationId) ValidateId128(envelope.CausationId, "causation_id", false);

        var entry = CoreGatewayProtocolRegistryV1.Get(envelope.MessageType);
        if (entry.Direction != direction) throw Error("protocol.unknown-message-type", "Message direction is invalid.");
        if (!string.Equals(entry.PayloadSchemaId.Value, envelope.PayloadSchemaId, StringComparison.Ordinal)) throw Error("protocol.payload-schema-mismatch", "MessageType/payload schema mismatch.");
        if (envelope.PayloadSchemaVersion is null || envelope.PayloadSchemaVersion.Major != 1 || envelope.PayloadSchemaVersion.Minor != 0) throw Error("protocol.schema-unsupported", "Payload schema must be 1.0.");

        if (entry.Bootstrap)
        {
            if (envelope.NegotiationGeneration != 0) throw Error("protocol.negotiation-stale", "Bootstrap generation must be 0.");
            if (envelope.ProtocolVersion is null || envelope.ProtocolVersion.Major != 0 || envelope.ProtocolVersion.Minor != 0) throw Error("protocol.version-incompatible", "Bootstrap protocol version must be 0.0.");
        }
        else
        {
            if (envelope.ProtocolVersion is null || envelope.ProtocolVersion.Major != 1 || envelope.ProtocolVersion.Minor != 0) throw Error("protocol.version-incompatible", "Normal protocol version must be 1.0.");
            if (envelope.NegotiationGeneration == 0 || generation is { } current && current != envelope.NegotiationGeneration) throw Error("protocol.negotiation-stale", "Negotiation generation is stale.");
        }
        if ((int)envelope.PayloadCompression != (int)CompressionKindV1.None) throw Error("protocol.capability-missing", "Compression NONE is required by baseline profile.");
        ValidateContexts(envelope, entry);
    }

    public static void ValidateStableToken(string value, string field)
    {
        if (value is null || !StableTokenPattern().IsMatch(value)) throw Error("protocol.malformed", $"Invalid StableToken in {field}.");
    }

    public static byte[] ValidateId128(ByteString value, string field, bool allowZero)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 16) throw Error("protocol.invalid-id", $"{field} must be exactly 16 bytes.");
        var bytes = value.ToByteArray();
        if (!allowZero && bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0) throw Error("protocol.invalid-id", $"{field} cannot be ZERO.");
        return bytes;
    }

    public static byte[] ValidateHash256(ByteString value, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 32) throw Error("protocol.malformed", $"{field} must be exactly 32 bytes.");
        return value.ToByteArray();
    }

    private static void ValidateContexts(WireEnvelopeV1 envelope, CoreGatewayMessageRegistryEntryV1 entry)
    {
        switch (entry.WorldContextPolicy)
        {
            case WorldContextPolicyV1.None:
                if (envelope.WorldContext is not null) throw Error("protocol.malformed", "WorldContext forbidden.");
                break;
            case WorldContextPolicyV1.Optional:
                if (envelope.WorldContext is not null) ValidateWorld(envelope.WorldContext);
                break;
            default:
                if (envelope.WorldContext is null) throw Error("protocol.missing-required", "WorldContext required.");
                ValidateWorld(envelope.WorldContext);
                if (entry.WorldContextPolicy == WorldContextPolicyV1.RequiredWithBasisStep && !envelope.WorldContext.HasBasisStep) throw Error("protocol.missing-required", "basis_step required.");
                if (entry.WorldContextPolicy == WorldContextPolicyV1.RequiredWithMasterGeneration && (!envelope.WorldContext.HasMasterGeneration || envelope.WorldContext.MasterGeneration == 0)) throw Error("protocol.missing-required", "master_generation required.");
                if (entry.WorldContextPolicy == WorldContextPolicyV1.RequiredWithConfigGeneration && (!envelope.WorldContext.HasConfigGeneration || envelope.WorldContext.ConfigGeneration == 0)) throw Error("protocol.missing-required", "config_generation required.");
                break;
        }

        switch (entry.OperationContextPolicy)
        {
            case OperationContextPolicyV1.None:
                if (envelope.OperationContext is not null) throw Error("protocol.malformed", "OperationContext forbidden.");
                break;
            case OperationContextPolicyV1.Optional:
                if (envelope.OperationContext is not null) ValidateOperation(envelope.OperationContext);
                break;
            case OperationContextPolicyV1.BatchRequired:
                if (envelope.OperationContext is null || !envelope.OperationContext.HasBatchId) throw Error("protocol.missing-required", "Batch OperationContext required.");
                ValidateOperation(envelope.OperationContext);
                break;
        }
    }

    private static void ValidateWorld(WorldContextWireV1 context)
    {
        ValidateId128(context.WorldId, "world_id", false);
        if (context.HasMasterGeneration && context.MasterGeneration == 0) throw Error("protocol.field-out-of-range", "MasterGeneration starts at 1.");
        if (context.HasConfigGeneration && context.ConfigGeneration == 0) throw Error("protocol.field-out-of-range", "ConfigGeneration starts at 1.");
    }

    private static void ValidateOperation(OperationContextWireV1 context)
    {
        if (!context.HasOperationId && !context.HasBatchId) throw Error("protocol.missing-required", "OperationContext requires operation_id or batch_id.");
        if (context.HasOperationId)
        {
            ValidateId128(context.OperationId, "operation_id", false);
            if (context.HasOperationPayloadDigest) ValidateHash256(context.OperationPayloadDigest, "operation_payload_digest");
        }
        else if (context.HasOperationPayloadDigest) throw Error("protocol.malformed", "operation_payload_digest requires operation_id.");
        if (context.HasBatchId) ValidateId128(context.BatchId, "batch_id", false);
    }

    private static CoreGatewayProtocolException Error(string code, string diagnostic, Exception? inner = null) => new(code, diagnostic, inner);
    [GeneratedRegex("^[a-z0-9][a-z0-9._/-]{0,63}$", RegexOptions.CultureInvariant)] private static partial Regex StableTokenPattern();
}

public sealed record SupportedProtocolRangeV1(uint Major, uint MinMinor, uint MaxMinor);

public sealed class CoreGatewayNegotiationProfileV1
{
    public CoreGatewayNegotiationProfileV1(IEnumerable<SupportedProtocolRangeV1>? versions = null, IEnumerable<string>? provided = null, IEnumerable<string>? required = null)
    {
        SupportedVersions = NormalizeRanges(versions ?? [new SupportedProtocolRangeV1(1, 0, 0)]);
        ProvidedCapabilities = NormalizeTokens(provided ?? CoreGatewayProtocolRegistryV1.BaselineCapabilities);
        RequiredCapabilities = NormalizeTokens(required ?? CoreGatewayProtocolRegistryV1.BaselineCapabilities);
    }
    public IReadOnlyList<SupportedProtocolRangeV1> SupportedVersions { get; }
    public IReadOnlyList<string> ProvidedCapabilities { get; }
    public IReadOnlyList<string> RequiredCapabilities { get; }

    private static IReadOnlyList<SupportedProtocolRangeV1> NormalizeRanges(IEnumerable<SupportedProtocolRangeV1> values)
    {
        var result = values.OrderBy(static x => x.Major).ToArray();
        if (result.Length == 0 || result.Select(static x => x.Major).Distinct().Count() != result.Length) throw new InvalidDataException("protocol.version-range-invalid");
        if (result.Any(static x => x.Major == 0 || x.Major > ushort.MaxValue || x.MinMinor > x.MaxMinor || x.MaxMinor > ushort.MaxValue)) throw new InvalidDataException("protocol.version-range-invalid");
        return Array.AsReadOnly(result);
    }

    private static IReadOnlyList<string> NormalizeTokens(IEnumerable<string> values)
    {
        var result = values.Order(StringComparer.Ordinal).ToArray();
        foreach (var value in result) CoreGatewayWireValidatorV1.ValidateStableToken(value, "capability");
        if (result.Distinct(StringComparer.Ordinal).Count() != result.Length) throw new InvalidDataException("protocol.capability-duplicate");
        return Array.AsReadOnly(result);
    }
}

public static class CoreGatewayNegotiatorV1
{
    public static ProtocolAcceptV1 Negotiate(ProtocolHelloV1 hello, CoreGatewayNegotiationProfileV1 profile)
    {
        ArgumentNullException.ThrowIfNull(hello); ArgumentNullException.ThrowIfNull(profile);
        CoreGatewayWireValidatorV1.ValidateStableToken(hello.ProtocolId, "hello.protocol_id");
        if (!string.Equals(hello.ProtocolId, CoreGatewayProtocolRegistryV1.ProtocolId, StringComparison.Ordinal)) throw new CoreGatewayProtocolException("protocol.wrong-protocol", "Hello ProtocolId mismatch.");
        var peerRanges = hello.SupportedVersions.Select(static x => new SupportedProtocolRangeV1(x.Major, x.MinMinor, x.MaxMinor)).OrderBy(static x => x.Major).ToArray();
        if (peerRanges.Length == 0 || peerRanges.Select(static x => x.Major).Distinct().Count() != peerRanges.Length || peerRanges.Any(static x => x.Major == 0 || x.Major > ushort.MaxValue || x.MinMinor > x.MaxMinor || x.MaxMinor > ushort.MaxValue)) throw new CoreGatewayProtocolException("protocol.version-incompatible", "Peer version ranges invalid.");
        var peerProvided = NormalizeCapabilities(hello.ProvidedCapabilities);
        var peerRequired = NormalizeCapabilities(hello.RequiredCapabilities);
        if (peerRequired.Any(required => !profile.ProvidedCapabilities.Contains(required, StringComparer.Ordinal)) || profile.RequiredCapabilities.Any(required => !peerProvided.Contains(required, StringComparer.Ordinal))) throw new CoreGatewayProtocolException("protocol.capability-missing", "Required baseline capability is missing.");

        (uint Major, uint Minor)? selected = null;
        foreach (var local in profile.SupportedVersions.OrderByDescending(static x => x.Major))
        {
            var peer = peerRanges.FirstOrDefault(x => x.Major == local.Major);
            if (peer is null) continue;
            var low = Math.Max(local.MinMinor, peer.MinMinor); var high = Math.Min(local.MaxMinor, peer.MaxMinor);
            if (low <= high) { selected = (local.Major, high); break; }
        }
        if (selected is null) throw new CoreGatewayProtocolException("protocol.version-incompatible", "No common protocol version.");
        var accept = new ProtocolAcceptV1 { NegotiatedVersion = new ProtocolVersionV1 { Major = selected.Value.Major, Minor = selected.Value.Minor }, NegotiationGeneration = 1 };
        accept.EffectiveOptionalCapabilities.AddRange(profile.ProvidedCapabilities.Intersect(peerProvided, StringComparer.Ordinal).Except(profile.RequiredCapabilities, StringComparer.Ordinal).Order(StringComparer.Ordinal));
        return accept;
    }

    private static IReadOnlyList<string> NormalizeCapabilities(IEnumerable<string> values)
    {
        var result = values.Order(StringComparer.Ordinal).ToArray();
        foreach (var value in result) CoreGatewayWireValidatorV1.ValidateStableToken(value, "capability");
        if (result.Distinct(StringComparer.Ordinal).Count() != result.Length) throw new CoreGatewayProtocolException("protocol.capability-missing", "Duplicate capability.");
        return result;
    }
}

public interface IProtocolMessageIdSourceV1 { OpaqueId128 Next(); }
public sealed class RandomProtocolMessageIdSourceV1 : IProtocolMessageIdSourceV1
{
    public OpaqueId128 Next() { Span<byte> bytes = stackalloc byte[16]; do RandomNumberGenerator.Fill(bytes); while (bytes.IndexOfAnyExcept((byte)0) < 0); return OpaqueId128.FromBytes(bytes); }
}

public sealed class CoreGatewayEnvelopeFactoryV1
{
    private readonly OpaqueId128 _coreInstanceId; private readonly IProtocolMessageIdSourceV1 _ids;
    public CoreGatewayEnvelopeFactoryV1(OpaqueId128 coreInstanceId, IProtocolMessageIdSourceV1? ids = null) { if (coreInstanceId.IsZero) throw new ArgumentException("Core instance id cannot be ZERO."); _coreInstanceId = coreInstanceId; _ids = ids ?? new RandomProtocolMessageIdSourceV1(); }
    public WireEnvelopeV1 BootstrapResponse(WireEnvelopeV1 request, string type, IMessage payload) => Create(request, type, payload, 0, true, null, null);
    public WireEnvelopeV1 NormalResponse(WireEnvelopeV1 request, string type, IMessage payload, uint generation, WorldContextWireV1? world = null, OperationContextWireV1? operation = null) => Create(request, type, payload, generation, false, world, operation);
    public WireEnvelopeV1 Notification(string type, IMessage payload, uint generation, OpaqueId128 correlationId, WorldContextWireV1? world = null, OperationContextWireV1? operation = null)
    {
        var entry = CoreGatewayProtocolRegistryV1.Get(type); if (entry.Direction != CoreGatewayMessageDirectionV1.CoreToGateway || entry.Bootstrap) throw new InvalidOperationException("Notification registry mismatch.");
        var envelope = Base(type, entry.PayloadSchemaId.Value, payload, generation, false, correlationId, null); envelope.WorldContext = world; envelope.OperationContext = operation; CoreGatewayWireValidatorV1.Validate(envelope, CoreGatewayMessageDirectionV1.CoreToGateway, generation); return envelope;
    }
    private WireEnvelopeV1 Create(WireEnvelopeV1 request, string type, IMessage payload, uint generation, bool bootstrap, WorldContextWireV1? world, OperationContextWireV1? operation)
    {
        var entry = CoreGatewayProtocolRegistryV1.Get(type); if (entry.Direction != CoreGatewayMessageDirectionV1.CoreToGateway || entry.Bootstrap != bootstrap) throw new InvalidOperationException("Response registry mismatch.");
        var envelope = Base(type, entry.PayloadSchemaId.Value, payload, generation, bootstrap, OpaqueId128.FromBytes(request.CorrelationId.Span), OpaqueId128.FromBytes(request.MessageId.Span)); envelope.WorldContext = world; envelope.OperationContext = operation; CoreGatewayWireValidatorV1.Validate(envelope, CoreGatewayMessageDirectionV1.CoreToGateway, bootstrap ? null : generation); return envelope;
    }
    private WireEnvelopeV1 Base(string type, string schema, IMessage payload, uint generation, bool bootstrap, OpaqueId128 correlation, OpaqueId128? cause)
    {
        var envelope = new WireEnvelopeV1 { EnvelopeVersion = 1, ProtocolId = CoreGatewayProtocolRegistryV1.ProtocolId, ProtocolVersion = new ProtocolVersionV1 { Major = bootstrap ? 0u : 1u, Minor = 0 }, NegotiationGeneration = generation, MessageType = type, MessageId = ByteString.CopyFrom(_ids.Next().ToBytes()), CorrelationId = ByteString.CopyFrom(correlation.ToBytes()), SenderInstanceId = ByteString.CopyFrom(_coreInstanceId.ToBytes()), PayloadSchemaId = schema, PayloadSchemaVersion = new SchemaVersionWireV1 { Major = 1, Minor = 0 }, PayloadCompression = CompressionKindV1.None, Payload = payload.ToByteString() };
        if (cause is { } value) envelope.CausationId = ByteString.CopyFrom(value.ToBytes()); return envelope;
    }
}
