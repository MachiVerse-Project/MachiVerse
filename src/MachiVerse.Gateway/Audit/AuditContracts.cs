using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using MachiVerse.Gateway.Observability;

namespace MachiVerse.Gateway.Audit;

public static class AuditEventKindRegistryV1
{
    private static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "audit.admin.audit-export",
        "audit.admin.audit-query",
        "audit.admin.command-completed",
        "audit.admin.command-requested",
        "audit.admin.config-change-applied",
        "audit.admin.config-change-rejected",
        "audit.admin.config-change-requested",
        "audit.admin.config-read",
        "audit.admin.high-impact-confirmed",
        "audit.admin.high-impact-expired",
        "audit.admin.log-query",
        "audit.admin.simulation-operation-requested",
        "audit.auth.login-failure",
        "audit.auth.login-success",
        "audit.auth.session-created",
        "audit.auth.session-revoked",
        "audit.authorization.denied",
        "audit.gateway.master-role-changed",
        "audit.persistence.migration-completed",
        "audit.persistence.migration-failed",
        "audit.persistence.migration-started",
        "audit.persistence.recovery-completed",
        "audit.persistence.recovery-failed",
        "audit.persistence.recovery-started",
        "audit.snapshot.exported",
        "audit.snapshot.imported",
    };

    public static IReadOnlySet<string> All => Kinds;

    public static void RequireKnown(string kind)
    {
        GatewayObservabilityRegistry.RequireStableToken(kind, nameof(kind));
        if (!Kinds.Contains(kind))
            throw new InvalidDataException("audit.kind-unregistered");
    }
}

public sealed record AuditRecordDraftV1(
    string AuditKind,
    long ObservedAtUnixNs,
    string Component,
    byte[] ComponentInstanceId,
    string? ActorRef,
    byte[]? SessionRefDigest,
    byte[]? OperationId,
    byte[]? CorrelationId,
    string? TargetRef,
    byte[]? WorldId,
    ulong? SimulationStep,
    ulong? ConfigGeneration,
    byte[]? RequestDigest,
    string ResultStatus,
    string ResultCode,
    byte[]? ApprovalEvidenceDigest,
    IReadOnlyDictionary<string, string> SummaryFields);

public sealed record AuditRecordV1(
    ulong AuditSequence,
    byte[] PreviousDigest,
    string AuditKind,
    long ObservedAtUnixNs,
    string Component,
    byte[] ComponentInstanceId,
    string? ActorRef,
    byte[]? SessionRefDigest,
    byte[]? OperationId,
    byte[]? CorrelationId,
    string? TargetRef,
    byte[]? WorldId,
    ulong? SimulationStep,
    ulong? ConfigGeneration,
    byte[]? RequestDigest,
    string ResultStatus,
    string ResultCode,
    byte[]? ApprovalEvidenceDigest,
    IReadOnlyDictionary<string, string> SummaryFields,
    byte[] RecordDigest);

