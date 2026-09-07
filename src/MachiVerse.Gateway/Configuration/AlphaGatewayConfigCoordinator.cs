using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using MachiVerse.Protocol.Canonical;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Gateway.Configuration;

public sealed record AlphaGatewayConfigSnapshot(
    ulong Generation,
    IReadOnlyDictionary<string, ulong> Values,
    byte[] Digest);

public sealed record AlphaGatewayConfigApplyResult(
    ResultV1 Result,
    ulong ResultingGeneration,
    byte[] ResultingConfigDigest,
    bool Replayed);

/// <summary>
/// INT-01 single-writer Config owner for the local Alpha Gateway.
/// It owns the effective non-secret operational values exposed by the Alpha Admin bridge,
/// their monotonically increasing ConfigGeneration, and same-OperationId replay records.
/// </summary>
public sealed class AlphaGatewayConfigCoordinator
{
    private const string ConfigDigestDomain = "mv.config.v1";
    private readonly object _gate = new();
    private readonly string _statePath;
    private PersistentState _state;

    public AlphaGatewayConfigCoordinator(GatewayConfig startupConfig, string componentDataDirectory)
    {
        ArgumentNullException.ThrowIfNull(startupConfig);
        if (string.IsNullOrWhiteSpace(componentDataDirectory))
            throw new ArgumentException("Gateway component data directory is required.", nameof(componentDataDirectory));

        var directory = Path.Combine(Path.GetFullPath(componentDataDirectory), "alpha-config");
        Directory.CreateDirectory(directory);
        _statePath = Path.Combine(directory, "gateway-config-state.json");

        var startupValues = BuildStartupValues(startupConfig);
        if (File.Exists(_statePath))
        {
            _state = JsonSerializer.Deserialize<PersistentState>(File.ReadAllText(_statePath))
                ?? throw new InvalidDataException("config.persist-state-invalid");
            ValidatePersistentState(_state);
            ValidateCandidate(_state.Values);
        }
        else
        {
            _state = new PersistentState(1, startupValues, new Dictionary<string, PersistentOperation>(StringComparer.Ordinal));
            PersistLocked();
        }
    }

    public AlphaGatewayConfigSnapshot Current
    {
        get
        {
            lock (_gate) return SnapshotLocked();
        }
    }

    public AlphaGatewayConfigApplyResult Apply(ConfigChangeRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OperationId.Length != 16 || IsZero(request.OperationId.Span))
            throw new InvalidDataException("protocol.invalid-id:operation_id");
        if (request.ImmutablePayloadDigest.Length != 32)
            throw new InvalidDataException("protocol.invalid-hash:immutable_payload_digest");

        var expectedDigest = ConfigChangeIdentityV1.ComputeImmutablePayloadDigest(request);
        if (!CryptographicOperations.FixedTimeEquals(expectedDigest, request.ImmutablePayloadDigest.Span))
            throw new InvalidDataException("operation.payload-digest-mismatch");

