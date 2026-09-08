using System.Diagnostics;
using MachiVerse.Simulation.Core.Observability;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Protocol;
using MachiVerse.Simulation.Core.Runtime;

using var telemetry = new CoreTelemetryV1(new JsonConsoleCoreStructuredLogSinkV1());
telemetry.EmitLog(
    "core.lifecycle.starting",
    CoreLogSeverityV1.Information,
    "core.lifecycle.starting.v1");

var options = AlphaSingleGatewayOptions.FromEnvironment();
AlphaSingleGatewayRuntime runtime;
var recoveryStarted = Stopwatch.GetTimestamp();
telemetry.EmitLog(
    "core.recovery.started",
    CoreLogSeverityV1.Information,
    "core.recovery.started.v1");
try
{
    runtime = await AlphaSingleGatewayRuntime.CreateAsync(options);
    telemetry.RecordRecoveryDuration("success", Stopwatch.GetElapsedTime(recoveryStarted));
    telemetry.EmitLog(
        "core.recovery.completed",
        CoreLogSeverityV1.Information,
        "core.recovery.completed.v1");
}
catch (Exception ex)
{
    telemetry.RecordRecoveryDuration("failure", Stopwatch.GetElapsedTime(recoveryStarted));
    telemetry.EmitLog(
        "core.recovery.failed",
        CoreLogSeverityV1.Error,
        "core.recovery.failed.v1",
        resultCode: CoreTelemetryV1.ClassifyFailure(ex),
        exception: ex);
    telemetry.EmitLog(
        "core.lifecycle.failed-safe",
        CoreLogSeverityV1.Critical,
        "core.lifecycle.failed-safe.v1",
        resultCode: CoreTelemetryV1.ClassifyFailure(ex),
        exception: ex);
    throw;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
builder.Services.AddSingleton(telemetry);
builder.Services.AddSingleton(runtime.Store);
builder.Services.AddSingleton(runtime.NegotiationProfile);
builder.Services.AddSingleton(runtime.EnvelopeFactory);
builder.Services.AddSingleton(runtime.IdentityResolver);
builder.Services.AddSingleton(runtime.Sessions);
builder.Services.AddSingleton(runtime.Master);
builder.Services.AddSingleton(runtime.Operations);
builder.Services.AddSingleton(runtime.Publications);
builder.Services.AddSingleton(runtime.WorldAuthority);

var app = builder.Build();
app.Lifetime.ApplicationStarted.Register(() => telemetry.EmitLog(
    "core.lifecycle.ready",
    CoreLogSeverityV1.Information,
    "core.lifecycle.ready.v1"));
app.MapGrpcService<CoreGatewayGrpcServiceV1>();
app.MapGet("/healthz", async (
    SqlitePersistenceStore store,
    CoreMasterAuthorityCoordinatorV1 master,
    CancellationToken cancellationToken) =>
{
    var head = await store.ReadCoreProtocolHeadAsync(cancellationToken);
    return Results.Ok(new
    {
        component = "simulation-core",
        status = "ready",
        worldId = head.WorldId.ToString(),
        finalizedStep = head.FinalizedStep,
        configGeneration = head.ConfigGeneration,
        masterGeneration = master.Current.MasterGeneration,
        localAlpha = true,
    });
});

await app.RunAsync();
