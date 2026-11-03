using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using OpenWigi.Service;
using OpenWigi.Service.Agents;
using OpenWigi.Service.Device;
using OpenWigi.Service.Sessions;

// Roda como serviço Windows (instalado) ou no console, para desenvolvimento:
//   dotnet run --project src/OpenWigi.Service -- --OpenWigi:AllowRemoteSessions=true
// Sem o aparelho, com um WigiDash de mentira que aparece na prévia do manager (o "Aparelho ativo" escolhido
// no manager, se houver, vale mais):
//   dotnet run --project src/OpenWigi.Service -- --OpenWigi:Device=Virtual
// Como serviço, o diretório atual é System32: a configuração vem da pasta do executável.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Ajustes da máquina (ex.: AllowRemoteSessions no PC de dev), fora do que o publish sobrescreve.
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true);
builder.Configuration.AddCommandLine(args);
builder.Services.Configure<ServiceOptions>(builder.Configuration.GetSection(ServiceOptions.Section));

builder.Services.AddWindowsService(o => o.ServiceName = "OpenWigiDash");
// A origem do Log de Eventos é criada pelo setup (precisa de admin). Sem isto o provider tentaria
// usar o nome do executável, que não existe, e os logs sumiriam em silêncio.
builder.Services.Configure<Microsoft.Extensions.Logging.EventLog.EventLogSettings>(s => s.SourceName = "OpenWigiDash");
builder.Services.AddSingleton<WtsSessionMonitor>();
builder.Services.AddSingleton<ISessionMonitor>(sp => sp.GetRequiredService<WtsSessionMonitor>());
if (WindowsServiceHelpers.IsWindowsService())
    builder.Services.AddSingleton<IHostLifetime, SessionAwareServiceLifetime>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DeviceHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceHost>());
builder.Services.AddSingleton(sp => new VirtualDisplay(sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new DeviceChoiceStore(DeviceChoiceStore.DefaultPath,
    sp.GetRequiredService<IOptions<ServiceOptions>>().Value.Device, sp.GetRequiredService<ILogger<DeviceChoiceStore>>()));
builder.Services.AddSingleton<IDisplay>(sp => new SwitchableDisplay(
    new DeviceHostDisplay(sp.GetRequiredService<DeviceHost>()), sp.GetRequiredService<VirtualDisplay>(),
    sp.GetRequiredService<DeviceChoiceStore>(), sp.GetRequiredService<ILogger<SwitchableDisplay>>()));
builder.Services.AddSingleton(sp => new ServiceScreenSettingsStore(ServiceScreenSettingsStore.DefaultPath,
    sp.GetRequiredService<ILogger<ServiceScreenSettingsStore>>()));
builder.Services.AddSingleton<SessionBroker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionBroker>());
builder.Services.AddHostedService<AgentServer>();

builder.Build().Run();
