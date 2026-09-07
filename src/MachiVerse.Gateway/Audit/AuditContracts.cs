using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using MachiVerse.Gateway.Observability;

namespace MachiVerse.Gateway.Audit;

public static class AuditEventKindRegistryV1
{
    private static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "audit.admin.audit-export", "audit.admin.audit-query", "audit.admin.command-completed",
        "audit.admin.command-requested", "audit.admin.config-change-applied",
        "audit.admin.config-change-rejected", "audit.admin.config-change-requested",
        "audit.admin.config-read", "audit.admin.high-impact-confirmed", "audit.admin.high-impact-expired",
        "audit.admin.log-query", "audit.admin.simulation-operation-requested", "audit.auth.login-failure",
        "audit.auth.login-success", "audit.auth.session-created", "audit.auth.session-revoked",
        "audit.authorization.denied", "audit.gateway.master-role-changed",
        "audit.persistence.migration-completed", "audit.persistence.migration-failed",
        "audit.persistence.migration-started", "audit.persistence.recovery-completed",
        "audit.persistence.recovery-failed", "audit.persistence.recovery-started",
        "audit.snapshot.exported", "audit.snapshot.imported",
    };

    public static IReadOnlySet<string> All => Kinds;

    public static void RequireKnown(string kind)
    {
        GatewayObservabilityRegistry.RequireStableToken(kind, nameof(kind));
        if (!Kinds.Contains(kind)) throw new InvalidDataException("audit.kind-unregistered");
    }
}

public sealed record AuditRecordDraftV1(
    string AuditKind, long ObservedAtUnixNs, string Component, byte[] ComponentInstanceId,
    string? ActorRef, byte[]? SessionRefDigest, byte[]? OperationId, byte[]? CorrelationId,
    string? TargetRef, byte[]? WorldId, ulong? SimulationStep, ulong? ConfigGeneration,
    byte[]? RequestDigest, string ResultStatus, string ResultCode, byte[]? ApprovalEvidenceDigest,
    IReadOnlyDictionary<string, string> SummaryFields);

public sealed record AuditRecordV1(
    ulong AuditSequence, byte[] PreviousDigest, string AuditKind, long ObservedAtUnixNs,
    string Component, byte[] ComponentInstanceId, string? ActorRef, byte[]? SessionRefDigest,
    byte[]? OperationId, byte[]? CorrelationId, string? TargetRef, byte[]? WorldId,
    ulong? SimulationStep, ulong? ConfigGeneration, byte[]? RequestDigest, string ResultStatus,
    string ResultCode, byte[]? ApprovalEvidenceDigest, IReadOnlyDictionary<string, string> SummaryFields,
    byte[] RecordDigest);

public sealed record AuditRetentionAnchorV1(
    ulong FirstRetainedSequence, byte[] PriorFinalDigest, ulong DeletedThroughSequence, ulong PolicyGeneration);

public static class AuditRecordCodecV1
{
    private const string AuditDomainLabel = "mv.audit-record.v1";

    public static AuditRecordV1 Create(ulong sequence, ReadOnlySpan<byte> previousDigest, AuditRecordDraftV1 draft)
    {
        if (sequence == 0) throw new InvalidDataException("audit.sequence-zero");
        RequireLength(previousDigest, 32, "previous_digest");
        ValidateDraft(draft);
        if (sequence == 1 && !IsZero(previousDigest))
            throw new InvalidDataException("audit.sequence-one-previous-digest-nonzero");

        var summary = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in draft.SummaryFields) summary.Add(pair.Key, pair.Value);

