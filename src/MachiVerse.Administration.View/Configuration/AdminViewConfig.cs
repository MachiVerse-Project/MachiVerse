namespace MachiVerse.Administration.View.Configuration;

public sealed record AdminViewConfig(
    int DashboardRefreshMs,
    int MetricsLocalHistorySamples,
    int MetricsMaxSeries,
    int LogDefaultPageSize,
    int AuditDefaultPageSize,
    int PresentationTimeoutMs,
    int ConfirmationUxTimeoutSeconds,
    int ReconnectInitialMs,
    int ReconnectMaxMs);
