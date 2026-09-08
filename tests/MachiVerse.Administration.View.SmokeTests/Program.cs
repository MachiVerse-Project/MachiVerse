using Google.Protobuf;
using MachiVerse.Administration.View.Modules.Management;
using MachiVerse.Administration.View.Modules.Monitoring;
using MachiVerse.Protocol.V1;

// ADMIN-02 observability projection coverage.
var store = new MonitoringProjectionStore();
Assert(store.Snapshot.HealthChannel.State == MonitoringAccessState.Unavailable);
Assert(store.Snapshot.LogChannel.State == MonitoringAccessState.Unavailable);
Assert(store.Snapshot.AuditChannel.State == MonitoringAccessState.Unavailable);

var target = new ComponentTargetV1
{
    ComponentKind = (ComponentKindV1)2, // COMPONENT_KIND_GATEWAY
    LogicalInstanceId = Id(1),
};

var health = new ComponentHealthV1
{
    Target = target,
    Health = (HealthStateV1)1, // HEALTH_STATE_HEALTHY
};
health.Metrics.Add(new MetricSampleV1
{
    Name = "gateway.connection.count",
    Value = new MetricValueV1 { UintValue = 3 },
    ObservedAtUnixMillis = 1000,
});
health.Conditions.Add(new HealthConditionV1
{
    Code = "gateway.ready",
    Severity = (HealthConditionSeverityV1)1, // HEALTH_CONDITION_SEVERITY_INFO
    Diagnostic = "fixture-ready",
});

Assert(store.TryApply(Envelope("component.health.result", "protocol.component-health.v1", health)));
Assert(store.Snapshot.Targets.Count == 1);
Assert(store.Snapshot.Health.Count == 1);
Assert(store.Snapshot.Health[0].Metrics.Count == 1);

var logPage = new LogPageV1();
logPage.Records.Add(new StructuredLogRecordV1
{
    RecordId = Id(2),
    TimestampUnixMillis = 1100,
    Severity = (LogSeverityV1)3, // LOG_SEVERITY_INFORMATION
    EventKind = "gateway.fixture",
    Source = target,
    CorrelationId = Id(3),
    OperationId = Id(4),
    Diagnostic = "server-provided diagnostic",
});
Assert(store.TryApply(Envelope("component.log.page", "protocol.log-page.v1", logPage)));
Assert(store.Snapshot.Logs.Count == 1);
Assert(store.Snapshot.Audit.Count == 0);
Assert(store.Snapshot.Logs[0].Correlation.CorrelationId is not null);
Assert(store.Snapshot.LogPageTrace?.CausationId is not null);

var auditPage = new AuditPageV1();
auditPage.Records.Add(new AuditRecordWireV1
{
    AuditRecordId = Id(5),
    TimestampUnixMillis = 1200,
    AuditEventKind = "admin.fixture",
    ActorAccountRef = Id(6),
    OperationId = Id(4),
    TargetKind = "gateway",
    ResultCode = "ok",
});
Assert(store.TryApply(Envelope("audit.page", "protocol.audit-page.v1", auditPage)));
Assert(store.Snapshot.Audit.Count == 1);
Assert(store.Snapshot.Logs.Count == 1);
Assert(store.Snapshot.AuditPageTrace?.CorrelationId is not null);

var healthQuery = MonitoringQueryBuilder.BuildHealth([target], ["gateway.connection.count"]);
Assert(healthQuery.Targets.Count == 1);
Assert(healthQuery.MetricNames.Count == 1);

var logQuery = MonitoringQueryBuilder.BuildLog(new LogQueryOptions(
    Targets: [target],
    FromUnixMillis: 1000,
    ToUnixMillis: 2000,
    EventKinds: ["gateway.fixture"],
    CorrelationId: Id(3),
    OperationId: Id(4),
    BasisStep: 10,
    PageSize: 200,
    Cursor: null));
Assert(logQuery.PageSize == 200);
Assert(logQuery.HasCorrelationId);

var auditQuery = MonitoringQueryBuilder.BuildAudit(new AuditQueryOptions(
    FromUnixMillis: 1000,
    ToUnixMillis: 2000,
    AuditEventKinds: ["admin.fixture"],
    OperationId: Id(4),
    SimulationStep: 10,
    PageSize: 200,
    Cursor: null));
Assert(auditQuery.PageSize == 200);
Assert(auditQuery.HasOperationId);

var csv = AuditExportFormatter.ToCsv(store.Snapshot.Audit);
Assert(csv.Contains("admin.fixture", StringComparison.Ordinal));
Assert(csv.Contains("gateway", StringComparison.Ordinal));

