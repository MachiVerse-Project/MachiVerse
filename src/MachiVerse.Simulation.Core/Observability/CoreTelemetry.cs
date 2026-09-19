using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Observability;

public enum CoreLogSeverityV1
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
}

public sealed record CoreStructuredLogEventV1(
    string EventKind,
    CoreLogSeverityV1 Severity,
    long ObservedAtUnixNanoseconds,
    string Component,
    string MessageTemplateId,
    ulong? SimulationStep,
    StableToken? Domain,
    string? ResultCode,
    IReadOnlyDictionary<string, string?> Attributes,
    string? ExceptionType);

public interface ICoreStructuredLogSinkV1
{
    void Emit(CoreStructuredLogEventV1 logEvent);
}

public sealed class NullCoreStructuredLogSinkV1 : ICoreStructuredLogSinkV1
{
    public static NullCoreStructuredLogSinkV1 Instance { get; } = new();

    private NullCoreStructuredLogSinkV1()
    {
    }

    public void Emit(CoreStructuredLogEventV1 logEvent)
        => ArgumentNullException.ThrowIfNull(logEvent);
}

public static class CoreLogRedactorV1
{
    private static readonly string[] SecretKeyFragments =
    [
        "password",
        "passwd",
        "access_token",
        "refresh_token",
        "id_token",
        "authorization",
        "cookie",
        "private_key",
        "client_secret",
        "credential",
        "secret",
        "oidc_code",
    ];

    public const string RedactedValue = "[REDACTED]";

    public static IReadOnlyDictionary<string, string?> Sanitize(
        IEnumerable<KeyValuePair<string, string?>>? attributes)
    {
        if (attributes is null)
            return new Dictionary<string, string?>(StringComparer.Ordinal);

        var sanitized = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var pair in attributes)
        {
            if (string.IsNullOrWhiteSpace(pair.Key)) continue;
            var key = pair.Key.Trim();
            sanitized[key] = IsSecretKey(key) ? RedactedValue : SanitizeValue(pair.Value);
        }
        return sanitized;
    }

    public static bool IsSecretKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var normalized = key.Replace('-', '_').Replace('.', '_').ToLowerInvariant();
        return SecretKeyFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal));
    }

    private static string? SanitizeValue(string? value)
    {
        if (value is null) return null;
        const int maximumDiagnosticValueLength = 512;
        return value.Length <= maximumDiagnosticValueLength
            ? value
            : value[..maximumDiagnosticValueLength];
    }
}

public sealed record CoreStateDiagnosticExportV1(
    ulong BasisStep,
    byte[] StateDigest,
    byte[] ConfigDigest,
    byte[] SchemaRegistryDigest,
    IReadOnlyList<KeyValuePair<string, byte[]>> PartitionDigests);

public static class CoreStateDiagnosticExporterV1
{
    public static CoreStateDiagnosticExportV1 Project(WorldStateV1 state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var diagnostic = state.Diagnostic;
        var partitions = diagnostic.PartitionDigests
            .Select(static pair => new KeyValuePair<string, byte[]>(pair.Key, pair.Value.ToArray()))
            .ToArray();
        return new CoreStateDiagnosticExportV1(
            state.Header.Step,
            diagnostic.StateDigest.ToArray(),
            diagnostic.ConfigDigest.ToArray(),
            diagnostic.SchemaRegistryDigest.ToArray(),
            Array.AsReadOnly(partitions));
    }
}

public sealed class CoreTelemetryV1 : IDisposable
{
    public const string MeterName = "MachiVerse.Simulation.Core";
    public const string ActivitySourceName = "MachiVerse.Simulation.Core";

    private readonly Meter _meter;
    private readonly ActivitySource _activitySource;
    private readonly ICoreStructuredLogSinkV1 _logSink;
    private readonly TimeProvider _timeProvider;
    private readonly CoreMetricCardinalityGuardV1 _cardinality = new();

