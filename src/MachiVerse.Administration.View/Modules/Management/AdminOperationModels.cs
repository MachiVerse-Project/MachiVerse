using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Administration.View.Modules.Management;

public enum AdminSessionAccessState
{
    Unavailable,
    Active,
    ReauthRequired,
    Revoked,
    Expired,
}

public sealed record AdminSessionProjection(
    string? SessionId,
    ulong SessionGeneration,
    AdminSessionAccessState State,
    string EffectiveRoleSet,
    IReadOnlyList<string> EffectivePermissions,
    string? ReasonCode = null)
{
    public bool HasPermission(string permission)
        => State == AdminSessionAccessState.Active
            && EffectivePermissions.Contains(permission, StringComparer.Ordinal);
}

public enum HighImpactConfirmationState
{
    NotRequired,
    Required,
    Confirming,
    Confirmed,
    ExpiredOrInvalid,
}

public sealed record AdminRequestFingerprint(
    string OperationId,
    string ImmutablePayloadDigest,
    string RequestKind)
{
    public override string ToString() => $"{RequestKind}:{OperationId}:{ImmutablePayloadDigest}";
}

public sealed record LocalConfirmationEvidence(
    string EvidenceId,
    AdminRequestFingerprint Fingerprint,
    ulong SessionGeneration,
    DateTimeOffset ConfirmedAt,
    DateTimeOffset ExpiresAt);

public sealed record HighImpactConfirmationSnapshot(
    HighImpactConfirmationState State,
    AdminRequestFingerprint? Fingerprint,
    ulong SessionGeneration,
    DateTimeOffset? ExpiresAt,
    bool Consumed,
    string? ReasonCode);

public sealed record AdminOperationDescriptor(
    string OperationKind,
    string PayloadSchemaId,
    uint PayloadSchemaMajor,
    uint PayloadSchemaMinor,
    bool HighImpact,
    string RequiredPermission = "admin.operation.submit");

public sealed record AdminOperationDraft(
    string OperationKind,
    ulong AdmissionBasisStep,
    ulong SchedulingPolicyGeneration,
    ulong? RequestedNotBeforeStep,
    ulong? RequestedDeadlineStep,
    ulong? CandidateStep,
    ByteString Payload);

public enum AdminOperationLifecycleState
{
    Prepared,
    AwaitingConfirmation,
    ReadyForSubmit,
    Submitted,
    Accepted,
    Scheduled,
    Terminal,
    DeliveryUnknown,
    Unauthorized,
    Rejected,
    Failed,
}

public sealed record AdminOperationResultProjection(
    int StatusValue,
    string Status,
    string Code,
    int RetryAdviceValue,
    string RetryAdvice,
    string Diagnostic)
{
    public static AdminOperationResultProjection? FromWire(ResultV1? result)
        => result is null
            ? null
            : new(
                (int)result.Status,
                result.Status.ToString(),
                result.Code,
                (int)result.RetryAdvice,
                result.RetryAdvice.ToString(),
                result.Diagnostic);
}

public sealed record TrackedAdminOperation(
    string OperationId,
    string ImmutablePayloadDigest,
    string OperationKind,
    bool HighImpact,
    ulong SessionGenerationAtPrepare,
    ulong AdmissionBasisStep,
    ulong? CandidateStep,
    AdminOperationLifecycleState State,
    ulong? EffectiveStep,
    AdminOperationResultProjection? TerminalResult,
    string? CorrelationId,
    string? LastReasonCode);

public enum AuditCorrelationState
{
    Missing,
    Matched,
    DigestMismatch,
}

public sealed record AdminAuditCorrelationProjection(
    string OperationId,
    AuditCorrelationState State,
    int MatchingRecordCount,
    string? LatestAuditEventKind,
    string? LatestResultCode,
    ulong? LatestSimulationStep,
    string? ReasonCode);
