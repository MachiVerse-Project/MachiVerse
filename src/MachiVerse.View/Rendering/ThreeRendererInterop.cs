using Microsoft.JSInterop;

namespace MachiVerse.View.Rendering;

public sealed record RendererStatus(
    bool Initialized,
    string BackendMode,
    string ThreeRevision,
    string? BasisStep,
    int ProjectionRecordCount,
    int SceneObjectCount,
    string RendererState);

public sealed class ThreeRendererInterop(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private string? _canvasId;
    private double _maxPixelRatio;

    public async ValueTask<RendererStatus> InitializeHostAsync(
        string canvasId,
        double maxPixelRatio,
        bool forceWebGl = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canvasId);
        if (!double.IsFinite(maxPixelRatio) || maxPixelRatio <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPixelRatio));

        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", cancellationToken, "./js/three-renderer.js");
        _canvasId = canvasId;
        _maxPixelRatio = maxPixelRatio;
        return await _module.InvokeAsync<RendererStatus>(
            "initializeRendererBoundary",
            cancellationToken,
            canvasId,
            maxPixelRatio,
            forceWebGl);
    }

    public async ValueTask ApplySceneProjectionAsync(
        SceneProjectionModel projection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var module = _module ?? throw new InvalidOperationException("Renderer has not been initialized.");
        await module.InvokeVoidAsync("applySceneProjection", cancellationToken, projection);
    }

    public async ValueTask ClearSceneProjectionAsync(CancellationToken cancellationToken = default)
    {
        var module = _module ?? throw new InvalidOperationException("Renderer has not been initialized.");
        await module.InvokeVoidAsync("clearSceneProjection", cancellationToken);
    }

    public async ValueTask<RendererStatus> ReinitializeAsync(
        bool forceWebGl = false,
        CancellationToken cancellationToken = default)
    {
        var module = _module ?? throw new InvalidOperationException("Renderer has not been initialized.");
        if (_canvasId is null) throw new InvalidOperationException("Renderer canvas has not been initialized.");
        return await module.InvokeAsync<RendererStatus>(
            "reinitializeRendererBoundary",
            cancellationToken,
            _canvasId,
            _maxPixelRatio,
            forceWebGl);
    }

    public async ValueTask<RendererStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var module = _module ?? throw new InvalidOperationException("Renderer has not been initialized.");
        return await module.InvokeAsync<RendererStatus>("getRendererStatus", cancellationToken);
    }

    internal async ValueTask<RendererStatus> SimulateLossForTestAsync(
        string reason = "test-loss",
        CancellationToken cancellationToken = default)
    {
        var module = _module ?? throw new InvalidOperationException("Renderer has not been initialized.");
        return await module.InvokeAsync<RendererStatus>("simulateRendererLossForTest", cancellationToken, reason);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;
        try
        {
            await _module.InvokeVoidAsync("disposeRendererBoundary");
        }
        finally
        {
            await _module.DisposeAsync();
            _module = null;
        }
    }
}
