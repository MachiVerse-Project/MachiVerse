using MachiVerse.Administration.View;
using MachiVerse.Administration.View.Configuration;
using MachiVerse.Administration.View.Modules.Management;
using MachiVerse.Administration.View.Modules.Monitoring;
using MachiVerse.Administration.View.Protocol;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var bootstrapClient = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
var configText = await bootstrapClient.GetStringAsync("config/admin-view.toml");
var adminConfig = AdminViewConfigLoader.LoadText(configText);

builder.Services.AddSingleton(adminConfig);
builder.Services.AddSingleton<MonitoringProjectionStore>();
builder.Services.AddSingleton<IMonitoringModuleBoundary>(static services => services.GetRequiredService<MonitoringProjectionStore>());

// Phase 4 does not yet define concrete standard operational command kinds.
// Keep the production catalog empty until the ADMIN-03/Gateway cross-review registers exact descriptors.
builder.Services.AddSingleton(new OperationalCommandCatalog());
builder.Services.AddSingleton<ManagementProjectionStore>();
builder.Services.AddSingleton<IManagementModuleBoundary>(static services => services.GetRequiredService<ManagementProjectionStore>());

builder.Services.AddSingleton<AdminSessionProjectionStore>();
builder.Services.AddSingleton(static services =>
    new HighImpactConfirmationController(services.GetRequiredService<AdminViewConfig>()));
// Simulation Admin Operation kinds remain registry-owned. Do not invent high-impact world mutations in the browser.
builder.Services.AddSingleton(new AdminOperationCatalog());
builder.Services.AddSingleton<SimulationAdminOperationController>();

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<AdminGatewayProtocolClient>();
builder.Services.AddScoped<AdminGatewaySession>();

await builder.Build().RunAsync();
