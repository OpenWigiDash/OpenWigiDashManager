using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenWigi.Plugins;

namespace OpenWigi.Manager.Services;

/// <summary>
/// Versão nova do OpenWigiDash (o manager, o serviço e os widgets que vêm junto), pelas releases do GitHub.
/// Com o updater ao lado do manager (instalado) e uma release com SHA256SUMS.txt, baixa o
/// OpenWigiDash-Setup.exe, confere e entrega ao OpenWigiDash.Updater.exe, que instala e reabre o manager.
/// Sem isso, só avisa e leva à página da release.
/// </summary>
public sealed class AppUpdates : IDisposable
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromDays(1);

    private readonly ManagerSettings _settings;
    private readonly ILogger _logger;
    private readonly HttpClient _http = PluginFeed.CreateHttpClient();
    private readonly AppReleaseFeed _feed;
    // O Setup.exe tem dezenas de MB: o download não cabe nos 30 s da checagem.
    private readonly HttpClient _downloadHttp = PluginFeed.CreateHttpClient();
    private readonly AppReleaseFeed _download;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly Timer _timer;
    private bool _checkedThisRun;

    public AppUpdates(ManagerSettings settings, ILoggerFactory loggerFactory)
    {
        _settings = settings;
        _logger = loggerFactory.CreateLogger<AppUpdates>();
        _http.Timeout = TimeSpan.FromSeconds(30);
        _feed = new AppReleaseFeed(_http);
        _downloadHttp.Timeout = TimeSpan.FromMinutes(15);
        _download = new AppReleaseFeed(_downloadHttp);
        // Uma vez logo depois de abrir (o aviso não fica gravado) e, depois, uma por dia.
        _timer = new Timer(_ => _ = BackgroundCheckAsync(), null, TimeSpan.FromMinutes(2), TimeSpan.FromHours(1));
    }

    /// <summary>Versão nova disponível, ou null (já na última, ainda não conferiu ou checagem desligada).</summary>
    public AppRelease? Available { get; private set; }

    /// <summary>Esta build tem versão para comparar. A de desenvolvimento (1.0.0) quase nunca vê versão nova.</summary>
    public static bool Supported => AppVersion.Current is not null;

    /// <summary>Algo mudou (versão disponível, checagem terminou).</summary>
    public event Action? Changed;

    /// <summary>Achou uma versão que o usuário ainda não viu no tray. Vem de outra thread.</summary>
    public event Action<AppRelease>? Found;

    /// <summary>Confere agora. Sem rede, a exceção sobe para a tela mostrar.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (AppVersion.Current is not { } current)
            return;
        await _busy.WaitAsync(cancellationToken);
        try
        {
            Available = await _feed.CheckAsync(current, _settings.BetaAppUpdates, cancellationToken);
            _settings.LastAppUpdateCheck = DateTimeOffset.UtcNow;
            _checkedThisRun = true;
        }
        finally
        {
            _busy.Release();
        }
        Changed?.Invoke();
        if (Available is { } release && !string.Equals(_settings.NotifiedAppUpdate, release.Tag, StringComparison.OrdinalIgnoreCase))
        {
            _settings.NotifiedAppUpdate = release.Tag;
            Found?.Invoke(release);
        }
    }

    private async Task BackgroundCheckAsync()
    {
        if (!_settings.CheckAppUpdates)
        {
            if (Available is not null)
            {
                Available = null;
                Changed?.Invoke();
            }
            return;
        }
        if (_checkedThisRun && DateTimeOffset.UtcNow - (_settings.LastAppUpdateCheck ?? DateTimeOffset.MinValue) < CheckEvery)
            return;
        try
        {
            await CheckAsync();
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Checagem de versão nova do OpenWigiDash falhou: {Message}", ex.Message);
        }
    }

    /// <summary>O updater que vem com o instalador, ao lado do manager. Na build de desenvolvimento não existe.</summary>
    public static string UpdaterPath => Path.Combine(AppContext.BaseDirectory, UpdaterFileName);

    private const string UpdaterFileName = "OpenWigiDash.Updater.exe";

    /// <summary>Dá para atualizar daqui, com um clique.</summary>
    public bool CanInstall => Available is { CanDownload: true } && File.Exists(UpdaterPath);

    /// <summary>Andamento do download do instalador (0 a 1), ou null quando não está baixando.</summary>
    public double? DownloadProgress { get; private set; }

    /// <summary>O instalador foi entregue ao updater: o app deve fechar (ele trata, na thread do WPF).</summary>
    public event Action? ExitRequested;

    /// <summary>
    /// Baixa e confere o instalador da versão disponível, abre o updater e pede para o app fechar. O updater
    /// espera o manager sair, roda o Setup.exe (o Windows pede permissão de administrador) e abre o manager de novo.
    /// </summary>
    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        if (Available is not { } release || !CanInstall)
            throw new InvalidOperationException("Não há versão nova para instalar daqui.");

        var directory = Path.Combine(Path.GetTempPath(), "OpenWigiDash", "update", release.Tag);
        string setup;
        var lastPercent = -1;
        DownloadProgress = 0;
        Changed?.Invoke();
        try
        {
            setup = await _download.DownloadSetupAsync(release, directory, new Progress<(long Done, long? Total)>(p =>
            {
                if (p.Total is not > 0)
                    return;
                var percent = (int)(p.Done * 100 / p.Total.Value);
                if (percent == lastPercent)
                    return;
                lastPercent = percent;
                DownloadProgress = percent / 100.0;
                Changed?.Invoke();
            }), cancellationToken);
        }
        finally
        {
            DownloadProgress = null;
            Changed?.Invoke();
        }

        // O MSI troca o updater que está em Program Files: roda uma cópia.
        var updater = Path.Combine(directory, UpdaterFileName);
        File.Copy(UpdaterPath, updater, overwrite: true);
        var start = new ProcessStartInfo(updater) { UseShellExecute = false };
        start.ArgumentList.Add("--setup");
        start.ArgumentList.Add(setup);
        start.ArgumentList.Add("--wait");
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--log");
        start.ArgumentList.Add(Path.Combine(directory, "setup.log"));
        if (Environment.ProcessPath is { } manager)
        {
            start.ArgumentList.Add("--manager");
            start.ArgumentList.Add(manager);
        }
        Process.Start(start)?.Dispose();
        _logger.LogInformation("Instalando {Tag} pelo updater: {Setup}", release.Tag, setup);
        ExitRequested?.Invoke();
    }

    /// <summary>Abre a página da release (notas e o OpenWigiDash-Setup.exe) no navegador.</summary>
    public void OpenReleasePage()
    {
        if (Available is { } release)
            PluginStore.OpenInBrowser(release.PageUrl);
    }

    public void Dispose()
    {
        _timer.Dispose();
        _http.Dispose();
        _downloadHttp.Dispose();
    }
}
