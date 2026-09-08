using System.Text.RegularExpressions;

namespace MachiVerse.Gateway.Observability;

public enum GatewayMetricInstrumentV1
{
    Gauge = 1,
    Counter = 2,
    Histogram = 3,
}

public sealed record GatewayMetricDescriptorV1(
    string Name,
    GatewayMetricInstrumentV1 Instrument,
    string Unit,
    IReadOnlySet<string> AllowedAttributes);

public static partial class GatewayObservabilityRegistry
{
    private static readonly IReadOnlySet<string> LogKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "gateway.auth.login-failed",
        "gateway.auth.login-succeeded",
        "gateway.auth.session-revoked",
        "gateway.authorization.denied",
        "gateway.connection.closed",
        "gateway.connection.opened",
        "gateway.connection.rejected",
        "gateway.custody.changed",
        "gateway.lifecycle.ready",
        "gateway.master.changed",
        "gateway.operation.rejected",
        "gateway.publication.coalesced",
        "gateway.queue.backpressure",
        "gateway.resync.completed",
        "gateway.resync.started",
    };

    private static readonly IReadOnlyDictionary<string, GatewayMetricDescriptorV1> Metrics =
        new Dictionary<string, GatewayMetricDescriptorV1>(StringComparer.Ordinal)
        {
            ["machiverse.gateway.auth.login"] = Metric("machiverse.gateway.auth.login", GatewayMetricInstrumentV1.Counter, "{attempt}", "result_class"),
            ["machiverse.gateway.authorization.denied"] = Metric("machiverse.gateway.authorization.denied", GatewayMetricInstrumentV1.Counter, "{request}", "permission_class"),
            ["machiverse.gateway.connection.active"] = Metric("machiverse.gateway.connection.active", GatewayMetricInstrumentV1.Gauge, "{connection}", "protocol"),
            ["machiverse.gateway.custody.count"] = Metric("machiverse.gateway.custody.count", GatewayMetricInstrumentV1.Gauge, "{operation}", "state"),
            ["machiverse.gateway.master.generation_change"] = Metric("machiverse.gateway.master.generation_change", GatewayMetricInstrumentV1.Counter, "{change}"),
            ["machiverse.gateway.operation.admission"] = Metric("machiverse.gateway.operation.admission", GatewayMetricInstrumentV1.Counter, "{operation}", "result_class"),
            ["machiverse.gateway.protocol.error"] = Metric("machiverse.gateway.protocol.error", GatewayMetricInstrumentV1.Counter, "{error}", "code", "protocol"),
            ["machiverse.gateway.publication.coalesced"] = Metric("machiverse.gateway.publication.coalesced", GatewayMetricInstrumentV1.Counter, "{publication}", "client_class"),
            ["machiverse.gateway.publication.written"] = Metric("machiverse.gateway.publication.written", GatewayMetricInstrumentV1.Counter, "By", "client_class", "kind"),
            ["machiverse.gateway.queue.depth"] = Metric("machiverse.gateway.queue.depth", GatewayMetricInstrumentV1.Gauge, "{item}", "queue"),
            ["machiverse.gateway.resync.duration"] = Metric("machiverse.gateway.resync.duration", GatewayMetricInstrumentV1.Histogram, "s", "result"),
            ["machiverse.gateway.retry.count"] = Metric("machiverse.gateway.retry.count", GatewayMetricInstrumentV1.Counter, "{retry}", "reason_class"),
        };

    private static readonly IReadOnlySet<string> ForbiddenMetricAttributes = new HashSet<string>(StringComparer.Ordinal)
    {
        "account",
        "account_id",
        "batch_id",
        "correlation_id",
        "entity_id",
        "message_id",
        "operation_id",
        "resident_id",
        "url",
        "user",
        "user_id",
    };

    private static readonly string[] SecretKeyFragments =
    [
        "authorization",
        "client-secret",
        "client_secret",
        "cookie",
        "password",
        "private-key",
        "private_key",
        "refresh-token",
        "refresh_token",
        "access-token",
        "access_token",
        "id-token",
        "id_token",
    ];

    public static IReadOnlySet<string> LogEventKinds => LogKinds;
    public static IReadOnlyDictionary<string, GatewayMetricDescriptorV1> MetricDescriptors => Metrics;

    public static void RequireLogEventKind(string eventKind)
    {
        RequireStableToken(eventKind, nameof(eventKind));
        if (!LogKinds.Contains(eventKind))
            throw new InvalidDataException("observability.log-kind-unregistered");
    }

    public static GatewayMetricDescriptorV1 RequireMetric(string metricName)
    {
        RequireStableToken(metricName, nameof(metricName));
        return Metrics.TryGetValue(metricName, out var descriptor)
            ? descriptor
            : throw new InvalidDataException("observability.metric-unregistered");
    }

    public static void ValidateMetricAttributes(
        GatewayMetricDescriptorV1 descriptor,
        IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(attributes);
        foreach (var (key, value) in attributes)
        {
            RequireStableToken(key, "metric attribute");
            if (ForbiddenMetricAttributes.Contains(key))
                throw new InvalidDataException("observability.metric-unbounded-label");
            if (!descriptor.AllowedAttributes.Contains(key))
                throw new InvalidDataException("observability.metric-attribute-unregistered");
            RequireBoundedAttributeValue(value);
        }
    }

    public static IReadOnlyDictionary<string, string> RedactDiagnosticAttributes(
        IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var normalized = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in attributes)
        {
            RequireStableToken(key, "log attribute");
            normalized[key] = IsSecretKey(key) || LooksLikeCredential(value)
                ? "[redacted]"
                : value;
        }
        return normalized;
    }

    public static void RequireNoCredentialMaterial(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (LooksLikeCredential(value))
            throw new InvalidDataException("observability.secret-material-rejected");
    }

    public static T ExecuteTelemetryBestEffort<T>(Func<T> worldIndependentOperation, Action<Exception>? onTelemetryFailure = null)
    {
        ArgumentNullException.ThrowIfNull(worldIndependentOperation);
        try
        {
            return worldIndependentOperation();
        }
        catch (Exception ex)
        {
            onTelemetryFailure?.Invoke(ex);
            return default!;
        }
    }

    public static void RequireStableToken(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || !StableTokenRegex().IsMatch(value))
            throw new InvalidDataException($"{field} must be a canonical StableToken.");
    }

    private static GatewayMetricDescriptorV1 Metric(
        string name,
        GatewayMetricInstrumentV1 instrument,
        string unit,
        params string[] attributes)
        => new(name, instrument, unit, new HashSet<string>(attributes, StringComparer.Ordinal));

    private static void RequireBoundedAttributeValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || !StableTokenRegex().IsMatch(value))
            throw new InvalidDataException("observability.metric-unbounded-label");
    }

    private static bool IsSecretKey(string key)
        => SecretKeyFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeCredential(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        return PrivateKeyRegex().IsMatch(value) ||
               JwtRegex().IsMatch(value) ||
               SessionCookieRegex().IsMatch(value) ||
               AuthorizationHeaderRegex().IsMatch(value);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._/-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableTokenRegex();

    [GeneratedRegex("-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"__Host-mv_session=[^;\s]{8,}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SessionCookieRegex();

    [GeneratedRegex(@"\b(?:Bearer|Basic)\s+[A-Za-z0-9+/=_~.-]{8,}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationHeaderRegex();
}