store.SetChannelAccess(MonitoringChannel.Audit, MonitoringAccessState.Unauthorized, "auth.unauthorized");
Assert(store.Snapshot.AuditChannel.State == MonitoringAccessState.Unauthorized);
Assert(store.Snapshot.AuditChannel.ReasonCode == "auth.unauthorized");
store.SetChannelAccess(MonitoringChannel.Logs, MonitoringAccessState.Redacted, "security.redacted");
Assert(store.Snapshot.LogChannel.State == MonitoringAccessState.Redacted);

var mismatched = Envelope("component.log.page", "protocol.audit-page.v1", logPage);
AssertThrows<InvalidDataException>(() => store.TryApply(mismatched));
AssertThrows<ArgumentOutOfRangeException>(() => MonitoringQueryBuilder.BuildAudit(new AuditQueryOptions(
    null, null, [], null, null, 0, null)));

// ADMIN-03 Config / operational command management coverage.
var commandDescriptor = new CommandDescriptor(
    CommandKind: "fixture.low-impact",
    AllowedTargetKinds: [(ComponentKindV1)2],
    PayloadSchemaId: "fixture.command.v1",
    PayloadSchemaMajor: 1,
    PayloadSchemaMinor: 0,
    RequiredPermission: "admin.command.execute.low-impact",
    ImpactClassification: "low-impact",
    StateChanging: true);
var commandCatalog = new OperationalCommandCatalog([commandDescriptor]);
var management = new ManagementProjectionStore(commandCatalog);
Assert(management.Snapshot.ConfigChannel.State == ManagementAccessState.Unavailable);
Assert(management.Snapshot.CommandChannel.State == ManagementAccessState.Unavailable);

var configReadResult = new ConfigReadResultV1
{
    Result = new ResultV1
    {
        Status = (ResultStatusV1)1, // RESULT_STATUS_SUCCESS
        Code = "ok",
        RetryAdvice = (RetryAdviceV1)1, // RETRY_ADVICE_DO_NOT_RETRY
    },
    Target = target,
    ConfigGeneration = 5,
    ConfigDigest = Digest(10),
};
configReadResult.Entries.Add(new ConfigEntryWireV1
{
    Key = "observability.log-level",
    EffectiveValue = new ConfigValueWireV1 { StringValue = "info" },
    Impact = "operational",
    Mutability = "runtime-safe",
});
configReadResult.Entries.Add(new ConfigEntryWireV1
{
    Key = "auth.oidc.client-secret-ref",
    EffectiveValue = new ConfigValueWireV1 { StringValue = "must-not-be-presented" },
    Impact = "operational",
    Mutability = "restart-required",
    Sensitive = true,
});
Assert(management.TryApply(Envelope("config.read.result", "protocol.config-read-result.v1", configReadResult)));
Assert(management.Snapshot.ConfigTargets.Count == 1);
Assert(management.Snapshot.ConfigTargets[0].ConfigGeneration == 5);
Assert(management.Snapshot.ConfigTargets[0].Entries.Single(x => x.Sensitive).EffectiveValueJson is null);
Assert(management.Snapshot.ConfigTargets[0].Entries.Single(x => x.Sensitive).Redacted);

var readRequest = management.BuildConfigRead(
    target,
    ["observability.log-level", "network.reconnect-max-ms", "observability.log-level"]);
Assert(readRequest.Keys.Count == 2);
Assert(readRequest.Keys[0] == "network.reconnect-max-ms");
Assert(readRequest.Keys[1] == "observability.log-level");

var currentConfig = management.Snapshot.ConfigTargets.Single();
var draft = management.CreateDraft(currentConfig).WithEdits([
    new ConfigChangeEdit("observability.log-level", new ConfigValueWireV1 { StringValue = "debug" }),
    new ConfigChangeEdit("network.reconnect-max-ms", new ConfigValueWireV1 { UintValue = 20000 }),
]);
var configOperationId = Id(20);
var configDigest = Digest(21);
var configChange = management.PrepareConfigChange(draft, configOperationId, configDigest);
Assert(configChange.ExpectedBaseGeneration == 5);
Assert(configChange.Changes.Count == 2);
Assert(configChange.Changes[0].Key == "network.reconnect-max-ms");
Assert(configChange.Changes[1].Key == "observability.log-level");
Assert(configChange.OperationId.Equals(configOperationId));
Assert(configChange.ImmutablePayloadDigest.Equals(configDigest));

var retryConfigChange = management.RetryConfigChange(configOperationId);
Assert(retryConfigChange.OperationId.Equals(configOperationId));
Assert(retryConfigChange.ImmutablePayloadDigest.Equals(configDigest));
Assert(retryConfigChange.ExpectedBaseGeneration == 5);

AssertThrows<InvalidDataException>(() => management.PrepareConfigChange(
    management.CreateDraft(currentConfig).WithEdits([
        new ConfigChangeEdit("observability.log-level", new ConfigValueWireV1 { StringValue = "info" }),
        new ConfigChangeEdit("observability.log-level", new ConfigValueWireV1 { StringValue = "warn" }),
    ]),
    Id(22),
    Digest(22)));