    private readonly Histogram<double> _stepDuration;
    private readonly Gauge<double> _stepLag;
    private readonly Counter<long> _stepOverrun;
    private readonly Histogram<double> _domainCpu;
    private readonly Histogram<double> _domainWall;
    private readonly Counter<long> _domainFailure;
    private readonly Histogram<long> _intentCount;
    private readonly Histogram<long> _eventCount;
    private readonly Histogram<long> _transactionCount;
    private readonly Counter<long> _conflictCount;
    private readonly Histogram<long> _candidateChangedRecordCount;
    private readonly Gauge<long> _memoryAuthoritative;
    private readonly Gauge<long> _memoryIndex;
    private readonly Gauge<long> _memoryCandidate;
    private readonly Gauge<long> _operationPending;
    private readonly Gauge<long> _queueDepth;
    private readonly Histogram<double> _persistenceCommitDuration;
    private readonly Counter<long> _persistenceHistoryWritten;
    private readonly Histogram<double> _snapshotDuration;
    private readonly Histogram<long> _snapshotSize;
    private readonly Histogram<double> _recoveryDuration;
    private readonly Counter<long> _publicationWritten;
    private readonly Histogram<long> _detailTransitionRecordCount;

    public CoreTelemetryV1(
        ICoreStructuredLogSinkV1? logSink = null,
        TimeProvider? timeProvider = null)
    {
        CoreMetricRegistryV1.ValidateContract();
        _logSink = logSink ?? NullCoreStructuredLogSinkV1.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _meter = new Meter(MeterName, "1.0");
        _activitySource = new ActivitySource(ActivitySourceName, "1.0");

        _stepDuration = _meter.CreateHistogram<double>("machiverse.core.step.duration", "ms");
        _stepLag = _meter.CreateGauge<double>("machiverse.core.step.lag", "ms");
        _stepOverrun = _meter.CreateCounter<long>("machiverse.core.step.overrun", "{overrun}");
        _domainCpu = _meter.CreateHistogram<double>("machiverse.core.domain.cpu", "ms");
        _domainWall = _meter.CreateHistogram<double>("machiverse.core.domain.wall", "ms");
        _domainFailure = _meter.CreateCounter<long>("machiverse.core.domain.failure", "{failure}");
        _intentCount = _meter.CreateHistogram<long>("machiverse.core.intent.count", "{intent}");
        _eventCount = _meter.CreateHistogram<long>("machiverse.core.event.count", "{event}");
        _transactionCount = _meter.CreateHistogram<long>("machiverse.core.transaction.count", "{transaction}");
        _conflictCount = _meter.CreateCounter<long>("machiverse.core.conflict.count", "{conflict}");
        _candidateChangedRecordCount = _meter.CreateHistogram<long>("machiverse.core.candidate.changed_record_count", "{record}");
        _memoryAuthoritative = _meter.CreateGauge<long>("machiverse.core.memory.authoritative", "By");
        _memoryIndex = _meter.CreateGauge<long>("machiverse.core.memory.index", "By");
        _memoryCandidate = _meter.CreateGauge<long>("machiverse.core.memory.candidate", "By");
        _operationPending = _meter.CreateGauge<long>("machiverse.core.operation.pending", "{operation}");
        _queueDepth = _meter.CreateGauge<long>("machiverse.core.queue.depth", "{item}");
        _persistenceCommitDuration = _meter.CreateHistogram<double>("machiverse.core.persistence.commit.duration", "ms");
        _persistenceHistoryWritten = _meter.CreateCounter<long>("machiverse.core.persistence.history.written", "By");
        _snapshotDuration = _meter.CreateHistogram<double>("machiverse.core.snapshot.duration", "s");
        _snapshotSize = _meter.CreateHistogram<long>("machiverse.core.snapshot.size", "By");
        _recoveryDuration = _meter.CreateHistogram<double>("machiverse.core.recovery.duration", "s");
        _publicationWritten = _meter.CreateCounter<long>("machiverse.core.publication.written", "By");
        _detailTransitionRecordCount = _meter.CreateHistogram<long>("machiverse.core.detail.transition_record_count", "{record}");
    }

