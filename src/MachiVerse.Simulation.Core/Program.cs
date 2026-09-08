using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Protocol;
using MachiVerse.Simulation.Core.Runtime;

var options = AlphaSingleGatewayOptions.FromEnvironment();
var runtime = await AlphaSingleGatewayRuntime.CreateAsync(options);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
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
