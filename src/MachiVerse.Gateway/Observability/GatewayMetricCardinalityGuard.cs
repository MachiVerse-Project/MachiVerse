namespace MachiVerse.Gateway.Observability;

public enum GatewayMetricSeriesDecisionV1
{
    Existing = 1,
    Accepted = 2,
    TargetExceeded = 3,
    HardLimitRejected = 4,
}

public readonly record struct GatewayMetricSeriesRegistrationV1(
    GatewayMetricSeriesDecisionV1 Decision,
    int ActiveSeriesCount)
{
    public bool WasRegistered => Decision != GatewayMetricSeriesDecisionV1.HardLimitRejected;
}

/// <summary>
/// Operational-only guard for metric time-series cardinality. Its decisions must never be
/// fed back into world scheduling, ordering, random input, custody, or authorization semantics.
/// </summary>
public sealed class GatewayMetricCardinalityGuardV1
{
    public const int TargetActiveSeries = 5_000;
    public const int HardActiveSeriesLimit = 10_000;

    private readonly object _gate = new();
    private readonly HashSet<string> _activeSeries = new(StringComparer.Ordinal);

    public int ActiveSeriesCount
    {
        get
        {
            lock (_gate) return _activeSeries.Count;
        }
    }

    public GatewayMetricSeriesRegistrationV1 Register(
        string metricName,
        IReadOnlyDictionary<string, string> attributes)
    {
        var descriptor = GatewayObservabilityRegistry.RequireMetric(metricName);
        GatewayObservabilityRegistry.ValidateMetricAttributes(descriptor, attributes);
        var seriesKey = BuildSeriesKey(metricName, attributes);

        lock (_gate)
        {
            if (_activeSeries.Contains(seriesKey))
                return new GatewayMetricSeriesRegistrationV1(
                    GatewayMetricSeriesDecisionV1.Existing,
                    _activeSeries.Count);

            if (_activeSeries.Count >= HardActiveSeriesLimit)
                return new GatewayMetricSeriesRegistrationV1(
                    GatewayMetricSeriesDecisionV1.HardLimitRejected,
                    _activeSeries.Count);

            _activeSeries.Add(seriesKey);
            var decision = _activeSeries.Count > TargetActiveSeries
                ? GatewayMetricSeriesDecisionV1.TargetExceeded
                : GatewayMetricSeriesDecisionV1.Accepted;
            return new GatewayMetricSeriesRegistrationV1(decision, _activeSeries.Count);
        }
    }

    public void ClearOperationalSeries()
    {
        lock (_gate) _activeSeries.Clear();
    }

    private static string BuildSeriesKey(
        string metricName,
        IReadOnlyDictionary<string, string> attributes)
    {
        var builder = new System.Text.StringBuilder(metricName.Length + attributes.Count * 32);
        builder.Append(metricName);
        foreach (var pair in attributes.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append('\0');
            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(pair.Value);
        }
        return builder.ToString();
    }
}
