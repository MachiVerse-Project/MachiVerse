using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Observability;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim15ObservabilitySmoke
{
    internal static async Task RunAsync()
    {
        VerifyCanonicalRegistries();
        VerifyStructuredLogRedaction();
        VerifyW3CTracePropagation();
        await VerifyTelemetryFailureWorldIndependenceAsync();
    }

    private static void VerifyCanonicalRegistries()
    {
        var expectedMetrics = new[]
        {
            "machiverse.core.step.duration",
            "machiverse.core.step.lag",
            "machiverse.core.step.overrun",
            "machiverse.core.domain.cpu",
            "machiverse.core.domain.wall",
            "machiverse.core.domain.failure",
            "machiverse.core.intent.count",
            "machiverse.core.event.count",
            "machiverse.core.transaction.count",
            "machiverse.core.conflict.count",
            "machiverse.core.candidate.changed_record_count",
            "machiverse.core.memory.authoritative",
            "machiverse.core.memory.index",
            "machiverse.core.memory.candidate",
            "machiverse.core.operation.pending",
            "machiverse.core.queue.depth",
            "machiverse.core.persistence.commit.duration",
            "machiverse.core.persistence.history.written",
            "machiverse.core.snapshot.duration",
            "machiverse.core.snapshot.size",
            "machiverse.core.recovery.duration",
            "machiverse.core.publication.written",
            "machiverse.core.detail.transition_record_count",
        };
        Require(CoreMetricRegistryV1.Definitions.Count == expectedMetrics.Length,
            "SIM-15 canonical Core metric count mismatch.");
        Require(CoreMetricRegistryV1.Definitions.Select(static definition => definition.Name).SequenceEqual(expectedMetrics),
            "SIM-15 canonical Core metric order/name mismatch.");
        CoreMetricRegistryV1.ValidateContract();
        Require(CoreMetricRegistryV1.EstimatedMaximumActiveSeries <= CoreMetricRegistryV1.StandardActiveSeriesTarget,
            "observability.metric.cardinality expected-series target exceeded.");
        Require(CoreMetricRegistryV1.StandardActiveSeriesTarget < CoreMetricRegistryV1.HardWarningSeriesThreshold,
            "observability.metric.cardinality warning threshold must remain above the normal target.");

        var forbidden = CoreMetricRegistryV1.ForbiddenMetricAttributeNames.ToHashSet(StringComparer.Ordinal);
        Require(CoreMetricRegistryV1.Definitions.All(definition =>
                definition.RequiredAttributes.Concat(definition.OptionalAttributes).All(attribute => !forbidden.Contains(attribute))),
            "observability.metric.no-id-label registry contains a forbidden high-cardinality label.");

        Require(CoreLogEventKindRegistryV1.Kinds.Count == 26,
            "SIM-15 canonical Core LogEventKind count mismatch.");
        Require(CoreLogEventKindRegistryV1.Kinds.Distinct(StringComparer.Ordinal).Count() == 26,
            "SIM-15 Core LogEventKind registry contains duplicates.");
        Require(CoreSpanRegistryV1.Names.Count == 8 && CoreSpanRegistryV1.Names.Distinct(StringComparer.Ordinal).Count() == 8,
            "SIM-15 canonical Core span registry mismatch.");
    }

    private static void VerifyStructuredLogRedaction()
    {
        var sink = new CollectingLogSink();
        using var telemetry = new CoreTelemetryV1(sink);
        telemetry.EmitLog(
            "core.lifecycle.starting",
            CoreLogSeverityV1.Information,
            "core.lifecycle.starting.v1",
            attributes:
            [
                new KeyValuePair<string, string?>("password", "password-secret-corpus"),
                new KeyValuePair<string, string?>("authorization", "Bearer authorization-secret-corpus"),
                new KeyValuePair<string, string?>("cookie", "cookie-secret-corpus"),
                new KeyValuePair<string, string?>("access_token", "token-secret-corpus"),
                new KeyValuePair<string, string?>("safe_field", "visible-value"),
            ]);

        var log = sink.Events.Single();
        Require(log.Attributes["password"] == CoreLogRedactorV1.RedactedValue,
            "observability.log.redaction password was not redacted.");
        Require(log.Attributes["authorization"] == CoreLogRedactorV1.RedactedValue,
            "observability.log.redaction Authorization was not redacted.");
        Require(log.Attributes["cookie"] == CoreLogRedactorV1.RedactedValue,
            "observability.log.redaction cookie was not redacted.");
        Require(log.Attributes["access_token"] == CoreLogRedactorV1.RedactedValue,
            "observability.log.redaction token was not redacted.");
        Require(log.Attributes["safe_field"] == "visible-value",
            "observability.log.redaction removed a safe structured value.");
        var emittedValues = string.Join('|', log.Attributes.Values);
        Require(!emittedValues.Contains("secret-corpus", StringComparison.Ordinal),
            "observability.log.redaction secret corpus leaked into emitted structured values.");
    }

    private static void VerifyW3CTracePropagation()
    {
        Activity? started = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == CoreTelemetryV1.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => started = activity,
        };
        ActivitySource.AddActivityListener(listener);

        const string traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        Require(CoreTelemetryV1.TryParseW3CParentContext(traceParent, null, isRemote: true, out var parent),
            "observability.trace.w3c-propagation could not parse canonical traceparent.");
        Require(parent.IsRemote, "observability.trace.w3c-propagation lost remote-parent identity.");

        using var telemetry = new CoreTelemetryV1();
        using var scope = telemetry.StartSpan(CoreSpanRegistryV1.StepTransition, parent);
        var child = started ?? throw new InvalidOperationException(
            "observability.trace.w3c-propagation did not start a sampled Core span.");
        Require(child.IdFormat == ActivityIdFormat.W3C,
            "observability.trace.w3c-propagation child span is not W3C format.");
        Require(child.TraceId == parent.TraceId,
            "observability.trace.w3c-propagation child span did not preserve TraceId.");
        Require(child.ParentSpanId == parent.SpanId,
            "observability.trace.w3c-propagation child span did not preserve parent SpanId.");
    }

    private static async Task VerifyTelemetryFailureWorldIndependenceAsync()
    {
        var worldId = OpaqueId128.Parse("00000000000000000000000000001500");
        const ulong basisStep = 15;
        var state = CreateWorldState(worldId, basisStep);
        var beforeDigest = state.Diagnostic.StateDigest.ToArray();
        var beforeDiagnostic = CoreStateDiagnosticExporterV1.Project(state);

        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = static (instrument, listener) =>
        {
            if (instrument.Meter.Name == CoreTelemetryV1.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<double>(static (_, _, _, _) =>
            throw new InvalidOperationException("simulated metric exporter failure"));
        meterListener.SetMeasurementEventCallback<long>(static (_, _, _, _) =>
            throw new InvalidOperationException("simulated metric exporter failure"));
        meterListener.Start();

        using var telemetry = new CoreTelemetryV1(new ThrowingLogSink());
        telemetry.RecordStepDuration(TimeSpan.FromMilliseconds(1), "total");
        telemetry.RecordStepLag(TimeSpan.FromMilliseconds(2));
        telemetry.RecordStepOverrun();
        telemetry.EmitLog(
            "core.step.started",
            CoreLogSeverityV1.Debug,
            "core.step.started.v1",
            basisStep);

        var plan = StandardDomainExecutionPlanV1.Create();
        var scheduler = new OperationSchedulerStateV1(basisStep, null, Array.Empty<ScheduledOperationRefV1>());
        var frozen = StepInputFreezerV1.Freeze(state, scheduler);
        var rawRuntimes = plan.Entries
            .Select(static entry => (IDomainRuntimeV1)new EmptyDomainRuntime(entry.DomainToken))
            .ToArray();
        var observedRuntimes = rawRuntimes
            .Select(runtime => (IDomainRuntimeV1)new ObservedDomainRuntimeV1(runtime, telemetry))
            .ToArray();

        var baseline = await DomainRuntimeExecutorV1.ExecuteAsync(plan, state, frozen, rawRuntimes, workerCount: 4);
        var observed = await DomainRuntimeExecutorV1.ExecuteAsync(plan, state, frozen, observedRuntimes, workerCount: 4);
        Require(
            baseline.Select(static output => (output.DomainToken, output.BasisStep, output.Intents.Count, output.LocalPartitionCandidates.Count))
                .SequenceEqual(observed.Select(static output => (output.DomainToken, output.BasisStep, output.Intents.Count, output.LocalPartitionCandidates.Count))),
            "observability.exporter-failure changed deterministic domain output.");
        Require(state.Header.Step == basisStep,
            "observability.exporter-failure changed authoritative world Step.");
        Require(state.Diagnostic.StateDigest.SequenceEqual(beforeDigest),
            "observability.exporter-failure changed authoritative world digest.");

        var afterDiagnostic = CoreStateDiagnosticExporterV1.Project(state);
        Require(afterDiagnostic.BasisStep == beforeDiagnostic.BasisStep &&
                afterDiagnostic.StateDigest.SequenceEqual(beforeDiagnostic.StateDigest) &&
                afterDiagnostic.ConfigDigest.SequenceEqual(beforeDiagnostic.ConfigDigest) &&
                afterDiagnostic.SchemaRegistryDigest.SequenceEqual(beforeDiagnostic.SchemaRegistryDigest),
            "StateDiagnostic export changed with telemetry/exporter failure.");
    }

    private static WorldStateV1 CreateWorldState(OpaqueId128 worldId, ulong step)
    {
        var zero = new byte[32];
        var partitions = StandardDomainPartitionRegistry.Entries.Select(entry => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                entry,
                revision: 1,
                basisStep: step,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(entry.PartitionId.Value)))));
        var header = new WorldStateHeaderV1(
            worldId,
            step,
            worldSeedDigest: zero,
            configGeneration: 1,
            masterGeneration: 1,
            rateGeneration: 1);
        return new WorldStateV1(
            header,
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            zero);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CollectingLogSink : ICoreStructuredLogSinkV1
    {
        internal List<CoreStructuredLogEventV1> Events { get; } = [];

        public void Emit(CoreStructuredLogEventV1 logEvent)
            => Events.Add(logEvent);
    }

    private sealed class ThrowingLogSink : ICoreStructuredLogSinkV1
    {
        public void Emit(CoreStructuredLogEventV1 logEvent)
            => throw new InvalidOperationException("simulated log exporter failure");
    }

    private sealed class EmptyDomainRuntime(StableToken domainToken) : IDomainRuntimeV1
    {
        public StableToken DomainToken { get; } = domainToken;

        public ValueTask<DomainCandidateOutputV1> ExecuteAsync(
            DomainRuntimeContextV1 context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new DomainCandidateOutputV1(DomainToken, context.FrozenInput.BasisStep));
        }
    }
}
