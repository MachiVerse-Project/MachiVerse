using MachiVerse.View;
using MachiVerse.View.Configuration;
using MachiVerse.View.Operations;
using MachiVerse.View.Protocol;
using MachiVerse.View.Rendering;
using MachiVerse.View.State;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var bootstrapClient = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
var configText = await bootstrapClient.GetStringAsync("config/general-view.toml");
var viewConfig = GeneralViewConfigLoader.LoadText(configText);

builder.Services.AddSingleton(viewConfig);
builder.Services.AddSingleton(SceneProjectionAdapterRegistry.Empty);
builder.Services.AddSingleton(ViewOperationCatalog.Empty);
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<GatewayProtocolClient>();
builder.Services.AddScoped<ConfirmedWorldStore>();
builder.Services.AddScoped<PublicationConsumer>();
builder.Services.AddScoped<PredictionStore>();
builder.Services.AddScoped<ReconciliationCoordinator>();
builder.Services.AddScoped<ViewOperationController>();
builder.Services.AddScoped<ThreeRendererInterop>();

await builder.Build().RunAsync();
