using System.Text.Json;

namespace OpenWigi.Host.Plugins;

/// <summary>
/// Widgets que já carregaram alguma vez neste perfil: nome e pasta do plugin. Quando um plugin some ou
/// deixa de carregar (removido, ou feito para outro SDK depois de atualizar o OpenWigiDash), o layout
/// ainda tem o id do widget, e é por aqui que a UI sabe o nome e de qual plugin ele era.
/// </summary>
internal sealed class KnownWidgets(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public sealed record Entry(string Name, string PluginFolder);

    private Dictionary<Guid, Entry> _entries = [];

    public Entry? Find(Guid widgetId) => _entries.GetValueOrDefault(widgetId);

    public void Load()
    {
        try
        {
            _entries = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<Guid, Entry>>(File.ReadAllText(path), Json) ?? []
                : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _entries = [];
        }
    }

    /// <summary>Guarda os que carregaram agora. Os que não carregaram ficam como estavam: é deles que se precisa lembrar.</summary>
    public void Remember(IEnumerable<LoadedWidget> widgets)
    {
        var changed = false;
        foreach (var widget in widgets)
        {
            var entry = new Entry(widget.Manifest.Name, Path.GetFileName(widget.PluginDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            if (_entries.GetValueOrDefault(widget.Manifest.Id) == entry)
                continue;
            _entries[widget.Manifest.Id] = entry;
            changed = true;
        }
        if (!changed)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_entries, Json));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Só serve para o aviso na UI: sem gravar, o widget que sumir aparece sem nome.
        }
    }
}

/// <summary>Por que uma instância do layout não está funcionando.</summary>
public enum WidgetProblemKind
{
    /// <summary>O plugin não está instalado (foi removido, ou o widget nunca carregou neste perfil).</summary>
    NotInstalled,

    /// <summary>O plugin está instalado mas não carregou (ex.: feito para outro SDK). <see cref="WidgetProblem.Reason"/> diz o motivo.</summary>
    PluginFailed,

    /// <summary>O widget carregou, mas a instância deu erro ao abrir.</summary>
    InstanceFailed,
}

/// <param name="WidgetName">Nome do widget, se ele já carregou alguma vez; null se nunca se soube.</param>
/// <param name="PluginFolder">Pasta do plugin (o id do pacote, quando veio da Loja); null se nunca se soube.</param>
public sealed record WidgetProblem(WidgetProblemKind Kind, string? WidgetName, string? PluginFolder, string? Reason);
