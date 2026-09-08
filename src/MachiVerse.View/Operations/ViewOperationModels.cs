using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.View.Operations;

public enum ViewMutationAccessState
{
    Blocked,
    Ready,
    Resyncing,
    Reconnecting,
    SessionRevoked,
    Unauthorized,
    Incompatible
}

public enum ViewOperationLifecycleState
{
    LocalDraft,
    Sent,
    AckedOrAccepted,
    PendingAuthoritative,
    DeliveryUnknown,
    Terminal,
    Rejected,
    Failed
}

public sealed record ViewOperationDraft(
    string OperationKind,
    ulong AdmissionBasisStep,
    ulong SchedulingPolicyGeneration,
    ulong? RequestedNotBeforeStep,
    ulong? RequestedDeadlineStep,
    ulong? CandidateStep,
    string PayloadSchemaId,
    uint PayloadSchemaMajor,
    uint PayloadSchemaMinor,
    ByteString Payload,
    string SemanticTarget,
    ByteString PredictedPayload);

public sealed record TrackedViewOperation(
    string OperationId,
    string ImmutablePayloadDigest,
    string OperationKind,
    string SemanticTarget,
    ViewOperationLifecycleState State,
    ulong AdmissionBasisStep,
    ulong? CandidateStep,
    ulong? EffectiveStep,
    ResultV1? TerminalResult,
    string? LastReasonCode,
    string? CorrelationId,
    StandardOperationV1 Request);