public sealed record AuditRetentionAnchorV1(
    ulong FirstRetainedSequence,
    byte[] PriorFinalDigest,
    ulong DeletedThroughSequence,
    ulong PolicyGeneration);

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

        var summary = new SortedDictionary<string, string>(draft.SummaryFields, StringComparer.Ordinal);
        var provisional = new AuditRecordV1(
            sequence,
            previousDigest.ToArray(),
            draft.AuditKind,
            draft.ObservedAtUnixNs,
            draft.Component,
            draft.ComponentInstanceId.ToArray(),
            draft.ActorRef,
            Clone(draft.SessionRefDigest),
            Clone(draft.OperationId),
            Clone(draft.CorrelationId),
            draft.TargetRef,
            Clone(draft.WorldId),
            draft.SimulationStep,
            draft.ConfigGeneration,
            Clone(draft.RequestDigest),
            draft.ResultStatus,
            draft.ResultCode,
            Clone(draft.ApprovalEvidenceDigest),
            summary,
            Array.Empty<byte>());

        var normalized = NormalizeWithoutDigest(provisional);
        return provisional with { RecordDigest = DomainHash(AuditDomainLabel, normalized) };
    }

    public static byte[] NormalizeWithoutDigest(AuditRecordV1 record)
    {
        using var stream = new MemoryStream();
        stream.WriteByte(1);
        WriteU64(stream, record.AuditSequence);
        WriteFixed(stream, record.PreviousDigest, 32, "previous_digest");
        WriteString(stream, record.AuditKind);
        WriteI64(stream, record.ObservedAtUnixNs);
        WriteString(stream, record.Component);
        WriteFixed(stream, record.ComponentInstanceId, 16, "component_instance_id");
        WriteOptionalString(stream, record.ActorRef);
        WriteOptionalFixed(stream, record.SessionRefDigest, 32, "session_ref_digest");
        WriteOptionalFixed(stream, record.OperationId, 16, "operation_id");
        WriteOptionalFixed(stream, record.CorrelationId, 16, "correlation_id");
        WriteOptionalString(stream, record.TargetRef);
        WriteOptionalFixed(stream, record.WorldId, 16, "world_id");
        WriteOptionalU64(stream, record.SimulationStep);
        WriteOptionalU64(stream, record.ConfigGeneration);
        WriteOptionalFixed(stream, record.RequestDigest, 32, "request_digest");
        WriteString(stream, record.ResultStatus);
        WriteString(stream, record.ResultCode);
        WriteOptionalFixed(stream, record.ApprovalEvidenceDigest, 32, "approval_evidence_digest");

        var ordered = record.SummaryFields.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        WriteU32(stream, checked((uint)ordered.Length));
        foreach (var (key, value) in ordered)
        {
            WriteString(stream, key);
            WriteString(stream, value);
        }
        return stream.ToArray();
    }

    public static byte[] DomainHash(string label, ReadOnlySpan<byte> normalized)
    {
        if (string.IsNullOrEmpty(label) || label.Any(static c => c > 0x7f))
            throw new InvalidDataException("audit.domain-hash-label-invalid");
        var labelBytes = Encoding.ASCII.GetBytes(label);
        var preimage = new byte[labelBytes.Length + 1 + normalized.Length];
        labelBytes.CopyTo(preimage, 0);
        normalized.CopyTo(preimage.AsSpan(labelBytes.Length + 1));
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

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in draft.SummaryFields)
        {
            GatewayObservabilityRegistry.RequireStableToken(key, "summary key");
            if (!keys.Add(key)) throw new InvalidDataException("audit.summary-key-duplicate");
            RequireSafeText(value, 1024, $"summary field {key}");
        }
    }

    private static void ValidateRecordShape(AuditRecordV1 record)
    {
        if (record.AuditSequence == 0) throw new InvalidDataException("audit.sequence-zero");
        RequireLength(record.PreviousDigest, 32, "previous_digest");
        RequireLength(record.RecordDigest, 32, "record_digest");
        if (record.AuditSequence == 1 && !IsZero(record.PreviousDigest))
            throw new InvalidDataException("audit.sequence-one-previous-digest-nonzero");
    }

    private static void RequireSafeText(string value, int maxBytes, string field)
    {
        if (value.Any(char.IsControl) || Encoding.UTF8.GetByteCount(value) > maxBytes)
            throw new InvalidDataException($"audit.{field}-invalid");
        GatewayObservabilityRegistry.RequireNoCredentialMaterial(value);
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteU32(stream, checked((uint)bytes.Length));
        stream.Write(bytes);
    }

    private static void WriteOptionalString(Stream stream, string? value)
    {
        stream.WriteByte(value is null ? (byte)0 : (byte)1);
        if (value is not null) WriteString(stream, value);
    }

    private static void WriteOptionalFixed(Stream stream, byte[]? value, int length, string field)
    {
        stream.WriteByte(value is null ? (byte)0 : (byte)1);
        if (value is not null) WriteFixed(stream, value, length, field);
    }

    private static void WriteOptionalU64(Stream stream, ulong? value)
    {
        stream.WriteByte(value.HasValue ? (byte)1 : (byte)0);
        if (value.HasValue) WriteU64(stream, value.Value);
    }

    private static void WriteFixed(Stream stream, ReadOnlySpan<byte> value, int length, string field)
    {
        RequireLength(value, length, field);
        stream.Write(value);
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteU64(Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteI64(Stream stream, long value)
        => WriteU64(stream, unchecked((ulong)value));

    private static void RequireOptionalLength(byte[]? value, int length, string field)
    {
        if (value is not null) RequireLength(value, length, field);
    }

    private static void RequireLength(ReadOnlySpan<byte> value, int length, string field)
    {
        if (value.Length != length) throw new InvalidDataException($"audit.{field}-length");
    }

    private static bool IsZero(ReadOnlySpan<byte> value)
    {
        var aggregate = 0;
        foreach (var item in value) aggregate |= item;
        return aggregate == 0;
    }

    private static byte[]? Clone(byte[]? value) => value?.ToArray();
}
