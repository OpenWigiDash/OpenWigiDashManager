using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenWigi.Sdk;

namespace OpenWigi.Host.Plugins;

/// <summary>Um widget carregado de um plugin, pronto para criar instâncias.</summary>
public sealed record LoadedWidget(IWidget Widget, string PluginDirectory)
{
    public WidgetManifest Manifest => Widget.Manifest;
}

/// <summary>Plugin que não carregou, com o motivo, para a UI mostrar.</summary>
public sealed record PluginLoadError(string PluginDirectory, string Reason);

/// <summary>
/// Acha e carrega plugins. Cada subpasta da raiz é um plugin; a DLL de entrada é a de
/// <c>plugin.json</c> (<c>{ "entry": "Meu.dll" }</c>) ou, sem ele, a que tem o nome da pasta.
/// Os widgets são lidos do atributo <see cref="OpenWigiWidgetAttribute"/>, sem varrer tipos.
/// </summary>
public sealed class PluginCatalog(ILoggerFactory loggerFactory) : IAsyncDisposable
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<PluginCatalog>();
    private readonly List<(PluginLoadContext Context, List<LoadedWidget> Widgets)> _plugins = [];
    private readonly List<PluginLoadError> _errors = [];

    public IReadOnlyList<LoadedWidget> Widgets => _plugins.SelectMany(p => p.Widgets).ToList();
    public IReadOnlyList<PluginLoadError> Errors => _errors;

    public LoadedWidget? Find(Guid widgetId) => Widgets.FirstOrDefault(w => w.Manifest.Id == widgetId);

    public Task LoadAllAsync(string root, IWidgetHostFactory hosts, CancellationToken cancellationToken = default) =>
        LoadAllAsync([root], hosts, cancellationToken);

    /// <summary>
    /// Carrega várias raízes. Um plugin com o mesmo nome de pasta numa raiz anterior ganha: assim a cópia
    /// do usuário substitui a que veio com o instalador, sem erro de id repetido.
    /// </summary>
    public async Task LoadAllAsync(IEnumerable<string> roots, IWidgetHostFactory hosts, CancellationToken cancellationToken = default)
    {
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                _logger.LogInformation("Pasta de plugins não existe: {Root}", root);
                continue;
            }

            foreach (var directory in Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (loaded.Add(Path.GetFileName(directory)))
                    await LoadAsync(directory, hosts, cancellationToken);
                else
                    _logger.LogInformation("Plugin {Directory} ignorado: já carregado de outra pasta", directory);
            }
        }
    }

    public async Task LoadAsync(string pluginDirectory, IWidgetHostFactory hosts, CancellationToken cancellationToken = default)
    {
        var entry = FindEntry(pluginDirectory);
        if (entry is null)
        {
            Fail(pluginDirectory, "DLL de entrada não encontrada (plugin.json ou <pasta>.dll).");
            return;
        }

        var context = new PluginLoadContext(entry);
        var widgets = new List<LoadedWidget>();
        try
        {
            var assembly = context.LoadFromAssemblyPath(entry);
            var declared = assembly.GetCustomAttributes<OpenWigiWidgetAttribute>().ToList();
            if (declared.Count == 0)
                throw new InvalidOperationException("O assembly não declara [assembly: OpenWigiWidget(typeof(...))].");

            foreach (var attribute in declared)
            {
                if (Activator.CreateInstance(attribute.WidgetType) is not IWidget widget)
                    throw new InvalidOperationException($"{attribute.WidgetType} não implementa IWidget.");

                var problems = widget.Manifest.Validate();
                if (problems.Count > 0)
                    throw new InvalidOperationException($"{widget.Manifest.Name}: {string.Join(" ", problems)}");
                if (Find(widget.Manifest.Id) is { } clash)
                    throw new InvalidOperationException($"Id {widget.Manifest.Id} já usado por {clash.Manifest.Name}.");

                await widget.LoadAsync(hosts.Create(widget.Manifest, pluginDirectory), cancellationToken);
                widgets.Add(new LoadedWidget(widget, pluginDirectory));
                _logger.LogInformation("Widget carregado: {Name} {Version} ({Id})", widget.Manifest.Name, widget.Manifest.Version, widget.Manifest.Id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (var widget in widgets)
                await TryUnloadAsync(widget.Widget);
            context.Unload();
            Fail(pluginDirectory, ex is TargetInvocationException { InnerException: { } inner } ? inner.Message : ex.Message);
            return;
        }

        _plugins.Add((context, widgets));
    }

    /// <summary>Descarrega todos os plugins. As instâncias já devem ter sido descartadas.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var (context, widgets) in _plugins)
        {
            foreach (var widget in widgets)
                await TryUnloadAsync(widget.Widget);
            context.Unload();
        }
        _plugins.Clear();
    }

    internal static string? FindEntry(string pluginDirectory)
    {
        var manifest = Path.Combine(pluginDirectory, "plugin.json");
        if (File.Exists(manifest))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            if (document.RootElement.TryGetProperty("entry", out var value) && value.GetString() is { Length: > 0 } name)
            {
                var path = Path.GetFullPath(Path.Combine(pluginDirectory, name));
                return File.Exists(path) ? path : null;
            }
        }

        var byName = Path.Combine(pluginDirectory, Path.GetFileName(pluginDirectory) + ".dll");
        return File.Exists(byName) ? Path.GetFullPath(byName) : null;
    }

    private void Fail(string pluginDirectory, string reason)
    {
        _errors.Add(new PluginLoadError(pluginDirectory, reason));
        _logger.LogWarning("Plugin em {Directory} não carregou: {Reason}", pluginDirectory, reason);
    }

    private async Task TryUnloadAsync(IWidget widget)
    {
        try
        {
            await widget.UnloadAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao descarregar {Name}", widget.Manifest.Name);
        }
    }
}

/// <summary>Monta o <see cref="IWidgetHost"/> que cada widget recebe no LoadAsync.</summary>
public interface IWidgetHostFactory
{
    IWidgetHost Create(WidgetManifest manifest, string pluginDirectory);
}
