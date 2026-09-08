using Google.Protobuf;
using MachiVerse.Protocol.V1;

namespace MachiVerse.Administration.View.Modules.Monitoring;

public sealed class MonitoringProjectionStore : IMonitoringModuleBoundary
{
    private readonly Dictionary<string, ComponentHealthProjection> _health = new(StringComparer.Ordinal);
    private IReadOnlyList<LogRecordProjection> _logs = Array.Empty<LogRecordProjection>();
    private IReadOnlyList<AuditRecordProjection> _audit = Array.Empty<AuditRecordProjection>();
    private EnvelopeTraceProjection? _logPageTrace;
    private EnvelopeTraceProjection? _auditPageTrace;
    private MonitoringChannelState _healthChannel = new(MonitoringAccessState.Unavailable, "not-loaded");
    private MonitoringChannelState _logChannel = new(MonitoringAccessState.Unavailable, "not-loaded");
    private MonitoringChannelState _auditChannel = new(MonitoringAccessState.Unavailable, "not-loaded");

    public event Action? Changed;

    public MonitoringSnapshot Snapshot => new(
        Targets: _health.Values.Select(static x => x.Target).OrderBy(static x => x.StableKey, StringComparer.Ordinal).ToArray(),
        Health: _health.Values.OrderBy(static x => x.Target.StableKey, StringComparer.Ordinal).ToArray(),
        Logs: _logs,
        Audit: _audit,
        LogPageTrace: _logPageTrace,
        AuditPageTrace: _auditPageTrace,
        HealthChannel: _healthChannel,
        LogChannel: _logChannel,
        AuditChannel: _auditChannel);

    public bool TryApply(WireEnvelopeV1 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        switch (envelope.MessageType)
        {
            case "component.health.result":
                RequireSchema(envelope, "protocol.component-health.v1");
                ApplyHealth(ComponentHealthV1.Parser.ParseFrom(envelope.Payload));
                return true;
            case "component.log.page":
                RequireSchema(envelope, "protocol.log-page.v1");
                ApplyLogPage(LogPageV1.Parser.ParseFrom(envelope.Payload), ProjectTrace(envelope));
                return true;
            case "audit.page":
                RequireSchema(envelope, "protocol.audit-page.v1");
                ApplyAuditPage(AuditPageV1.Parser.ParseFrom(envelope.Payload), ProjectTrace(envelope));
                return true;
            default:
                return false;
        }
    }

    public void SetChannelAccess(MonitoringChannel channel, MonitoringAccessState state, string? reasonCode = null)
    {
        var value = new MonitoringChannelState(state, reasonCode);
        switch (channel)
        {
            case MonitoringChannel.Health:
                _healthChannel = value;
                break;
            case MonitoringChannel.Logs:
                _logChannel = value;
                break;
            case MonitoringChannel.Audit:
                _auditChannel = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(channel));
        }

        Changed?.Invoke();
    }

    private void ApplyHealth(ComponentHealthV1 health)
    {
        var target = ProjectTarget(health);
        var metrics = health.Metrics.Select(ProjectMetric).ToArray();
        var conditions = health.Conditions
            .Select(static condition => new HealthConditionProjection(
                condition.Code,
                condition.Severity.ToString(),
                condition.Diagnostic))
            .ToArray();

        _health[target.StableKey] = new ComponentHealthProjection(target, metrics, conditions);
        _healthChannel = new MonitoringChannelState(MonitoringAccessState.Available);
        Changed?.Invoke();
    }

    private void ApplyLogPage(LogPageV1 page, EnvelopeTraceProjection trace)
    {
        _logs = page.Records.Select(ProjectLog).ToArray();
        _logPageTrace = trace;
        _logChannel = new MonitoringChannelState(MonitoringAccessState.Available);
        Changed?.Invoke();
    }

    private void ApplyAuditPage(AuditPageV1 page, EnvelopeTraceProjection trace)
    {
        _audit = page.Records.Select(ProjectAudit).ToArray();
        _auditPageTrace = trace;
        _auditChannel = new MonitoringChannelState(MonitoringAccessState.Available);
        Changed?.Invoke();
    }

    private static ManagementTargetProjection ProjectTarget(ComponentHealthV1 health)
    {
        var componentKind = health.Target.ComponentKind.ToString();
        var instanceId = health.Target.HasLogicalInstanceId ? Hex(health.Target.LogicalInstanceId) : null;
        var stableKey = instanceId is null ? componentKind : $"{componentKind}:{instanceId}";
        var observedAt = health.Metrics.Count == 0 ? 0UL : health.Metrics.Max(static metric => metric.ObservedAtUnixMillis);

        return new ManagementTargetProjection(
            stableKey,
            componentKind,
            instanceId,
            health.Health.ToString(),
            observedAt);
    }

    private static MetricProjection ProjectMetric(MetricSampleV1 metric)
        => new(
            metric.Name,
            JsonFormatter.Default.Format(metric.Value),
            metric.ObservedAtUnixMillis,
            metric.Labels.Select(static label => new KeyValueProjection(label.Key, label.Value)).ToArray());

    private static LogRecordProjection ProjectLog(StructuredLogRecordV1 record)
    {
        var sourceKind = record.Source.ComponentKind.ToString();
        var sourceInstance = record.Source.HasLogicalInstanceId ? Hex(record.Source.LogicalInstanceId) : null;
        var sourceKey = sourceInstance is null ? sourceKind : $"{sourceKind}:{sourceInstance}";

        return new LogRecordProjection(
            Hex(record.RecordId),
            record.TimestampUnixMillis,
            record.Severity.ToString(),
            record.EventKind,
            sourceKey,
            new CorrelationContextProjection(
                record.HasCorrelationId ? Hex(record.CorrelationId) : null,
                record.HasOperationId ? Hex(record.OperationId) : null,
                record.HasBatchId ? Hex(record.BatchId) : null,
                record.HasSimulationStep ? record.SimulationStep : null),
            record.Attributes.Select(static attribute => new KeyValueProjection(attribute.Key, attribute.Value)).ToArray(),
            record.Diagnostic);
    }

    private static AuditRecordProjection ProjectAudit(AuditRecordWireV1 record)
        => new(
            Hex(record.AuditRecordId),
            record.TimestampUnixMillis,
            record.AuditEventKind,
            Hex(record.ActorAccountRef),
            record.HasOperationId ? Hex(record.OperationId) : null,
            record.HasImmutablePayloadDigest ? Hex(record.ImmutablePayloadDigest) : null,
            record.HasSimulationStep ? record.SimulationStep : null,
            record.TargetKind,
            record.ResultCode,
            record.Attributes.Select(static attribute => new KeyValueProjection(attribute.Key, attribute.Value)).ToArray());

    private static EnvelopeTraceProjection ProjectTrace(WireEnvelopeV1 envelope)
        => new(
            envelope.MessageId.Length == 16 ? Hex(envelope.MessageId) : null,
            envelope.CorrelationId.Length == 16 ? Hex(envelope.CorrelationId) : null,
            envelope.HasCausationId && envelope.CausationId.Length == 16 ? Hex(envelope.CausationId) : null);

    private static void RequireSchema(WireEnvelopeV1 envelope, string expected)
    {
        if (!string.Equals(envelope.PayloadSchemaId, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Message '{envelope.MessageType}' expected payload schema '{expected}', received '{envelope.PayloadSchemaId}'.");
        }
    }

    private static string Hex(ByteString value)
        => Convert.ToHexString(value.ToByteArray()).ToLowerInvariant();
}
