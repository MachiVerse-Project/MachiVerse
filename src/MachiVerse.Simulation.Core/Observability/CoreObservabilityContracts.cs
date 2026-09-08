namespace MachiVerse.Simulation.Core.Observability;

public enum CoreMetricInstrumentKindV1
{
    Histogram = 0,
    Gauge = 1,
    Counter = 2,
}

public sealed record CoreMetricDefinitionV1(
    string Name,
    CoreMetricInstrumentKindV1 Instrument,
    string Unit,
    IReadOnlyList<string> RequiredAttributes,
    IReadOnlyList<string> OptionalAttributes,
    int MaxExpectedSeries);

public static class CoreMetricRegistryV1
{
    public const int StandardActiveSeriesTarget = 5_000;
    public const int HardWarningSeriesThreshold = 10_000;

    private static readonly string[] ForbiddenAttributeNames =
    [
        "entity_id",
        "resident_id",
        "operation_id",
        "message_id",
        "correlation_id",
        "batch_id",
        "account_id",
        "user_id",
        "url",
        "path",
        "query",
        "raw_result_code",
    ];

    private static readonly CoreMetricDefinitionV1[] CanonicalDefinitions =
    [
        Definition("machiverse.core.step.duration", CoreMetricInstrumentKindV1.Histogram, "ms", [], ["phase"], 8),
        Definition("machiverse.core.step.lag", CoreMetricInstrumentKindV1.Gauge, "ms", [], [], 1),
        Definition("machiverse.core.step.overrun", CoreMetricInstrumentKindV1.Counter, "{overrun}", [], [], 1),
        Definition("machiverse.core.domain.cpu", CoreMetricInstrumentKindV1.Histogram, "ms", ["domain"], [], 8),
        Definition("machiverse.core.domain.wall", CoreMetricInstrumentKindV1.Histogram, "ms", ["domain"], [], 8),
        Definition("machiverse.core.domain.failure", CoreMetricInstrumentKindV1.Counter, "{failure}", ["domain", "code"], [], 256),
        Definition("machiverse.core.intent.count", CoreMetricInstrumentKindV1.Histogram, "{intent}", ["domain"], [], 8),
        Definition("machiverse.core.event.count", CoreMetricInstrumentKindV1.Histogram, "{event}", ["domain"], [], 8),
        Definition("machiverse.core.transaction.count", CoreMetricInstrumentKindV1.Histogram, "{transaction}", ["kind_class"], [], 32),
        Definition("machiverse.core.conflict.count", CoreMetricInstrumentKindV1.Counter, "{conflict}", ["domain", "mode"], [], 64),
        Definition("machiverse.core.candidate.changed_record_count", CoreMetricInstrumentKindV1.Histogram, "{record}", [], [], 1),
        Definition("machiverse.core.memory.authoritative", CoreMetricInstrumentKindV1.Gauge, "By", [], [], 1),
        Definition("machiverse.core.memory.index", CoreMetricInstrumentKindV1.Gauge, "By", [], [], 1),
        Definition("machiverse.core.memory.candidate", CoreMetricInstrumentKindV1.Gauge, "By", [], [], 1),
        Definition("machiverse.core.operation.pending", CoreMetricInstrumentKindV1.Gauge, "{operation}", ["state"], [], 8),
        Definition("machiverse.core.queue.depth", CoreMetricInstrumentKindV1.Gauge, "{item}", ["queue"], [], 16),
        Definition("machiverse.core.persistence.commit.duration", CoreMetricInstrumentKindV1.Histogram, "ms", ["commit_kind"], [], 16),
        Definition("machiverse.core.persistence.history.written", CoreMetricInstrumentKindV1.Counter, "By", ["record_class"], [], 16),
        Definition("machiverse.core.snapshot.duration", CoreMetricInstrumentKindV1.Histogram, "s", ["result"], [], 8),
        Definition("machiverse.core.snapshot.size", CoreMetricInstrumentKindV1.Histogram, "By", ["compression"], [], 4),
        Definition("machiverse.core.recovery.duration", CoreMetricInstrumentKindV1.Histogram, "s", ["result"], [], 8),
        Definition("machiverse.core.publication.written", CoreMetricInstrumentKindV1.Counter, "By", ["kind"], [], 16),
        Definition("machiverse.core.detail.transition_record_count", CoreMetricInstrumentKindV1.Histogram, "{record}", ["direction", "domain"], [], 16),
    ];

