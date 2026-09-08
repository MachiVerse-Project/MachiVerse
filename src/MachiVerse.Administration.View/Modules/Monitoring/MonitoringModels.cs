namespace MachiVerse.Administration.View.Modules.Monitoring;

public enum MonitoringChannel
{
    Health,
    Logs,
    Audit,
}

public enum MonitoringAccessState
{
    Available,
    Unavailable,
    Unauthorized,
    Redacted,
}

public sealed record MonitoringChannelState(
    MonitoringAccessState State,
    string? ReasonCode = null);

public sealed record EnvelopeTraceProjection(
    string? MessageId,
    string? CorrelationId,
    string? CausationId);

public sealed record ManagementTargetProjection(
    string StableKey,
    string ComponentKind,
    string? LogicalInstanceId,
    string HealthState,
    ulong LastObservedAtUnixMillis);

public sealed record MetricProjection(
    string Name,
    string ValueJson,
    ulong ObservedAtUnixMillis,
    IReadOnlyList<KeyValueProjection> Labels);

public sealed record HealthConditionProjection(
    string Code,
    string Severity,
    string Diagnostic);

public sealed record ComponentHealthProjection(
    ManagementTargetProjection Target,
    IReadOnlyList<MetricProjection> Metrics,
    IReadOnlyList<HealthConditionProjection> Conditions);

public sealed record KeyValueProjection(string Key, string Value);

public sealed record CorrelationContextProjection(
    string? CorrelationId,
    string? OperationId,
    string? BatchId,
    ulong? SimulationStep);

public sealed record LogRecordProjection(
    string RecordId,
    ulong TimestampUnixMillis,
    string Severity,
    string EventKind,
    string SourceTargetKey,
    CorrelationContextProjection Correlation,
    IReadOnlyList<KeyValueProjection> Attributes,
    string Diagnostic);

public sealed record AuditRecordProjection(
    string AuditRecordId,
    ulong TimestampUnixMillis,
    string AuditEventKind,
    string ActorAccountRef,
    string? OperationId,
    string? ImmutablePayloadDigest,
    ulong? SimulationStep,
    string TargetKind,
    string ResultCode,
    IReadOnlyList<KeyValueProjection> Attributes);

public sealed record MonitoringSnapshot(
    IReadOnlyList<ManagementTargetProjection> Targets,
    IReadOnlyList<ComponentHealthProjection> Health,
    IReadOnlyList<LogRecordProjection> Logs,
    IReadOnlyList<AuditRecordProjection> Audit,
    EnvelopeTraceProjection? LogPageTrace,
    EnvelopeTraceProjection? AuditPageTrace,
    MonitoringChannelState HealthChannel,
    MonitoringChannelState LogChannel,
    MonitoringChannelState AuditChannel);
