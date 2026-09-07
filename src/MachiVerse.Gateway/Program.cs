using MachiVerse.Gateway.Configuration;

var builder = WebApplication.CreateBuilder(args);
var configPath = Environment.GetEnvironmentVariable("MACHIVERSE_GATEWAY_CONFIG") ?? "config/gateway.toml";
var gatewayConfig = GatewayConfigLoader.LoadFile(configPath);

builder.Services.AddSingleton(gatewayConfig);
builder.Services.AddGrpc();

var app = builder.Build();
app.UseWebSockets();
app.MapGet("/healthz", () => Results.Ok(new { component = "gateway", status = "starting-foundation" }));
app.Run();