management.MarkSubmitted(configOperationId);
management.MarkDeliveryUnknown(configOperationId);
Assert(management.Snapshot.Mutations.Single(x => x.OperationId == Hex(configOperationId)).State == ManagementMutationState.DeliveryUnknown);

var staleResult = new ConfigChangeResultV1
{
    Result = new ResultV1
    {
        Status = (ResultStatusV1)6, // RESULT_STATUS_REJECTED
        Code = "config.stale-generation",
        RetryAdvice = (RetryAdviceV1)4, // RETRY_ADVICE_RESYNC_THEN_RETRY
    },
};
Assert(management.TryApply(MutationEnvelope(
    "config.change.result",
    "protocol.config-change-result.v1",
    staleResult,
    configOperationId,
    configDigest)));
Assert(management.Snapshot.Mutations.Single(x => x.OperationId == Hex(configOperationId)).State == ManagementMutationState.StaleGeneration);

var valueReturnDraft = management.CreateValueReturnDraft(currentConfig, [
    new ConfigChangeEdit("observability.log-level", new ConfigValueWireV1 { StringValue = "info" }),
]);
Assert(valueReturnDraft.BaseConfigGeneration == currentConfig.ConfigGeneration);
Assert(valueReturnDraft.Edits.Count == 1);

AssertThrows<InvalidOperationException>(() => management.PrepareOperationalCommand(
    "not.registered",
    target,
    ByteString.Empty));
AssertThrows<InvalidDataException>(() => management.PrepareOperationalCommand(
    commandDescriptor.CommandKind,
    target,
    ByteString.CopyFromUtf8("fixture-payload")));

var commandOperationId = Id(30);
var commandDigest = Digest(31);
var command = management.PrepareOperationalCommand(
    commandDescriptor.CommandKind,
    target,
    ByteString.CopyFromUtf8("fixture-payload"),
    commandOperationId,
    commandDigest);
Assert(command.HasOperationId);
Assert(command.HasImmutablePayloadDigest);
Assert(command.PayloadSchemaId == commandDescriptor.PayloadSchemaId);
var retryCommand = management.RetryOperationalCommand(commandOperationId);
Assert(retryCommand.OperationId.Equals(commandOperationId));
Assert(retryCommand.ImmutablePayloadDigest.Equals(commandDigest));

AssertThrows<InvalidDataException>(() => management.PrepareOperationalCommand(
    commandDescriptor.CommandKind,
    target,
    ByteString.CopyFromUtf8("different-payload"),
    commandOperationId,
    Digest(32)));

var commandResult = new OperationStatusResultV1
{
    OperationId = commandOperationId,
    State = (OperationLifecycleWireStateV1)4, // OPERATION_LIFECYCLE_TERMINAL
    OperationPayloadDigest = commandDigest,
    TerminalResult = new ResultV1
    {
        Status = (ResultStatusV1)1,
        Code = "ok",
        RetryAdvice = (RetryAdviceV1)1,
    },
};
Assert(management.TryApply(Envelope("operation.result", "protocol.operation-status-result.v1", commandResult)));
var trackedCommand = management.Snapshot.Mutations.Single(x => x.OperationId == Hex(commandOperationId));
Assert(trackedCommand.State == ManagementMutationState.Terminal);
Assert(trackedCommand.ResultCode == "ok");

Console.WriteLine("ADMIN-02 and ADMIN-03 smoke checks passed.");

static WireEnvelopeV1 Envelope(string messageType, string schemaId, IMessage payload)
    => new()
    {
        MessageType = messageType,
        MessageId = Id(7),
        CorrelationId = Id(8),
        CausationId = Id(9),
        PayloadSchemaId = schemaId,
        Payload = payload.ToByteString(),
    };

static WireEnvelopeV1 MutationEnvelope(
    string messageType,
    string schemaId,
    IMessage payload,
    ByteString operationId,
    ByteString digest)
{
    var envelope = Envelope(messageType, schemaId, payload);
    envelope.OperationContext = new OperationContextWireV1
    {
        OperationId = operationId,
        OperationPayloadDigest = digest,
    };
    return envelope;
}

static ByteString Id(byte first)
{
    var bytes = new byte[16];
    bytes[0] = first;
    return ByteString.CopyFrom(bytes);
}

static ByteString Digest(byte first)
{
    var bytes = new byte[32];
    bytes[0] = first;
    return ByteString.CopyFrom(bytes);
}

static string Hex(ByteString value)
    => Convert.ToHexString(value.ToByteArray()).ToLowerInvariant();

static void Assert(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Smoke assertion failed.");
    }
}

static void AssertThrows<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
