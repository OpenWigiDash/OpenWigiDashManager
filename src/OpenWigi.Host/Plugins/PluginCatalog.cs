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

/// <summary>
/// Tela de configurações do plugin inteiro (<see cref="OpenWigiPluginSettingsAttribute"/>), com o
/// <see cref="IWidgetHost"/> que ela recebe em cascata.
/// </summary>
public sealed record PluginSettings(string PluginDirectory, Type Component, IWidgetHost Host);

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
    private readonly List<(PluginLoadContext Context, List<LoadedWidget> Widgets, PluginSettings? Settings)> _plugins = [];
    private readonly List<PluginLoadError> _errors = [];

    public IReadOnlyList<LoadedWidget> Widgets => _plugins.SelectMany(p => p.Widgets).ToList();
    public IReadOnlyList<PluginLoadError> Errors => _errors;

    public LoadedWidget? Find(Guid widgetId) => Widgets.FirstOrDefault(w => w.Manifest.Id == widgetId);

    /// <summary>Tela de configurações do plugin da pasta <paramref name="pluginDirectory"/>, ou null se ele não tem.</summary>
    public PluginSettings? SettingsOf(string pluginDirectory) =>
        _plugins.Select(p => p.Settings).FirstOrDefault(s => s is not null && SamePath(s.PluginDirectory, pluginDirectory));

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

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

            // Instalações, atualizações e remoções feitas pelo owd plugin/loja enquanto o plugin estava em uso.
            try
            {
                foreach (var (directory, reason) in OpenWigi.Plugins.PendingChanges.Apply(root))
                    Fail(directory, reason);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Mudanças pendentes de plugins em {Root} não foram aplicadas", root);
            }

            // Pastas com ponto (.pending e outras) não são plugins.
            foreach (var directory in Directory.GetDirectories(root).Where(d => !Path.GetFileName(d).StartsWith('.')).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!loaded.Add(Path.GetFileName(directory)))
                {
                    _logger.LogInformation("Plugin {Directory} ignorado: já carregado de outra pasta", directory);
                    continue;
                }
                // Um plugin com qualquer defeito (plugin.json ruim, DLL corrompida...) fica na lista de erros;
                // os outros carregam e o manager abre.
                try
                {
                    await LoadAsync(directory, hosts, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Plugin {Directory} não carregou", directory);
                    Fail(directory, $"Não carregou: {ex.Message}");
                }
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
        PluginSettings? settings = null;
        IWidgetHost? firstHost = null; // a tela do plugin recebe o mesmo host dos widgets (pasta, log, relógio, tema)
        try
        {
            var assembly = context.LoadFromAssemblyPath(entry);
            // Antes de ler atributos e criar tipos: feito para outro SDK, o plugin quebraria com erro de tipo.
            if (SdkProblem(assembly.GetReferencedAssemblies().FirstOrDefault(r => r.Name == typeof(IWidget).Assembly.GetName().Name)?.Version) is { } sdk)
                throw new InvalidOperationException(sdk);
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

                var host = hosts.Create(widget.Manifest, pluginDirectory);
                await widget.LoadAsync(host, cancellationToken);
                widgets.Add(new LoadedWidget(widget, pluginDirectory));
                _logger.LogInformation("Widget carregado: {Name} {Version} ({Id})", widget.Manifest.Name, widget.Manifest.Version, widget.Manifest.Id);
                firstHost ??= host;
            }
            if (firstHost is not null)
                settings = ReadSettings(assembly, pluginDirectory, firstHost);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (var widget in widgets)
                await TryUnloadAsync(widget.Widget);
            context.Unload();
            Fail(pluginDirectory, ex is TargetInvocationException { InnerException: { } inner } ? inner.Message : ex.Message);
            return;
        }

        _plugins.Add((context, widgets, settings));
    }

    /// <summary>
    /// A tela do plugin inteiro, se o assembly declara uma. Tipo que não é componente Razor fica de fora com aviso
    /// no log: os widgets carregam do mesmo jeito.
    /// </summary>
    private PluginSettings? ReadSettings(Assembly assembly, string pluginDirectory, IWidgetHost host)
    {
        if (assembly.GetCustomAttribute<OpenWigiPluginSettingsAttribute>() is not { ComponentType: { } type })
            return null;
        if (!typeof(Microsoft.AspNetCore.Components.IComponent).IsAssignableFrom(type) || type.IsAbstract)
        {
            _logger.LogWarning("Plugin {Directory}: {Type} não é um componente Razor; a tela de configurações do plugin fica de fora",
                pluginDirectory, type);
            return null;
        }
        return new PluginSettings(pluginDirectory, type, host);
    }

    /// <summary>Descarrega todos os plugins. As instâncias já devem ter sido descartadas.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var (context, widgets, _) in _plugins)
        {
            foreach (var widget in widgets)
                await TryUnloadAsync(widget.Widget);
            context.Unload();
        }
        _plugins.Clear();
    }

    /// <summary>
    /// Mensagem para o plugin compilado contra um SDK que este host não carrega (o mesmo critério de
    /// <see cref="OpenWigiSdk.IsCompatible"/>), ou null se serve. Sem referência ao SDK, quem decide é o resto da carga.
    /// </summary>
    internal static string? SdkProblem(Version? referenced)
    {
        if (referenced is null || OpenWigiSdk.IsCompatible(new Version(referenced.Major, referenced.Minor)))
            return null;
        var host = OpenWigiSdk.Version;
        return referenced.Major > host.Major || (referenced.Major == host.Major && referenced.Minor > host.Minor)
            ? $"Feito para o SDK {referenced.Major}.{referenced.Minor}, mais novo que o deste OpenWigiDash ({host.Major}.{host.Minor}). Atualize o OpenWigiDash."
            : $"Feito para o SDK {referenced.Major}.{referenced.Minor}, que este OpenWigiDash não carrega mais (ele usa o {host.Major}.{host.Minor}). Procure uma versão nova do plugin na Loja.";
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
