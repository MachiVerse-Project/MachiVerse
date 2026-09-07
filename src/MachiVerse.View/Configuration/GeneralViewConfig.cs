namespace MachiVerse.View.Configuration;

public sealed record GeneralViewConfig(
    int TargetFps,
    double MaxPixelRatio,
    bool PredictionEnabled,
    int PredictionMaxHorizonMs,
    int ReconcileSoftDurationMs,
    int ReconcileMaxSoftDurationMs,
    int ReconnectInitialMs,
    int ReconnectMaxMs);
