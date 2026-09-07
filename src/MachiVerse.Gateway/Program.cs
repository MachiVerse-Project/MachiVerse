using MachiVerse.Gateway.Audit;
using MachiVerse.Gateway.Configuration;
using MachiVerse.Gateway.Protocol;
using MachiVerse.Gateway.State;

var builder = WebApplication.CreateBuilder(args);
var configPath = Environment.GetEnvironmentVariable("MACHIVERSE_GATEWAY_CONFIG")
    ?? Path.Combine(AppContext.BaseDirectory, "config", "gateway.toml");
var gatewayConfig = GatewayConfigLoader.LoadFile(configPath);
var alphaCoreOptions = AlphaCoreLinkOptions.TryFromEnvironment();
var alphaCoreState = new AlphaCoreLinkState();

builder.Services.AddSingleton(gatewayConfig);
builder.Services.AddSingleton(alphaCoreState);
builder.Services.AddGrpc();

if (alphaCoreOptions is not null)
{
    var componentDataDirectory = Environment.GetEnvironmentVariable("MACHIVERSE_GATEWAY_DATA_ROOT")
        ?? Path.Combine(AppContext.BaseDirectory, "data", "gateway-alpha");

    builder.Services.AddSingleton(alphaCoreOptions);
    builder.Services.AddSingleton(new AlphaGatewayConfigCoordinator(gatewayConfig, componentDataDirectory));
    builder.Services.AddSingleton(_ => new GatewayAuditStoreV1(componentDataDirectory, gatewayConfig.Audit.QueryMaxPageSize));
    builder.Services.AddSingleton<IAuditWriterV1>(sp => sp.GetRequiredService<GatewayAuditStoreV1>());
    builder.Services.AddSingleton<ProtectedAdminAuditGateV1>();
    builder.Services.AddSingleton<ProtocolNegotiationState>();
    builder.Services.AddSingleton<SchedulingPolicyProjection>();
    builder.Services.AddSingleton<ConfirmedProjectionCache>();
    builder.Services.AddSingleton<ResyncCoordinator>();
    builder.Services.AddSingleton(new MasterAuthorityTracker(alphaCoreOptions.GatewayLogicalId));
    builder.Services.AddSingleton<AlphaViewBridge>();
    builder.Services.AddSingleton<AlphaAdminBridge>();
    builder.Services.AddHostedService<AlphaCoreConnectionWorker>();
}

var app = builder.Build();
app.UseWebSockets();
app.MapGet("/healthz", () =>
{
    var core = alphaCoreState.Current;
    return Results.Ok(new
    {
        component = "gateway",
        status = core.Enabled ? core.Status : "standalone-foundation",
        core,
    });
});

if (alphaCoreOptions is not null)
{
    app.Map("/ws/v1/view", async context =>
    {
        var bridge = context.RequestServices.GetRequiredService<AlphaViewBridge>();
        await bridge.HandleAsync(context);
    });

    app.Map("/ws/v1/admin", async context =>
    {
        var bridge = context.RequestServices.GetRequiredService<AlphaAdminBridge>();
        await bridge.HandleAsync(context);
    });
}

await app.RunAsync();