    public void RecordStepDuration(TimeSpan duration, string? phase = null)
    {
        KeyValuePair<string, object?>[] tags = phase is null ? [] : [Tag("phase", phase)];
        Record("machiverse.core.step.duration", tags, () => _stepDuration.Record(duration.TotalMilliseconds, tags));
    }

    public void RecordStepLag(TimeSpan lag)
        => Record("machiverse.core.step.lag", [], () => _stepLag.Record(lag.TotalMilliseconds));

    public void RecordStepOverrun()
        => Record("machiverse.core.step.overrun", [], () => _stepOverrun.Add(1));

    public void RecordDomainCpu(StableToken domain, TimeSpan duration)
        => RecordDomainHistogram("machiverse.core.domain.cpu", _domainCpu, domain, duration.TotalMilliseconds);

    public void RecordDomainWall(StableToken domain, TimeSpan duration)
        => RecordDomainHistogram("machiverse.core.domain.wall", _domainWall, domain, duration.TotalMilliseconds);

    public void RecordDomainFailure(StableToken domain, string codeClass)
    {
        var tags = new[] { Tag("domain", domain.Value), Tag("code", NormalizeMetricValue(codeClass)) };
        Record("machiverse.core.domain.failure", tags, () => _domainFailure.Add(1, tags));
    }

    public void RecordIntentCount(StableToken domain, long count)
        => RecordDomainHistogram("machiverse.core.intent.count", _intentCount, domain, Math.Max(0, count));

    public void RecordEventCount(StableToken domain, long count)
        => RecordDomainHistogram("machiverse.core.event.count", _eventCount, domain, Math.Max(0, count));

    public void RecordTransactionCount(string kindClass, long count)
    {
        var tags = new[] { Tag("kind_class", NormalizeMetricValue(kindClass)) };
        Record("machiverse.core.transaction.count", tags, () => _transactionCount.Record(Math.Max(0, count), tags));
    }

    public void RecordConflict(StableToken domain, string mode, long count = 1)
    {
        var tags = new[] { Tag("domain", domain.Value), Tag("mode", NormalizeMetricValue(mode)) };
        Record("machiverse.core.conflict.count", tags, () => _conflictCount.Add(Math.Max(0, count), tags));
    }

    public void RecordCandidateChangedRecordCount(long count)
        => Record("machiverse.core.candidate.changed_record_count", [], () => _candidateChangedRecordCount.Record(Math.Max(0, count)));

    public void RecordAuthoritativeMemoryBytes(long bytes)
        => Record("machiverse.core.memory.authoritative", [], () => _memoryAuthoritative.Record(Math.Max(0, bytes)));

    public void RecordIndexMemoryBytes(long bytes)
        => Record("machiverse.core.memory.index", [], () => _memoryIndex.Record(Math.Max(0, bytes)));

    public void RecordCandidateMemoryBytes(long bytes)
        => Record("machiverse.core.memory.candidate", [], () => _memoryCandidate.Record(Math.Max(0, bytes)));

    public void RecordPendingOperations(string state, long count)
    {
        var tags = new[] { Tag("state", NormalizeMetricValue(state)) };
        Record("machiverse.core.operation.pending", tags, () => _operationPending.Record(Math.Max(0, count), tags));
    }

    public void RecordQueueDepth(string queue, long depth)
    {
        var tags = new[] { Tag("queue", NormalizeMetricValue(queue)) };
        Record("machiverse.core.queue.depth", tags, () => _queueDepth.Record(Math.Max(0, depth), tags));
    }

    public void RecordPersistenceCommitDuration(string commitKind, TimeSpan duration)
    {
        var tags = new[] { Tag("commit_kind", NormalizeMetricValue(commitKind)) };
        Record("machiverse.core.persistence.commit.duration", tags, () => _persistenceCommitDuration.Record(duration.TotalMilliseconds, tags));
    }

