using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenWigi.Plugins;

namespace OpenWigi.Manager.Services;

/// <summary>
/// Versão nova do OpenWigiDash (o manager, o serviço e os widgets que vêm junto), pelas releases do GitHub.
/// Por enquanto só avisa: o usuário baixa o OpenWigiDash-Setup.exe pela página da release.
/// </summary>
public sealed class AppUpdates : IDisposable
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromDays(1);

    private readonly ManagerSettings _settings;
    private readonly ILogger _logger;
    private readonly HttpClient _http = PluginFeed.CreateHttpClient();
    private readonly AppReleaseFeed _feed;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly Timer _timer;
    private bool _checkedThisRun;

    public AppUpdates(ManagerSettings settings, ILoggerFactory loggerFactory)
    {
        _settings = settings;
        _logger = loggerFactory.CreateLogger<AppUpdates>();
        _http.Timeout = TimeSpan.FromSeconds(30);
        _feed = new AppReleaseFeed(_http);
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
    }
}
