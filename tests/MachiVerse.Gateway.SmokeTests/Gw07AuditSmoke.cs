using System.Runtime.CompilerServices;
using MachiVerse.Gateway.Audit;
using MachiVerse.Gateway.Configuration;
using MachiVerse.Gateway.Observability;

internal static class Gw07AuditSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var config = GatewayConfigLoader.LoadFile("config/gateway.toml");
        Require(config.Audit.RetentionDays == 400, "GW-07 audit retention default must be 400 days.");
        Require(config.Audit.QueryMaxPageSize == 1000, "GW-07 audit query max page size must be 1000.");

        GatewayObservabilityRegistry.RequireLogEventKind("gateway.authorization.denied");
        var metric = GatewayObservabilityRegistry.RequireMetric("machiverse.gateway.authorization.denied");
        GatewayObservabilityRegistry.ValidateMetricAttributes(metric, new Dictionary<string, string>
        {
            ["permission_class"] = "admin.config.write",
        });

        RequireReject(
            () => GatewayObservabilityRegistry.ValidateMetricAttributes(metric, new Dictionary<string, string>
            {
                ["operation_id"] = "1234",
            }),
            "observability.metric-unbounded-label");

        var redacted = GatewayObservabilityRegistry.RedactDiagnosticAttributes(new Dictionary<string, string>
        {
            ["authorization"] = "Bearer abcdefghijklmnop",
            ["result"] = "ok",
        });
        Require(redacted["authorization"] == "[redacted]", "Credential-like log material must be redacted.");
        Require(redacted["result"] == "ok", "Non-secret diagnostic values must survive redaction.");

        var instanceId = Enumerable.Repeat((byte)1, 16).ToArray();
        var first = AuditRecordCodecV1.Create(1, new byte[32], Draft("audit.admin.config-read", 1, instanceId));
        AuditRecordCodecV1.ValidateDigest(first);
        var second = AuditRecordCodecV1.Create(2, first.RecordDigest, Draft("audit.admin.config-change-requested", 2, instanceId));
        AuditRecordCodecV1.ValidateDigest(second);
        Require(second.PreviousDigest.AsSpan().SequenceEqual(first.RecordDigest), "Audit chain must preserve previous digest identity.");

        var temp = Path.Combine(Path.GetTempPath(), "machiverse-gw07-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            RunStoreAsync(temp, config, instanceId).GetAwaiter().GetResult();
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static async Task RunStoreAsync(string temp, GatewayConfig config, byte[] instanceId)
    {
        await using var store = new GatewayAuditStoreV1(temp, config.Audit.QueryMaxPageSize);
        await store.InitializeAsync();
        await store.AppendAsync(Draft("audit.admin.config-read", 10, instanceId));
        await store.AppendAsync(Draft("audit.admin.config-change-requested", 20, instanceId));
        await store.ValidateChainAsync();

        var page = await store.QueryAsync(null, 100);
        Require(page.Count == 2 && page[0].AuditSequence == 1 && page[1].AuditSequence == 2,
            "Audit query must preserve sequence order.");
        await RequireRejectAsync(() => store.QueryAsync(null, config.Audit.QueryMaxPageSize + 1).AsTask(), "audit.query-page-size-out-of-range");

        var anchor = await store.ApplyRetentionAsync(
            cutoffObservedAtUnixNs: 15,
            policyGeneration: 1,
            retentionActionAudit: Draft("audit.admin.audit-export", 30, instanceId));
        Require(anchor is not null && anchor.DeletedThroughSequence == 1 && anchor.FirstRetainedSequence == 2,
            "Retention must delete a complete prefix and preserve a boundary anchor.");
        await store.ValidateChainAsync();

        var failing = new ThrowingAuditWriter();
        var gate = new ProtectedAdminAuditGateV1(failing);
        var forwarded = false;
        try
        {
            await gate.ForwardAsync(
                Draft("audit.admin.command-requested", 40, instanceId),
                _ =>
                {
                    forwarded = true;
                    return ValueTask.FromResult("forwarded");
                },
                resultAuditFactory: null);
            throw new InvalidOperationException("Protected Admin request should fail closed when request audit cannot commit.");
        }
        catch (ProtectedAdminAuditUnavailableException ex) when (ex.Message == "component.unavailable")
        {
        }
        Require(!forwarded, "Protected Admin request audit failure must result in zero downstream forwards.");

        var postMutationWriter = new FailSecondAuditWriter();
        var observedPostFailure = false;
        var postGate = new ProtectedAdminAuditGateV1(postMutationWriter, _ => observedPostFailure = true);
        var result = await postGate.ForwardAsync(
            Draft("audit.admin.command-requested", 50, instanceId),
            _ => ValueTask.FromResult("mutated"),
            _ => Draft("audit.admin.command-completed", 60, instanceId));
        Require(result.Result == "mutated" && !result.ResultAuditCommitted && observedPostFailure,
            "Post-mutation audit failure must not roll back authoritative downstream completion.");
    }

    private static AuditRecordDraftV1 Draft(string kind, long observedAt, byte[] instanceId)
        => new(
            kind,
            observedAt,
            "gateway",
            instanceId,
            ActorRef: "actor-ref",
            SessionRefDigest: null,
            OperationId: null,
            CorrelationId: null,
            TargetRef: "gateway",
            WorldId: null,
            SimulationStep: null,
            ConfigGeneration: null,
            RequestDigest: null,
            ResultStatus: "success",
            ResultCode: "ok",
            ApprovalEvidenceDigest: null,
            SummaryFields: new Dictionary<string, string>());

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireReject(Action action, string expected)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expected)
        {
            return;
        }
        throw new InvalidOperationException($"Expected rejection: {expected}");
    }

    private static async Task RequireRejectAsync(Func<Task> action, string expected)
    {
        try
        {
            await action();
        }
        catch (InvalidDataException ex) when (ex.Message == expected)
        {
            return;
        }
        throw new InvalidOperationException($"Expected rejection: {expected}");
    }

    private sealed class ThrowingAuditWriter : IAuditWriterV1
    {
        public ValueTask<AuditRecordV1> AppendAsync(AuditRecordDraftV1 draft, CancellationToken cancellationToken = default)
            => ValueTask.FromException<AuditRecordV1>(new IOException("audit unavailable"));
    }

    private sealed class FailSecondAuditWriter : IAuditWriterV1
    {
        private int _calls;

        public ValueTask<AuditRecordV1> AppendAsync(AuditRecordDraftV1 draft, CancellationToken cancellationToken = default)
        {
            _calls++;
            if (_calls == 2)
                return ValueTask.FromException<AuditRecordV1>(new IOException("result audit unavailable"));
            var record = AuditRecordCodecV1.Create(1, new byte[32], draft);
            return ValueTask.FromResult(record);
        }
    }
}
