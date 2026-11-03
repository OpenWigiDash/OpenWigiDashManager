using System;
using System.Collections.Generic;
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
using OpenWigi.Sdk;

namespace OpenWigi.Manager;

/// <summary>
/// O manager roda no tray de cada usuário. Ele é o agente (fala com o serviço e desenha os widgets
/// quando tem a tela) e, quando pedido, abre a janela de configuração.
///   --tray      começa só no ícone (é o que o início com o Windows usa)
///   --autostart aberto pela entrada Run da máquina: sai se o usuário desligou o início com o Windows
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

        // Erros que escapam: o manager avisa e segue rodando sempre que dá. Só o que o .NET não deixa
        // segurar (erro numa thread de fundo) fecha, e mesmo assim com a janela de erro e o relatório.
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            OnUnexpectedError(args.Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            ErrorReport.Write(args.Exception, "Erro numa tarefa em segundo plano (o manager seguiu rodando).");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                ErrorReport.Show(ex, "O OpenWigiDash encontrou um erro e vai fechar.");
        };

        try
        {
            await StartAsync(e);
        }
        catch (Exception ex)
        {
            ErrorReport.Show(ex, "O OpenWigiDash não conseguiu abrir.");
            _tray?.Dispose();
            try
            {
                _single?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // O mutex não era desta instância.
            }
            Shutdown(1);
        }
    }

    private async Task StartAsync(StartupEventArgs e)
    {
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
        var settings = _services.GetRequiredService<ManagerSettings>();
        settings.AdoptMachineAutostart();
        // A entrada Run da máquina abre o manager para todo usuário; quem desligou em Ajustes sai aqui.
        if (e.Args.Contains("--autostart") && !settings.StartWithWindows)
        {
            Shutdown();
            return;
        }
        var agent = _services.GetRequiredService<AgentClient>();
        var dashboard = _services.GetRequiredService<DashboardHost>();
        agent.Attach(dashboard);
        await dashboard.StartAsync();
        agent.Start();

        _services.GetRequiredService<ManagerSettings>().PreviewDetachRequested +=
            () => Dispatcher.BeginInvoke(new Action(ShowPreviewWindow));
        _services.GetRequiredService<PluginStore>().RestartRequested +=
            () => Dispatcher.BeginInvoke(new Action(RestartApp));
        _tray = new TrayIcon(agent, a => Dispatcher.BeginInvoke(a), ShowMainWindow, ShowPreviewWindow, ExitApp);
        _services.GetRequiredService<AppUpdates>().ExitRequested += () => Dispatcher.BeginInvoke(new Action(ExitApp));
        _services.GetRequiredService<AppUpdates>().Found += release => Dispatcher.BeginInvoke(new Action(() =>
            _tray?.ShowNotice($"OpenWigiDash {release.Version} disponível", "Clique para ver as novidades em Ajustes.",
                () => ShowMainWindow("/ajustes"))));
        if (!e.Args.Contains("--tray"))
        {
            var open = Array.IndexOf(e.Args, "--open");
            ShowMainWindow(open >= 0 && open + 1 < e.Args.Length ? e.Args[open + 1] : null);
        }
        if (e.Args.Contains("--preview"))
            ShowPreviewWindow();
        if (dashboard.LayoutProblem is { } problem)
            _ = Dispatcher.BeginInvoke(new Action(() => MessageBox.Show(problem, "OpenWigiDash",
                MessageBoxButton.OK, MessageBoxImage.Warning)));
    }

    private DateTime _lastErrorNotice;

    private void OnUnexpectedError(Exception error)
    {
        // O WebView2 precisa de Direct3D para desenhar a janela. Numa sessão RDP desconectada (ou sem
        // GPU) ele falha ao abrir; isso não pode derrubar o agente, que segue desenhando no aparelho.
        if (error is System.Runtime.InteropServices.COMException && error.StackTrace?.Contains("WebView2") == true)
        {
            StartupLog.Write($"WebView2 não abriu: {error.Message} (0x{error.HResult:X8}).");
            _window?.CloseForExit();
            _window = null;
            _tray?.ShowBalloon("Não foi possível abrir a janela agora (sem vídeo nesta sessão). O painel continua rodando.");
            return;
        }

        var report = ErrorReport.Write(error, "Erro inesperado (o manager seguiu rodando).");
        // Um aviso por minuto no máximo: erro que se repete não vira uma enxurrada de balões.
        if (DateTime.UtcNow - _lastErrorNotice < TimeSpan.FromMinutes(1))
            return;
        _lastErrorNotice = DateTime.UtcNow;
        var text = $"{error.Message} O OpenWigiDash continua rodando. Clique para ver os detalhes.";
        if (_tray is not null)
            _tray.ShowNotice("Algo deu errado", text, () => OpenReport(report));
        else
            ErrorReport.Show(error, "Algo deu errado, mas o OpenWigiDash continua rodando.");
    }

    private static void OpenReport(string? report)
    {
        try
        {
            var target = report ?? ErrorReport.Directory;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Sem programa para abrir .txt: o caminho está no balão e no log.
        }
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
        // Janela de ícones para as telas de configurações dos plugins (@inject IWidgetIconPicker). Scoped, como o IDialogService.
        services.AddScoped<IWidgetIconPicker, IconPicker>();
        services.AddSingleton<ManagerSettings>();
        services.AddSingleton<AgentClient>();
        services.AddSingleton<AppUpdates>();
        var index = Array.IndexOf(args, "--plugins");
        var plugins = index >= 0 && index + 1 < args.Length
            ? args[index + 1]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenWigiDash", "Widgets");
        services.AddSingleton(sp =>
        {
            // Instalado, os widgets que vêm junto ficam ao lado da pasta do manager (Program Files\OpenWigiDash\Widgets).
            var bundled = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Widgets"));
            var options = new DashboardOptions
            {
                PluginsDirectory = plugins,
                BundledPluginsDirectory = index < 0 && Directory.Exists(bundled) ? bundled : null,
                DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenWigiDash"),
                // Widget quebrado aparece no aparelho e na prévia com o mesmo aviso do Canvas.
                ProblemTile = ProblemTileRenderer.Render,
            };
            return new DashboardHost(options, sp.GetRequiredService<AgentClient>().Sink, sp.GetRequiredService<ILoggerFactory>());
        });
        // A loja instala na mesma pasta de onde o manager carrega os plugins.
        services.AddSingleton(sp => new PluginStore(sp.GetRequiredService<ManagerSettings>(), sp.GetRequiredService<DashboardHost>(),
            plugins, sp.GetRequiredService<ILoggerFactory>()));
        return services.BuildServiceProvider();
    }

    private void ShowMainWindow() => ShowMainWindow(null);

    private void ShowMainWindow(string? route)
    {
        if (_window is null)
        {
            var created = new MainWindow(_services!);
            created.Closed += (_, _) =>
            {
                if (_window == created)
                    _window = null;
            };
            created.ReopenRequested += () => ReopenMainWindow(created);
            _window = created;
        }
        var window = _window;
        try
        {
            window.Show();
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Show() da janela falhou: {ex.Message}");
            // Um Show() que falha no meio deixa a janela quebrada: reaproveitada, o próximo Show() dá
            // "O Visual raiz de um VisualTarget não pode ter um pai". Ela sai, e a próxima abertura cria
            // outra. O agente segue rodando no tray.
            if (_window == window)
                _window = null;
            try
            {
                window.CloseForExit();
            }
            catch (Exception)
            {
                // Sem HWND não há o que fechar.
            }
            _tray?.ShowBalloon($"Não foi possível abrir a janela: {ex.Message}");
            return;
        }
        if (_window is null)
            return; // o WebView2 não conseguiu abrir (ver DispatcherUnhandledException)
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
        if (route is not null)
            window.Navigate(route);
    }

    // Troca a janela por uma nova (WebView2 novo). Só volta a abrir se ela estava aberta: escondida no
    // tray, a próxima abertura já cria a nova.
    private void ReopenMainWindow(MainWindow broken)
    {
        if (_window != broken)
            return;
        var wasVisible = broken.IsVisible;
        _window = null;
        broken.CloseForExit();
        if (wasVisible)
            ShowMainWindow();
    }

    // Mantém a pasta de plugins alternativa; troca a tela pedida pela Loja e tira o --tray e o --autostart.
    private static IEnumerable<string> RestartArguments()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToList();
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] is "--tray" or "--autostart" or "--preview")
                continue;
            if (args[i] == "--open")
            {
                i++;
                continue;
            }
            yield return args[i];
        }
        yield return "--open";
        yield return "/loja";
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

    private bool _restart;

    /// <summary>
    /// Fecha e abre de novo na Loja, para os plugins instalados/atualizados/removidos valerem. A nova cópia
    /// só sobe depois que esta solta o mutex de instância única (ver ExitApp).
    /// </summary>
    private void RestartApp()
    {
        _restart = true;
        ExitApp();
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
        if (_restart && Environment.ProcessPath is { } exe)
        {
            var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (var arg in RestartArguments())
                start.ArgumentList.Add(arg);
            System.Diagnostics.Process.Start(start);
        }
        Shutdown();
    }
}