    public void RecordPersistenceHistoryWritten(string recordClass, long bytes)
    {
        var tags = new[] { Tag("record_class", NormalizeMetricValue(recordClass)) };
        Record("machiverse.core.persistence.history.written", tags, () => _persistenceHistoryWritten.Add(Math.Max(0, bytes), tags));
    }

    public void RecordSnapshotDuration(string result, TimeSpan duration)
    {
        var tags = new[] { Tag("result", NormalizeMetricValue(result)) };
        Record("machiverse.core.snapshot.duration", tags, () => _snapshotDuration.Record(duration.TotalSeconds, tags));
    }

    public void RecordSnapshotSize(string compression, long bytes)
    {
        var tags = new[] { Tag("compression", NormalizeMetricValue(compression)) };
        Record("machiverse.core.snapshot.size", tags, () => _snapshotSize.Record(Math.Max(0, bytes), tags));
    }

    public void RecordRecoveryDuration(string result, TimeSpan duration)
    {
        var tags = new[] { Tag("result", NormalizeMetricValue(result)) };
        Record("machiverse.core.recovery.duration", tags, () => _recoveryDuration.Record(duration.TotalSeconds, tags));
    }

    public void RecordPublicationWritten(string kind, long bytes)
    {
        var tags = new[] { Tag("kind", NormalizeMetricValue(kind)) };
        Record("machiverse.core.publication.written", tags, () => _publicationWritten.Add(Math.Max(0, bytes), tags));
    }

    public void RecordDetailTransitionRecordCount(string direction, StableToken domain, long count)
    {
        var tags = new[] { Tag("direction", NormalizeMetricValue(direction)), Tag("domain", domain.Value) };
        Record("machiverse.core.detail.transition_record_count", tags, () => _detailTransitionRecordCount.Record(Math.Max(0, count), tags));
    }

    public IDisposable? StartSpan(
        string spanName,
        ActivityContext? parentContext = null,
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        if (!CoreSpanRegistryV1.Contains(spanName)) return null;
        try
        {
            var activity = parentContext is { } parent
                ? _activitySource.StartActivity(spanName, ActivityKind.Internal, parent, tags)
                : _activitySource.StartActivity(spanName, ActivityKind.Internal, default(ActivityContext), tags);
            return activity is null ? null : new SafeActivityScopeV1(activity);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool TryParseW3CParentContext(
        string? traceParent,
        string? traceState,
        bool isRemote,
        out ActivityContext context)
    {
        try
        {
            return ActivityContext.TryParse(traceParent, traceState, isRemote, out context);
        }
        catch (Exception)
        {
            context = default;
            return false;
        }
    }

    public void EmitLog(
        string eventKind,
        CoreLogSeverityV1 severity,
        string messageTemplateId,
        ulong? simulationStep = null,
        StableToken? domain = null,
        string? resultCode = null,
        IEnumerable<KeyValuePair<string, string?>>? attributes = null,
        Exception? exception = null)
    {
        Safe(() =>
        {
            if (!CoreLogEventKindRegistryV1.Contains(eventKind)) return;
            if (string.IsNullOrWhiteSpace(messageTemplateId)) return;
            var now = _timeProvider.GetUtcNow();
            var unixNanos = checked((now.UtcDateTime.Ticks - DateTime.UnixEpoch.Ticks) * 100L);
            _logSink.Emit(new CoreStructuredLogEventV1(
                eventKind,
                severity,
                unixNanos,
                "simulation-core",
                messageTemplateId,
                simulationStep,
                domain,
                resultCode is null ? null : NormalizeMetricValue(resultCode),
                CoreLogRedactorV1.Sanitize(attributes),
                exception?.GetType().FullName));
        });
    }

    public static string ClassifyFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            OperationCanceledException => "cancelled",
            InvalidDataException => "invalid-data",
            OverflowException => "numeric",
            IOException => "io",
            _ => "unexpected",
        };
    }

