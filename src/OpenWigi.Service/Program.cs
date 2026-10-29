using Microsoft.Extensions.Hosting.WindowsServices;
using OpenWigi.Service;
using OpenWigi.Service.Agents;
using OpenWigi.Service.Device;
using OpenWigi.Service.Sessions;

// Roda como serviço Windows (instalado) ou no console, para desenvolvimento:
//   dotnet run --project src/OpenWigi.Service -- --OpenWigi:AllowRemoteSessions=true
var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<ServiceOptions>(builder.Configuration.GetSection(ServiceOptions.Section));

builder.Services.AddWindowsService(o => o.ServiceName = "OpenWigiDash");
builder.Services.AddSingleton<WtsSessionMonitor>();
builder.Services.AddSingleton<ISessionMonitor>(sp => sp.GetRequiredService<WtsSessionMonitor>());
if (WindowsServiceHelpers.IsWindowsService())
    builder.Services.AddSingleton<IHostLifetime, SessionAwareServiceLifetime>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DeviceHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceHost>());
builder.Services.AddSingleton<IDisplay>(sp => new DeviceHostDisplay(sp.GetRequiredService<DeviceHost>()));
builder.Services.AddSingleton<SessionBroker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionBroker>());
builder.Services.AddHostedService<AgentServer>();

builder.Build().Run();
