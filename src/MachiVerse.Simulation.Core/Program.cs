using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Protocol;
using MachiVerse.Simulation.Core.Runtime;

var options = AlphaSingleGatewayOptions.FromEnvironment();
var runtime = await AlphaSingleGatewayRuntime.CreateAsync(options);
var multiGatewayAlpha = string.Equals(
    Environment.GetEnvironmentVariable("MACHIVERSE_ALPHA_MULTI_GATEWAY"),
    "1",
    StringComparison.Ordinal);

IAuthenticatedGatewayIdentityResolverV1 identityResolver = runtime.IdentityResolver;
if (multiGatewayAlpha)
{
    // AlphaSingleGatewayRuntime seeds the INT-01 singleton identity so old local flows remain unchanged.
    // INT-02 explicitly removes that synthetic session and switches to per-connection loopback metadata.
    runtime.Sessions.Disconnect(options.GatewayLogicalId, options.GatewayComponentInstanceId);
    await runtime.Master.ReconcileAsync(new StableToken("master.alpha-multi-gateway-startup"));
    identityResolver = new AlphaLocalMetadataGatewayIdentityResolverV1();
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
builder.Services.AddSingleton(runtime.Store);
builder.Services.AddSingleton(runtime.NegotiationProfile);
builder.Services.AddSingleton(runtime.EnvelopeFactory);
builder.Services.AddSingleton(identityResolver);
builder.Services.AddSingleton(runtime.Sessions);
builder.Services.AddSingleton(runtime.Master);
builder.Services.AddSingleton(runtime.Operations);
builder.Services.AddSingleton(runtime.Publications);
builder.Services.AddSingleton(runtime.WorldAuthority);

var app = builder.Build();
app.MapGrpcService<CoreGatewayGrpcServiceV1>();
app.MapGet("/healthz", async (
    SqlitePersistenceStore store,
    CoreGatewaySessionRegistryV1 sessions,
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
        currentMasterGatewayId = master.Current.CurrentMasterGatewayId?.ToString(),
        registeredGatewayCount = sessions.Snapshot().Count,
        localAlpha = true,
        multiGatewayAlpha,
    });
});

await app.RunAsync();