    public void Dispose()
    {
        Safe(_activitySource.Dispose);
        Safe(_meter.Dispose);
    }

    private void RecordDomainHistogram(string metricName, Histogram<double> instrument, StableToken domain, double value)
    {
        var tags = new[] { Tag("domain", domain.Value) };
        Record(metricName, tags, () => instrument.Record(value, tags));
    }

    private void RecordDomainHistogram(string metricName, Histogram<long> instrument, StableToken domain, long value)
    {
        var tags = new[] { Tag("domain", domain.Value) };
        Record(metricName, tags, () => instrument.Record(value, tags));
    }

    private void Record(string metricName, KeyValuePair<string, object?>[] tags, Action measurement)
    {
        Safe(() =>
        {
            if (!_cardinality.TryAdmit(metricName, tags)) return;
            measurement();
        });
    }

    private static KeyValuePair<string, object?> Tag(string name, string value)
        => new(name, value);

    private static string NormalizeMetricValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        var trimmed = value.Trim().ToLowerInvariant();
        if (trimmed.Length > 64) return "other";
        if (CoreMetricCardinalityGuardV1.LooksLikeIdentifier(trimmed)) return "identifier-redacted";
        return trimmed;
    }

    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // Telemetry is explicitly non-authoritative. Listener/exporter/log failures must not
            // alter the world transition, scheduling, persistence authority, or protocol result.
        }
    }

    private sealed class SafeActivityScopeV1(Activity activity) : IDisposable
    {
        private Activity? _activity = activity;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _activity, null);
            if (current is null) return;
            Safe(current.Dispose);
        }
    }
}

internal sealed class CoreMetricCardinalityGuardV1
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _seriesByMetric =
        new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, CoreMetricDefinitionV1> _definitions =
        CoreMetricRegistryV1.Definitions.ToDictionary(static definition => definition.Name, StringComparer.Ordinal);
    private int _globalSeriesCount;

    public bool TryAdmit(string metricName, IReadOnlyList<KeyValuePair<string, object?>> tags)
    {
        if (!_definitions.TryGetValue(metricName, out var definition)) return false;
        if (!ValidateTagShape(definition, tags)) return false;

        var key = tags.Count == 0
            ? "_"
            : string.Join('|', tags.OrderBy(static tag => tag.Key, StringComparer.Ordinal)
                .Select(static tag => tag.Key + "=" + Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture)));
        var series = _seriesByMetric.GetOrAdd(
            metricName,
            static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        if (series.ContainsKey(key)) return true;
        if (series.Count >= definition.MaxExpectedSeries) return false;
        if (Volatile.Read(ref _globalSeriesCount) >= CoreMetricRegistryV1.StandardActiveSeriesTarget) return false;
        if (!series.TryAdd(key, 0)) return true;

        var global = Interlocked.Increment(ref _globalSeriesCount);
        if (global <= CoreMetricRegistryV1.StandardActiveSeriesTarget) return true;
        series.TryRemove(key, out _);
        Interlocked.Decrement(ref _globalSeriesCount);
        return false;
    }

    public static bool LooksLikeIdentifier(string value)
    {
        if (Guid.TryParse(value, out _)) return true;
        if (value.Length is 32 or 64 && value.All(static c =>
                c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            return true;
        return false;
    }

    private static bool ValidateTagShape(
        CoreMetricDefinitionV1 definition,
        IReadOnlyList<KeyValuePair<string, object?>> tags)
    {
        var allowed = definition.RequiredAttributes
            .Concat(definition.OptionalAttributes)
            .ToHashSet(StringComparer.Ordinal);
        if (tags.Any(tag => !allowed.Contains(tag.Key))) return false;
        var present = tags.Select(static tag => tag.Key).ToHashSet(StringComparer.Ordinal);
        if (definition.RequiredAttributes.Any(required => !present.Contains(required))) return false;
        if (tags.Select(static tag => tag.Key).Distinct(StringComparer.Ordinal).Count() != tags.Count) return false;
        return true;
    }
}
