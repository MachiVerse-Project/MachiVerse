using System.Diagnostics;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;

namespace MachiVerse.Simulation.Core.Observability;

/// <summary>
/// Adds non-authoritative telemetry around an existing domain runtime without changing the
/// deterministic domain contract or candidate output.
/// </summary>
public sealed class ObservedDomainRuntimeV1(
    IDomainRuntimeV1 inner,
    CoreTelemetryV1 telemetry) : IDomainRuntimeV1
{
    private readonly IDomainRuntimeV1 _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly CoreTelemetryV1 _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));

    public StableToken DomainToken => _inner.DomainToken;

    public async ValueTask<DomainCandidateOutputV1> ExecuteAsync(
        DomainRuntimeContextV1 context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tags = new[]
        {
            new KeyValuePair<string, object?>("domain", DomainToken.Value),
        };
        using var span = _telemetry.StartSpan(CoreSpanRegistryV1.DomainCalculate, tags: tags);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var output = await _inner.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            _telemetry.RecordIntentCount(DomainToken, output.Intents.Count);
            return output;
        }
        catch (Exception ex)
        {
            var code = CoreTelemetryV1.ClassifyFailure(ex);
            _telemetry.RecordDomainFailure(DomainToken, code);
            _telemetry.EmitLog(
                "core.domain.failed",
                CoreLogSeverityV1.Error,
                "core.domain.failed.v1",
                context.FrozenInput.BasisStep,
                DomainToken,
                code,
                exception: ex);
            throw;
        }
        finally
        {
            _telemetry.RecordDomainWall(DomainToken, Stopwatch.GetElapsedTime(started));
        }
    }
}

/// <summary>
/// Decorates the SIM-06 durability seam. Telemetry runs outside the authority decision and any
/// telemetry/exporter exception is contained by CoreTelemetryV1.
/// </summary>
public sealed class ObservedStepTransitionDurabilityV1(
    IStepTransitionDurabilityV1 inner,
    CoreTelemetryV1 telemetry) : IStepTransitionDurabilityV1
{
    private readonly IStepTransitionDurabilityV1 _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly CoreTelemetryV1 _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));

    public async Task<DurableTransitionResult> CommitAsync(
        StepCandidateV1 candidate,
        StepFinalizeMaterialV1 material,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(material);
        using var span = _telemetry.StartSpan(CoreSpanRegistryV1.PersistenceCommit);
        var started = Stopwatch.GetTimestamp();
        try
        {
            return await _inner.CommitAsync(candidate, material, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _telemetry.EmitLog(
                "core.persistence.commit-failed",
                CoreLogSeverityV1.Error,
                "core.persistence.transition-commit-failed.v1",
                candidate.BasisStep,
                resultCode: CoreTelemetryV1.ClassifyFailure(ex),
                exception: ex);
            throw;
        }
        finally
        {
            _telemetry.RecordPersistenceCommitDuration("transition", Stopwatch.GetElapsedTime(started));
        }
    }
}

public static class ObservedSnapshotCommitCoordinatorV1
{
    public static async Task<DurableSnapshotCommitResult> CommitAsync(
        CoreTelemetryV1 telemetry,
        SqlitePersistenceStore store,
        WorldPersistencePaths world,
        SnapshotPhysicalPaths physical,
        SnapshotCommitMaterial snapshot,
        HistoryRecordMaterial history,
        Func<SnapshotPhysicalPaths, CancellationToken, Task> validateStaging,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        using var span = telemetry.StartSpan(CoreSpanRegistryV1.SnapshotWrite);
        telemetry.EmitLog(
            "core.snapshot.started",
            CoreLogSeverityV1.Information,
            "core.snapshot.started.v1",
            snapshot.SnapshotStep);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var durable = await SnapshotCommitCoordinator.CommitAsync(
                store,
                world,
                physical,
                snapshot,
                history,
                validateStaging,
                cancellationToken).ConfigureAwait(false);
            telemetry.RecordSnapshotDuration("success", Stopwatch.GetElapsedTime(started));
            telemetry.EmitLog(
                "core.snapshot.committed",
                CoreLogSeverityV1.Information,
                "core.snapshot.committed.v1",
                snapshot.SnapshotStep);
            return durable;
        }
        catch (Exception ex)
        {
            telemetry.RecordSnapshotDuration("failure", Stopwatch.GetElapsedTime(started));
            telemetry.EmitLog(
                "core.snapshot.failed",
                CoreLogSeverityV1.Error,
                "core.snapshot.failed.v1",
                snapshot.SnapshotStep,
                resultCode: CoreTelemetryV1.ClassifyFailure(ex),
                exception: ex);
            throw;
        }
    }
}

/// <summary>
/// Explicit Step-level scope for hosts/coordinators that own the full transition boundary.
/// Ending telemetry never authorizes, aborts, or mutates a Step; it only observes the result.
/// </summary>
public sealed class CoreStepTelemetryScopeV1 : IDisposable
{
    private readonly CoreTelemetryV1 _telemetry;
    private readonly ulong _basisStep;
    private readonly long _started;
    private readonly IDisposable? _span;
    private int _completed;

    public CoreStepTelemetryScopeV1(CoreTelemetryV1 telemetry, ulong basisStep)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _basisStep = basisStep;
        _span = telemetry.StartSpan(CoreSpanRegistryV1.StepTransition);
        _started = Stopwatch.GetTimestamp();
        telemetry.EmitLog(
            "core.step.started",
            CoreLogSeverityV1.Debug,
            "core.step.started.v1",
            basisStep);
    }

    public void Commit()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;
        _telemetry.RecordStepDuration(Stopwatch.GetElapsedTime(_started), "total");
        _telemetry.EmitLog(
            "core.step.committed",
            CoreLogSeverityV1.Information,
            "core.step.committed.v1",
            _basisStep);
        _span?.Dispose();
    }

    public void Abort(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;
        _telemetry.RecordStepDuration(Stopwatch.GetElapsedTime(_started), "total");
        _telemetry.EmitLog(
            "core.step.aborted",
            CoreLogSeverityV1.Warning,
            "core.step.aborted.v1",
            _basisStep,
            resultCode: CoreTelemetryV1.ClassifyFailure(exception),
            exception: exception);
        _span?.Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
            _span?.Dispose();
    }
}
