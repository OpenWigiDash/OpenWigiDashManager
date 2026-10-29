using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using OpenWigi.Host;
using OpenWigi.Manager.Services;
using OpenWigi.Manager.Tray;

namespace OpenWigi.Manager;

/// <summary>
/// O manager roda no tray de cada usuário. Ele é o agente (fala com o serviço e desenha os widgets
/// quando tem a tela) e, quando pedido, abre a janela de configuração.
///   --tray      começa só no ícone (é o que o início com o Windows usa)
///   --plugins   pasta de plugins alternativa
///   --open      abre a janela numa tela (ex.: /widgets)
///   --preview   abre a prévia destacada
/// </summary>
public partial class App : System.Windows.Application
{
    private Mutex? _single;
    private EventWaitHandle? _showSignal;
    private ServiceProvider? _services;
    private TrayIcon? _tray;
    private MainWindow? _window;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Uma instância por sessão do Windows: os outros usuários têm a sua (o nome é "Local\").
        _single = new Mutex(true, @"Local\OpenWigiDashManager", out var first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\OpenWigiDashManager.Show");
        if (!first)
        {
            _showSignal.Set(); // a instância que já roda abre a janela
            Shutdown();
            return;
        }
        _ = Task.Run(WaitForShowSignal);

        _services = BuildServices(e.Args);
        var agent = _services.GetRequiredService<AgentClient>();
        var dashboard = _services.GetRequiredService<DashboardHost>();
        agent.Attach(dashboard);
        await dashboard.StartAsync();
        agent.Start();

        _services.GetRequiredService<ManagerSettings>().PreviewDetachRequested +=
            () => Dispatcher.BeginInvoke(new Action(ShowPreviewWindow));
        _tray = new TrayIcon(agent, a => Dispatcher.BeginInvoke(a), ShowMainWindow, ShowPreviewWindow, ExitApp);
        if (!e.Args.Contains("--tray"))
        {
            var open = Array.IndexOf(e.Args, "--open");
            ShowMainWindow(open >= 0 && open + 1 < e.Args.Length ? e.Args[open + 1] : null);
        }
        if (e.Args.Contains("--preview"))
            ShowPreviewWindow();
    }

    private ServiceProvider BuildServices(string[] args)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Information));
        services.AddWpfBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        services.AddMudServices();
        services.AddSingleton<ManagerSettings>();
        services.AddSingleton<AgentClient>();
        services.AddSingleton(sp =>
        {
            var index = Array.IndexOf(args, "--plugins");
            var plugins = index >= 0 && index + 1 < args.Length
                ? args[index + 1]
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenWigiDash", "Widgets");
            var options = new DashboardOptions
            {
                PluginsDirectory = plugins,
                DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenWigiDash"),
            };
            return new DashboardHost(options, sp.GetRequiredService<AgentClient>().Sink, sp.GetRequiredService<ILoggerFactory>());
        });
        return services.BuildServiceProvider();
    }

    private void ShowMainWindow() => ShowMainWindow(null);

    private void ShowMainWindow(string? route)
    {
        if (_window is null)
        {
            _window = new MainWindow(_services!);
            _window.Closed += (_, _) => _window = null;
        }
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
        if (route is not null)
            _window.Navigate(route);
    }

    private PreviewWindow? _preview;

    private void ShowPreviewWindow()
    {
        if (_preview is null)
        {
            _preview = new PreviewWindow(_services!.GetRequiredService<AgentClient>());
            _preview.Closed += (_, _) => _preview = null;
            _preview.Show();
        }
        if (_preview.WindowState == WindowState.Minimized)
            _preview.WindowState = WindowState.Normal;
        _preview.Activate();
    }

    private void WaitForShowSignal()
    {
        while (_showSignal!.WaitOne())
            Dispatcher.BeginInvoke(new Action(ShowMainWindow));
    }

    private async void ExitApp()
    {
        _tray?.Dispose();
        _preview?.Close();
        _window?.CloseForExit();
        if (_services is not null)
        {
            await _services.GetRequiredService<AgentClient>().DisposeAsync();
            await _services.GetRequiredService<DashboardHost>().DisposeAsync();
            await _services.DisposeAsync();
        }
        _single?.ReleaseMutex();
        Shutdown();
    }
}