        var operationKey = Convert.ToHexStringLower(request.OperationId.Span);
        var digestHex = Convert.ToHexStringLower(request.ImmutablePayloadDigest.Span);
        lock (_gate)
        {
            if (_state.Operations.TryGetValue(operationKey, out var prior))
            {
                if (!string.Equals(prior.ImmutablePayloadDigest, digestHex, StringComparison.Ordinal))
                    throw new InvalidDataException("config.operation-id-reuse");
                return Replay(prior);
            }

            if (request.ExpectedBaseGeneration != _state.Generation)
            {
                return CompleteLocked(
                    operationKey,
                    digestHex,
                    status: 6,
                    code: "config.generation-stale",
                    retryAdvice: 4,
                    generation: _state.Generation,
                    digest: ComputeConfigDigest(_state.Values));
            }

            if (request.HasRequestedEffectiveStep)
                return RejectLocked(operationKey, digestHex, "config.invalid");

            var candidate = new Dictionary<string, ulong>(_state.Values, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? invalidCode = null;
            foreach (var change in request.Changes)
            {
                if (!seen.Add(change.Key))
                {
                    invalidCode = "config.invalid";
                    break;
                }
                if (!candidate.ContainsKey(change.Key))
                {
                    invalidCode = "config.unknown-field";
                    break;
                }
                if (!AlphaGatewayConfigPolicy.IsRuntimeMutable(change.Key))
                {
                    invalidCode = "config.static-change-offline";
                    break;
                }
                if (change.Value is null || (int)change.Value.ValueCase != 3 || change.Value.UintValue > int.MaxValue)
                {
                    invalidCode = "config.invalid";
                    break;
                }
                candidate[change.Key] = change.Value.UintValue;
            }

            if (request.Changes.Count == 0) invalidCode = "config.invalid";
            if (invalidCode is null)
            {
                try { ValidateCandidate(candidate); }
                catch (InvalidDataException) { invalidCode = "config.invalid"; }
            }

            if (invalidCode is not null)
                return RejectLocked(operationKey, digestHex, invalidCode);

            if (DictionaryEqual(_state.Values, candidate))
            {
                return CompleteLocked(
                    operationKey,
                    digestHex,
                    status: 4,
                    code: "config.no-change",
                    retryAdvice: 1,
                    generation: _state.Generation,
                    digest: ComputeConfigDigest(_state.Values));
            }

            var nextGeneration = checked(_state.Generation + 1);
            var nextDigest = ComputeConfigDigest(candidate);
            _state = _state with { Generation = nextGeneration, Values = candidate };
            return CompleteLocked(
                operationKey,
                digestHex,
                status: 1,
                code: "config.change.applied",
                retryAdvice: 1,
                generation: nextGeneration,
                digest: nextDigest);
        }
    }

    private AlphaGatewayConfigApplyResult RejectLocked(string operationKey, string immutableDigest, string code)
        => CompleteLocked(
            operationKey,
            immutableDigest,
            status: 6,
            code,
            retryAdvice: 1,
            generation: _state.Generation,
            digest: ComputeConfigDigest(_state.Values));

    private AlphaGatewayConfigApplyResult CompleteLocked(
        string operationKey,
        string immutableDigest,
        int status,
        string code,
        int retryAdvice,
        ulong generation,
        byte[] digest)
    {
        var operation = new PersistentOperation(
            immutableDigest,
            status,
            code,
            retryAdvice,
            generation,
            Convert.ToHexStringLower(digest));
        _state.Operations[operationKey] = operation;
        PersistLocked();
        return new AlphaGatewayConfigApplyResult(
            ToWireResult(operation),
            generation,
            digest.ToArray(),
            false);
    }

    private static AlphaGatewayConfigApplyResult Replay(PersistentOperation operation)
        => new(
            ToWireResult(operation),
            operation.ResultingGeneration,
            Convert.FromHexString(operation.ResultingDigest),
            true);

    private static ResultV1 ToWireResult(PersistentOperation operation)
        => new()
        {
            Status = (ResultStatusV1)operation.Status,
            Code = operation.Code,
            RetryAdvice = (RetryAdviceV1)operation.RetryAdvice,
        };

    private AlphaGatewayConfigSnapshot SnapshotLocked()
        => new(
            _state.Generation,
            new Dictionary<string, ulong>(_state.Values, StringComparer.Ordinal),
            ComputeConfigDigest(_state.Values));

