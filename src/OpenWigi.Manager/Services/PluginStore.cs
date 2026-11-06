using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenWigi.Host;
using OpenWigi.Host.Plugins;
using OpenWigi.Plugins;

namespace OpenWigi.Manager.Services;

/// <summary>
/// O que a tela Loja usa: busca no nuget.org, instalados, ícones, atualizações e o aviso de reiniciar.
/// A lógica é a mesma do <c>owd plugin</c> (OpenWigi.Plugins); aqui fica só o estado da UI.
/// </summary>
public sealed class PluginStore : IDisposable
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromDays(1);

    private readonly ManagerSettings _settings;
    private readonly DashboardHost _dashboard;
    private readonly ILogger _logger;
    private readonly HttpClient _http = PluginFeed.CreateHttpClient();
    private readonly PluginFeed _feed;
    private readonly PluginInstaller _installer;
    private readonly PluginIconCache _icons;
    private readonly VerifiedListSource _verified;
    private bool _policyLoaded;
    private readonly Dictionary<string, string?> _iconUrls = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly Timer _timer;
    private Dictionary<string, PackageVersion> _updates = new(StringComparer.OrdinalIgnoreCase);

    public PluginStore(ManagerSettings settings, DashboardHost dashboard, string widgetsDirectory, ILoggerFactory loggerFactory)
    {
        _settings = settings;
        _dashboard = dashboard;
        _logger = loggerFactory.CreateLogger<PluginStore>();
        _http.Timeout = TimeSpan.FromSeconds(30);
        _feed = new PluginFeed(_http);
        _installer = new PluginInstaller(_feed, widgetsDirectory, PluginInstaller.DefaultCacheDirectory);
        _icons = new PluginIconCache(_http, _feed, PluginIconCache.DefaultCacheDirectory);
        _verified = new VerifiedListSource(_http, VerifiedListSource.DefaultCacheFile);
        _feed.Policy = _verified.LoadCached();
        // Checagem diária em segundo plano: confere de hora em hora se já passou um dia da última.
        _timer = new Timer(_ => _ = BackgroundCheckAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));
    }

    /// <summary>Algo mudou (instalados, atualizações, reiniciar).</summary>
    public event Action? Changed;

    /// <summary>Instalou, atualizou ou removeu algo: só vale quando o manager reabrir.</summary>
    public bool RestartNeeded { get; private set; }

    public int UpdateCount => _updates.Count;

    /// <summary>A tela pediu para reiniciar o manager e aplicar as mudanças (o app trata, na thread do WPF).</summary>
    public event Action? RestartRequested;

    public void RequestRestart() => RestartRequested?.Invoke();

    public PackageVersion? UpdateFor(string packageId) => _updates.GetValueOrDefault(packageId);

    public IReadOnlyList<InstalledPlugin> Installed => _installer.List();

    public InstalledPlugin? FindInstalled(string packageId) => _installer.Find(packageId);

    public Task<IReadOnlyList<FeedPackage>> SearchAsync(string? query, CancellationToken cancellationToken) =>
        _feed.SearchAsync(string.IsNullOrWhiteSpace(query) ? null : query.Trim(), take: 30, cancellationToken);

    /// <summary>Compara os instalados com o feed. Sem rede, a exceção sobe para a tela mostrar.</summary>
    public async Task CheckUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var updates = await _installer.CheckUpdatesAsync(cancellationToken);
        _updates = updates.ToDictionary(u => u.Plugin.PackageId!, u => u.Version, StringComparer.OrdinalIgnoreCase);
        _settings.LastPluginUpdateCheck = DateTimeOffset.UtcNow;
        Changed?.Invoke();
    }

    /// <summary>Lista de verificados e versões bloqueadas: baixa de novo (ou fica com a cópia offline).</summary>
    public async Task RefreshPolicyAsync(CancellationToken cancellationToken = default)
    {
        _feed.Policy = await _verified.LoadAsync(cancellationToken);
        _policyLoaded = true;
        Changed?.Invoke();
    }

    public PluginTrust TrustOf(FeedPackage package) => _feed.Policy.TrustOf(package);

    /// <summary>Motivo do bloqueio da versão instalada; null se ela não está bloqueada.</summary>
    public string? BlockReason(InstalledPlugin plugin) =>
        plugin is { PackageId: { } id, Version: { } version, PendingRemoval: false } ? _feed.Policy.BlockReason(id, version) : null;

    /// <summary>Instalados numa versão bloqueada, com o motivo (para a faixa na Loja e em Widgets).</summary>
    public IReadOnlyList<(InstalledPlugin Plugin, string Reason)> BlockedInstalled =>
        Installed.Select(p => (p, BlockReason(p))).Where(x => x.Item2 is not null).Select(x => (x.p, x.Item2!)).ToList();

    private async Task BackgroundCheckAsync()
    {
        // Na primeira volta, a lista de bloqueio é atualizada para avisar quem tem versão com problema.
        if (!_policyLoaded)
        {
            try
            {
                await RefreshPolicyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Lista de verificados não carregou: {Message}", ex.Message);
            }
        }
        if (!_settings.CheckPluginUpdates || DateTimeOffset.UtcNow - (_settings.LastPluginUpdateCheck ?? DateTimeOffset.MinValue) < CheckEvery)
            return;
        try
        {
            await RefreshPolicyAsync();
            await CheckUpdatesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Checagem de atualização dos plugins falhou: {Message}", ex.Message);
        }
    }

    public Task<InstallResult> InstallAsync(string packageId, string? version = null) =>
        Change(() => _installer.InstallAsync(packageId, version));

    public Task<InstallResult?> UpdateAsync(string packageId) =>
        Change(() => _installer.UpdateAsync(packageId));

    public Task<bool> RemoveAsync(string packageId) =>
        Change(() => Task.Run(() => _installer.Remove(packageId)));

    private async Task<T> Change<T>(Func<Task<T>> action)
    {
        await _busy.WaitAsync();
        try
        {
            var result = await action();
            RestartNeeded = true;
            // A versão nova já é a instalada; a checagem é refeita na próxima vez.
            _updates = _updates.Where(u => _installer.Find(u.Key) is { Version: { } v } && u.Value > v)
                .ToDictionary(u => u.Key, u => u.Value, StringComparer.OrdinalIgnoreCase);
            return result;
        }
        finally
        {
            _busy.Release();
            Changed?.Invoke();
        }
    }

    /// <summary>Widgets que o plugin carregou nesta sessão (vazio se não carregou ou é novo).</summary>
    public IReadOnlyList<LoadedWidget> WidgetsOf(InstalledPlugin plugin) =>
        _dashboard.AvailableWidgets.Where(w => SamePath(w.PluginDirectory, plugin.Directory)).ToList();

    /// <summary>Tela de configurações do plugin inteiro, se ele declara uma e carregou nesta sessão.</summary>
    public PluginSettings? SettingsOf(InstalledPlugin plugin) => _dashboard.GetPluginSettings(plugin.Directory);

    /// <summary>
    /// Tela do plugin inteiro pela pasta (nome) do plugin, inclusive os que vêm com o instalador, que a Loja não lista
    /// como instalados.
    /// </summary>
    public PluginSettings? SettingsOf(string folder) =>
        _dashboard.AvailableWidgets.Select(w => w.PluginDirectory).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(d => Path.GetFileName(d.TrimEnd(Path.DirectorySeparatorChar)).Equals(folder, StringComparison.OrdinalIgnoreCase))
            .Select(_dashboard.GetPluginSettings).FirstOrDefault(s => s is not null);

    /// <summary>Endereço da página do plugin já na aba Configurações (o mesmo plugin pela pasta).</summary>
    public static string SettingsRoute(string pluginDirectory) =>
        $"/loja?configurar={Uri.EscapeDataString(Path.GetFileName(pluginDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))}";

    /// <summary>Quantos widgets deste plugin estão no layout (ficam como ausentes ao remover).</summary>
    public int InstancesOf(InstalledPlugin plugin)
    {
        var ids = WidgetsOf(plugin).Select(w => w.Manifest.Id).ToHashSet();
        return _dashboard.Layout.AllPlacements.Count(p => ids.Contains(p.WidgetId));
    }

    /// <summary>Ícone do plugin como data URL: o da pasta, se instalado, senão o do feed (com cache). Null = genérico.</summary>
    public async Task<string?> IconAsync(string packageId, FeedPackage? package)
    {
        if (_iconUrls.TryGetValue(packageId, out var cached))
            return cached;
        string? file = null;
        try
        {
            file = _installer.Find(packageId) is { } installed ? PluginIconCache.Installed(installed) : null;
            if (file is null && package is not null)
                file = await _icons.GetAsync(package);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogInformation("Ícone de {Id} não carregou: {Message}", packageId, ex.Message);
        }
        var url = file is null ? null : ToDataUrl(file);
        _iconUrls[packageId] = url;
        return url;
    }

    private static string? ToDataUrl(string file)
    {
        var bytes = File.ReadAllBytes(file);
        return bytes.Length > PluginIconCache.MaxBytes || PluginIconCache.ImageType(bytes) is not { } type
            ? null
            : $"data:{type};base64,{Convert.ToBase64String(bytes)}";
    }

    /// <summary>Abre um endereço http(s) no navegador do usuário.</summary>
    public static void OpenInBrowser(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _timer.Dispose();
        _http.Dispose();
    }
}