        var provisional = new AuditRecordV1(
            sequence, previousDigest.ToArray(), draft.AuditKind, draft.ObservedAtUnixNs, draft.Component,
            draft.ComponentInstanceId.ToArray(), draft.ActorRef, Clone(draft.SessionRefDigest),
            Clone(draft.OperationId), Clone(draft.CorrelationId), draft.TargetRef, Clone(draft.WorldId),
            draft.SimulationStep, draft.ConfigGeneration, Clone(draft.RequestDigest), draft.ResultStatus,
            draft.ResultCode, Clone(draft.ApprovalEvidenceDigest), summary, Array.Empty<byte>());
        return provisional with { RecordDigest = DomainHash(AuditDomainLabel, NormalizeWithoutDigest(provisional)) };
    }

    // MV-DCBOR-v1 logical map. Unsigned schema field keys follow the P4-07 AuditRecord field order.
    public static byte[] NormalizeWithoutDigest(AuditRecordV1 record)
    {
        ValidateRecordShapeWithoutDigest(record);
        using var stream = new MemoryStream();
        WriteMajor(stream, 5, 19); // map(19), fields 0..18 excluding record_digest
        WritePair(stream, 0, s => WriteUnsigned(s, record.AuditSequence));
        WritePair(stream, 1, s => WriteBytes(s, record.PreviousDigest));
        WritePair(stream, 2, s => WriteText(s, record.AuditKind));
        WritePair(stream, 3, s => WriteSigned(s, record.ObservedAtUnixNs));
        WritePair(stream, 4, s => WriteText(s, record.Component));
        WritePair(stream, 5, s => WriteBytes(s, record.ComponentInstanceId));
        WritePair(stream, 6, s => WriteOptionalText(s, record.ActorRef));
        WritePair(stream, 7, s => WriteOptionalBytes(s, record.SessionRefDigest));
        WritePair(stream, 8, s => WriteOptionalBytes(s, record.OperationId));
        WritePair(stream, 9, s => WriteOptionalBytes(s, record.CorrelationId));
        WritePair(stream, 10, s => WriteOptionalText(s, record.TargetRef));
        WritePair(stream, 11, s => WriteOptionalBytes(s, record.WorldId));
        WritePair(stream, 12, s => WriteOptionalUnsigned(s, record.SimulationStep));
        WritePair(stream, 13, s => WriteOptionalUnsigned(s, record.ConfigGeneration));
        WritePair(stream, 14, s => WriteOptionalBytes(s, record.RequestDigest));
        WritePair(stream, 15, s => WriteText(s, record.ResultStatus));
        WritePair(stream, 16, s => WriteText(s, record.ResultCode));
        WritePair(stream, 17, s => WriteOptionalBytes(s, record.ApprovalEvidenceDigest));
        WritePair(stream, 18, s => WriteSummaryMap(s, record.SummaryFields));
        return stream.ToArray();
    }

    public static byte[] DomainHash(string label, ReadOnlySpan<byte> mvDcbor)
    {
        if (string.IsNullOrEmpty(label) || label.Any(static c => c > 0x7f))
            throw new InvalidDataException("audit.domain-hash-label-invalid");
        if (mvDcbor.IsEmpty) throw new InvalidDataException("audit.domain-hash-value-empty");
        var labelBytes = Encoding.ASCII.GetBytes(label);
        var preimage = new byte[labelBytes.Length + 1 + mvDcbor.Length];
        labelBytes.CopyTo(preimage, 0);
        mvDcbor.CopyTo(preimage.AsSpan(labelBytes.Length + 1));
        return SHA256.HashData(preimage);
    }

    public static void ValidateDigest(AuditRecordV1 record)
    {
        ValidateRecordShape(record);
        var expected = DomainHash(AuditDomainLabel, NormalizeWithoutDigest(record));
        if (!CryptographicOperations.FixedTimeEquals(expected, record.RecordDigest))
            throw new InvalidDataException("audit.record-digest-mismatch");
    }

    private static void ValidateDraft(AuditRecordDraftV1 draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        AuditEventKindRegistryV1.RequireKnown(draft.AuditKind);
        GatewayObservabilityRegistry.RequireStableToken(draft.Component, "component");
        RequireLength(draft.ComponentInstanceId, 16, "component_instance_id");
        RequireOptionalLength(draft.SessionRefDigest, 32, "session_ref_digest");
        RequireOptionalLength(draft.OperationId, 16, "operation_id");
        RequireOptionalLength(draft.CorrelationId, 16, "correlation_id");
        RequireOptionalLength(draft.WorldId, 16, "world_id");
        RequireOptionalLength(draft.RequestDigest, 32, "request_digest");
        RequireOptionalLength(draft.ApprovalEvidenceDigest, 32, "approval_evidence_digest");
        if (draft.ActorRef is not null) RequireSafeText(draft.ActorRef, 512, "actor_ref");
        if (draft.TargetRef is not null) GatewayObservabilityRegistry.RequireStableToken(draft.TargetRef, "target_ref");
        GatewayObservabilityRegistry.RequireStableToken(draft.ResultStatus, "result_status");
        GatewayObservabilityRegistry.RequireStableToken(draft.ResultCode, "result_code");
        foreach (var (key, value) in draft.SummaryFields)
        {
            GatewayObservabilityRegistry.RequireStableToken(key, "summary key");
            RequireSafeText(value, 1024, $"summary field {key}");
        }
    }

    private static void ValidateRecordShape(AuditRecordV1 record)
    {
        ValidateRecordShapeWithoutDigest(record);
        RequireLength(record.RecordDigest, 32, "record_digest");
    }

    private static void ValidateRecordShapeWithoutDigest(AuditRecordV1 record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.AuditSequence == 0) throw new InvalidDataException("audit.sequence-zero");
        RequireLength(record.PreviousDigest, 32, "previous_digest");
        RequireLength(record.ComponentInstanceId, 16, "component_instance_id");
        RequireOptionalLength(record.SessionRefDigest, 32, "session_ref_digest");
        RequireOptionalLength(record.OperationId, 16, "operation_id");
        RequireOptionalLength(record.CorrelationId, 16, "correlation_id");
        RequireOptionalLength(record.WorldId, 16, "world_id");
        RequireOptionalLength(record.RequestDigest, 32, "request_digest");
        RequireOptionalLength(record.ApprovalEvidenceDigest, 32, "approval_evidence_digest");
        AuditEventKindRegistryV1.RequireKnown(record.AuditKind);
        GatewayObservabilityRegistry.RequireStableToken(record.Component, "component");
        if (record.ActorRef is not null) RequireSafeText(record.ActorRef, 512, "actor_ref");
        if (record.TargetRef is not null) GatewayObservabilityRegistry.RequireStableToken(record.TargetRef, "target_ref");
        GatewayObservabilityRegistry.RequireStableToken(record.ResultStatus, "result_status");
        GatewayObservabilityRegistry.RequireStableToken(record.ResultCode, "result_code");
        foreach (var (key, value) in record.SummaryFields)
        {
            GatewayObservabilityRegistry.RequireStableToken(key, "summary key");
            RequireSafeText(value, 1024, $"summary field {key}");
        }
        if (record.AuditSequence == 1 && !IsZero(record.PreviousDigest))
            throw new InvalidDataException("audit.sequence-one-previous-digest-nonzero");
    }

    private static void WritePair(Stream s, ulong key, Action<Stream> value)
    {
        WriteUnsigned(s, key);
        value(s);
    }

    private static void WriteSummaryMap(Stream s, IReadOnlyDictionary<string, string> fields)
    {
        var encoded = fields.Select(pair => (Key: EncodeText(pair.Key), pair.Value))
            .OrderBy(x => x.Key, ByteArrayComparer.Instance).ToArray();
        WriteMajor(s, 5, (ulong)encoded.Length);
        foreach (var item in encoded)
        {
            s.Write(item.Key);
            WriteText(s, item.Value);
        }
    }

    private static byte[] EncodeText(string value)
    {
        using var s = new MemoryStream();
        WriteText(s, value);
        return s.ToArray();
    }

    private static void WriteOptionalText(Stream s, string? value) { if (value is null) WriteNull(s); else WriteText(s, value); }
    private static void WriteOptionalBytes(Stream s, byte[]? value) { if (value is null) WriteNull(s); else WriteBytes(s, value); }
    private static void WriteOptionalUnsigned(Stream s, ulong? value) { if (value.HasValue) WriteUnsigned(s, value.Value); else WriteNull(s); }
    private static void WriteNull(Stream s) => s.WriteByte(0xf6);
    private static void WriteBytes(Stream s, ReadOnlySpan<byte> value) { WriteMajor(s, 2, (ulong)value.Length); s.Write(value); }
    private static void WriteText(Stream s, string value) { var bytes = Encoding.UTF8.GetBytes(value); WriteMajor(s, 3, (ulong)bytes.Length); s.Write(bytes); }
    private static void WriteUnsigned(Stream s, ulong value) => WriteMajor(s, 0, value);
    private static void WriteSigned(Stream s, long value)
    {
        if (value >= 0) WriteMajor(s, 0, (ulong)value);
        else WriteMajor(s, 1, checked((ulong)(-1 - value)));
    }

    private static void WriteMajor(Stream s, byte major, ulong value)
    {
        if (value < 24) { s.WriteByte((byte)((major << 5) | value)); return; }
        if (value <= byte.MaxValue) { s.WriteByte((byte)((major << 5) | 24)); s.WriteByte((byte)value); return; }
        if (value <= ushort.MaxValue) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)value); s.WriteByte((byte)((major << 5) | 25)); s.Write(b); return; }
        if (value <= uint.MaxValue) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, (uint)value); s.WriteByte((byte)((major << 5) | 26)); s.Write(b); return; }
        { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, value); s.WriteByte((byte)((major << 5) | 27)); s.Write(b); }
    }

    private static void RequireSafeText(string value, int maxBytes, string field)
    {
        if (value.Any(char.IsControl) || Encoding.UTF8.GetByteCount(value) > maxBytes)
            throw new InvalidDataException($"audit.{field}-invalid");
        GatewayObservabilityRegistry.RequireNoCredentialMaterial(value);
    }
    private static void RequireOptionalLength(byte[]? value, int length, string field) { if (value is not null) RequireLength(value, length, field); }
    private static void RequireLength(ReadOnlySpan<byte> value, int length, string field) { if (value.Length != length) throw new InvalidDataException($"audit.{field}-length"); }
    private static bool IsZero(ReadOnlySpan<byte> value) { var x = 0; foreach (var b in value) x |= b; return x == 0; }
    private static byte[]? Clone(byte[]? value) => value?.ToArray();

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();
        public int Compare(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            var length = Math.Min(x.Length, y.Length);
            for (var i = 0; i < length; i++) { var c = x[i].CompareTo(y[i]); if (c != 0) return c; }
            return x.Length.CompareTo(y.Length);
        }
    }
}