    private void PersistLocked()
    {
        var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
        var tempPath = _statePath + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, _statePath, overwrite: true);
    }

    private static Dictionary<string, ulong> BuildStartupValues(GatewayConfig config)
        => new(StringComparer.Ordinal)
        {
            ["network.connect-timeout-ms"] = checked((ulong)config.ConnectTimeoutMs),
            ["network.reconnect-initial-ms"] = checked((ulong)config.ReconnectInitialMs),
            ["network.reconnect-max-ms"] = checked((ulong)config.ReconnectMaxMs),
            ["peer.heartbeat-interval-ms"] = checked((ulong)config.HeartbeatIntervalMs),
            ["peer.heartbeat-timeout-ms"] = checked((ulong)config.HeartbeatTimeoutMs),
            ["auth.session-idle-lifetime-seconds"] = checked((ulong)config.SessionIdleLifetimeSeconds),
            ["auth.session-absolute-lifetime-seconds"] = checked((ulong)config.SessionAbsoluteLifetimeSeconds),
            ["queue.publication-capacity"] = checked((ulong)config.OutboundQueues.PublicationCapacity),
            ["queue.result-capacity"] = checked((ulong)config.OutboundQueues.ResultCapacity),
            ["publication.max-client-backlog"] = checked((ulong)config.OutboundQueues.MaxClientBacklog),
            ["publication.buffer-ms"] = checked((ulong)config.OutboundQueues.PublicationBufferMs),
            ["audit.retention-days"] = checked((ulong)config.Audit.RetentionDays),
            ["audit.query-max-page-size"] = checked((ulong)config.Audit.QueryMaxPageSize),
        };

    private static void ValidatePersistentState(PersistentState state)
    {
        if (state.Generation == 0) throw new InvalidDataException("config.persist-generation-zero");
        if (state.Values is null || state.Operations is null) throw new InvalidDataException("config.persist-state-invalid");
        foreach (var operation in state.Operations.Values)
        {
            if (operation.ImmutablePayloadDigest.Length != 64 || operation.ResultingDigest.Length != 64 || operation.ResultingGeneration == 0)
                throw new InvalidDataException("config.persist-operation-invalid");
            if (operation.Status is < 1 or > 7 || operation.RetryAdvice is < 1 or > 5)
                throw new InvalidDataException("config.persist-operation-result-invalid");
        }
    }

    private static void ValidateCandidate(IReadOnlyDictionary<string, ulong> values)
    {
        RequirePositiveInt(values, "network.connect-timeout-ms");
        var reconnectInitial = RequirePositiveInt(values, "network.reconnect-initial-ms");
        var reconnectMax = RequirePositiveInt(values, "network.reconnect-max-ms");
        var heartbeatInterval = RequirePositiveInt(values, "peer.heartbeat-interval-ms");
        var heartbeatTimeout = RequirePositiveInt(values, "peer.heartbeat-timeout-ms");
        var idle = RequireRange(values, "auth.session-idle-lifetime-seconds", 300, 86400);
        var absolute = RequireRange(values, "auth.session-absolute-lifetime-seconds", 900, 604800);
        RequirePositiveInt(values, "queue.publication-capacity");
        RequirePositiveInt(values, "queue.result-capacity");
        RequirePositiveInt(values, "publication.max-client-backlog");
        RequirePositiveInt(values, "publication.buffer-ms");
        RequireRange(values, "audit.retention-days", 30, 3650);
        RequireRange(values, "audit.query-max-page-size", 100, 10000);

        if (reconnectMax < reconnectInitial) throw new InvalidDataException("config.constraint.reconnect-range");
        if ((long)heartbeatTimeout < (long)heartbeatInterval * 3) throw new InvalidDataException("config.constraint.heartbeat-timeout");
        if (absolute < idle) throw new InvalidDataException("config.constraint.session-lifetime");
    }

    private static int RequirePositiveInt(IReadOnlyDictionary<string, ulong> values, string key)
    {
        var value = Require(values, key);
        if (value is 0 or > int.MaxValue) throw new InvalidDataException($"config.range:{key}");
        return checked((int)value);
    }

    private static int RequireRange(IReadOnlyDictionary<string, ulong> values, string key, int minimum, int maximum)
    {
        var value = Require(values, key);
        if (value < (ulong)minimum || value > (ulong)maximum) throw new InvalidDataException($"config.range:{key}");
        return checked((int)value);
    }

    private static ulong Require(IReadOnlyDictionary<string, ulong> values, string key)
        => values.TryGetValue(key, out var value) ? value : throw new InvalidDataException($"config.missing:{key}");

    private static bool DictionaryEqual(IReadOnlyDictionary<string, ulong> left, IReadOnlyDictionary<string, ulong> right)
        => left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static byte[] ComputeConfigDigest(IReadOnlyDictionary<string, ulong> values)
    {
        var writer = new ConfigDigestWriter();
        writer.WriteMapStart(3);
        writer.WriteUnsigned(0); writer.WriteAsciiText("1.0");
        writer.WriteUnsigned(1); writer.WriteAsciiText("gateway");
        writer.WriteUnsigned(2);
        var fields = values.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToArray();
        writer.WriteArrayStart((ulong)fields.Length);
        foreach (var pair in fields)
        {
            writer.WriteMapStart(2);
            writer.WriteUnsigned(0); writer.WriteAsciiText(pair.Key);
            writer.WriteUnsigned(1); writer.WriteUnsigned(pair.Value);
        }

        var labelBytes = Encoding.ASCII.GetBytes(ConfigDigestDomain);
        var canonical = writer.ToArray();
        var preimage = new byte[labelBytes.Length + 1 + canonical.Length];
        labelBytes.CopyTo(preimage, 0);
        canonical.CopyTo(preimage, labelBytes.Length + 1);
        return SHA256.HashData(preimage);
    }

    private static bool IsZero(ReadOnlySpan<byte> value)
    {
        var aggregate = 0;
        foreach (var item in value) aggregate |= item;
        return aggregate == 0;
    }

    private sealed record PersistentState(
        ulong Generation,
        Dictionary<string, ulong> Values,
        Dictionary<string, PersistentOperation> Operations);

    private sealed record PersistentOperation(
        string ImmutablePayloadDigest,
        int Status,
        string Code,
        int RetryAdvice,
        ulong ResultingGeneration,
        string ResultingDigest);

    private sealed class ConfigDigestWriter
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();
        public void WriteUnsigned(ulong value) => WriteInitial(0, value);
        public void WriteAsciiText(string value)
        {
            if (value.Any(static c => c > 0x7f)) throw new InvalidDataException("config.canonical-text-non-ascii");
            var bytes = Encoding.ASCII.GetBytes(value);
            WriteInitial(3, (ulong)bytes.Length);
            WriteRaw(bytes);
        }
        public void WriteArrayStart(ulong count) => WriteInitial(4, count);
        public void WriteMapStart(ulong count) => WriteInitial(5, count);
        public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

        private void WriteInitial(byte major, ulong value)
        {
            if (value < 24) { WriteByte((byte)((major << 5) | (byte)value)); return; }
            if (value <= byte.MaxValue) { WriteByte((byte)((major << 5) | 24)); WriteByte((byte)value); return; }
            if (value <= ushort.MaxValue)
            {
                WriteByte((byte)((major << 5) | 25));
                var span = _buffer.GetSpan(2); BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)value); _buffer.Advance(2); return;
            }
            if (value <= uint.MaxValue)
            {
                WriteByte((byte)((major << 5) | 26));
                var span = _buffer.GetSpan(4); BinaryPrimitives.WriteUInt32BigEndian(span, (uint)value); _buffer.Advance(4); return;
            }
            WriteByte((byte)((major << 5) | 27));
            var destination = _buffer.GetSpan(8); BinaryPrimitives.WriteUInt64BigEndian(destination, value); _buffer.Advance(8);
        }
        private void WriteByte(byte value) { var span = _buffer.GetSpan(1); span[0] = value; _buffer.Advance(1); }
        private void WriteRaw(ReadOnlySpan<byte> value) { var span = _buffer.GetSpan(value.Length); value.CopyTo(span); _buffer.Advance(value.Length); }
    }
}