    public static IReadOnlyList<CoreMetricDefinitionV1> Definitions => CanonicalDefinitions;
    public static IReadOnlyCollection<string> ForbiddenMetricAttributeNames => ForbiddenAttributeNames;
    public static int EstimatedMaximumActiveSeries => CanonicalDefinitions.Sum(static definition => definition.MaxExpectedSeries);

    public static void ValidateContract()
    {
        if (CanonicalDefinitions.Select(static definition => definition.Name).Distinct(StringComparer.Ordinal).Count() != CanonicalDefinitions.Length)
            throw new InvalidOperationException("observability.metric.duplicate-name");
        if (CanonicalDefinitions.Any(static definition => !definition.Name.StartsWith("machiverse.core.", StringComparison.Ordinal)))
            throw new InvalidOperationException("observability.metric.invalid-prefix");
        if (CanonicalDefinitions.Any(static definition => definition.MaxExpectedSeries <= 0))
            throw new InvalidOperationException("observability.metric.invalid-series-budget");
        if (EstimatedMaximumActiveSeries > StandardActiveSeriesTarget)
            throw new InvalidOperationException("observability.metric.cardinality-target-exceeded");

        var forbidden = ForbiddenAttributeNames.ToHashSet(StringComparer.Ordinal);
        foreach (var definition in CanonicalDefinitions)
        {
            var attributes = definition.RequiredAttributes.Concat(definition.OptionalAttributes).ToArray();
            if (attributes.Distinct(StringComparer.Ordinal).Count() != attributes.Length)
                throw new InvalidOperationException($"observability.metric.duplicate-attribute:{definition.Name}");
            if (attributes.Any(forbidden.Contains))
                throw new InvalidOperationException($"observability.metric.high-cardinality-attribute:{definition.Name}");
        }
    }

    private static CoreMetricDefinitionV1 Definition(
        string name,
        CoreMetricInstrumentKindV1 instrument,
        string unit,
        string[] requiredAttributes,
        string[] optionalAttributes,
        int maxExpectedSeries)
        => new(
            name,
            instrument,
            unit,
            Array.AsReadOnly(requiredAttributes),
            Array.AsReadOnly(optionalAttributes),
            maxExpectedSeries);
}

public static class CoreLogEventKindRegistryV1
{
    private static readonly string[] CanonicalKinds =
    [
        "core.lifecycle.starting",
        "core.lifecycle.ready",
        "core.lifecycle.failed-safe",
        "core.step.started",
        "core.step.overrun",
        "core.step.aborted",
        "core.step.committed",
        "core.domain.failed",
        "core.invariant.failed",
        "core.numeric.failed",
        "core.operation.accepted",
        "core.operation.scheduled",
        "core.operation.terminal",
        "core.operation.duplicate",
        "core.operation.payload-mismatch",
        "core.detail.promotion-deferred",
        "core.persistence.commit-failed",
        "core.snapshot.started",
        "core.snapshot.committed",
        "core.snapshot.failed",
        "core.recovery.started",
        "core.recovery.completed",
        "core.recovery.failed",
        "core.migration.started",
        "core.migration.completed",
        "core.migration.failed",
    ];

    private static readonly HashSet<string> CanonicalKindSet = CanonicalKinds.ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<string> Kinds => CanonicalKinds;

    public static bool Contains(string eventKind)
        => CanonicalKindSet.Contains(eventKind);
}

public static class CoreSpanRegistryV1
{
    public const string OperationAccept = "core.operation.accept";
    public const string OperationSchedule = "core.operation.schedule";
    public const string StepTransition = "core.step.transition";
    public const string DomainCalculate = "core.domain.calculate";
    public const string Merge = "core.merge";
    public const string PersistenceCommit = "core.persistence.commit";
    public const string SnapshotWrite = "core.snapshot.write";
    public const string RecoveryReplay = "core.recovery.replay";

    private static readonly string[] CanonicalNames =
    [
        OperationAccept,
        OperationSchedule,
        StepTransition,
        DomainCalculate,
        Merge,
        PersistenceCommit,
        SnapshotWrite,
        RecoveryReplay,
    ];

    private static readonly HashSet<string> CanonicalNameSet = CanonicalNames.ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<string> Names => CanonicalNames;

    public static bool Contains(string spanName)
        => CanonicalNameSet.Contains(spanName);
}
