using OpenWigi.Plugins;

namespace OpenWigi.Cli;

/// <summary>
/// <c>owd plugin ...</c>: plugins do nuget.org. Não abre o aparelho, então funciona com o manager aberto;
/// o que estiver em uso troca quando o manager reabrir.
/// </summary>
internal static class PluginCommands
{
    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        var sub = args.FirstOrDefault()?.ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        var widgets = Option(rest, "--widgets") ?? PluginInstaller.DefaultWidgetsDirectory;
        var feedUrl = Option(rest, "--source") ?? PluginFeed.NuGetOrg;
        using var http = PluginFeed.CreateHttpClient();
        var feed = new PluginFeed(http, feedUrl);
        var installer = new PluginInstaller(feed, widgets, PluginInstaller.DefaultCacheDirectory);
        var positional = Positional(rest);

        try
        {
            return sub switch
            {
                "search" => await SearchAsync(feed, string.Join(' ', positional)),
                "install" when positional.Length == 1 => await InstallAsync(installer, positional[0], Option(rest, "--version")),
                "update" when rest.Contains("--all") || positional.Length == 1 => await UpdateAsync(installer, rest.Contains("--all") ? null : positional[0]),
                "remove" when positional.Length == 1 => Remove(installer, positional[0]),
                "list" => await ListAsync(installer, feed, rest.Contains("--offline")),
                _ => Help(),
            };
        }
        catch (PluginInstallException ex)
        {
            Console.Error.WriteLine($"erro: {ex.Message}");
            return 3;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"erro de rede: {ex.Message}");
            return 4;
        }
    }

    public const string HelpText = """
          owd plugin search [texto]        procura plugins no nuget.org
          owd plugin install ID [--version X]
                                           instala a versão mais nova compatível (ou a pedida)
          owd plugin update ID | --all     atualiza para a mais nova compatível
          owd plugin remove ID             tira o plugin (dados e configurações ficam)
          owd plugin list [--offline]      instalados, versão e se há atualização
                                           (--widgets PASTA e --source URL trocam a pasta e o feed)
        """;

    private static int Help()
    {
        Console.WriteLine("owd plugin — plugins do nuget.org (funciona com o manager aberto)\n");
        Console.WriteLine(HelpText);
        return 1;
    }

    private static async Task<int> SearchAsync(PluginFeed feed, string query)
    {
        var results = await feed.SearchAsync(query);
        if (results.Count == 0)
        {
            Console.WriteLine("Nenhum plugin encontrado.");
            return 0;
        }
        foreach (var p in results)
        {
            var version = p.CompatibleVersion?.ToString() ?? $"nenhuma compatível (mais nova: {p.LatestVersion})";
            Console.WriteLine($"{p.Id}  {version}");
            Console.WriteLine($"    {p.Authors} · {p.TotalDownloads:N0} downloads");
            if (p.Description.Length > 0)
                Console.WriteLine($"    {p.Description.ReplaceLineEndings(" ")}");
        }
        return 0;
    }

    private static async Task<int> InstallAsync(PluginInstaller installer, string id, string? version)
    {
        var result = await installer.InstallAsync(id, version);
        var what = result.Previous is { } previous && !previous.Equals(result.Version)
            ? $"{result.Id} {previous} → {result.Version}"
            : $"{result.Id} {result.Version}";
        Console.WriteLine(result.Applied
            ? $"Instalado: {what}. Reabra o manager para carregar."
            : $"Baixado: {what}. O plugin está em uso; a troca acontece quando o manager reabrir.");
        WarnDuplicates(installer, result.Id);
        return 0;
    }

    private static async Task<int> UpdateAsync(PluginInstaller installer, string? id)
    {
        List<string> targets = id is not null
            ? [id]
            : installer.List().Where(p => p.PackageId is not null && !p.PendingRemoval).Select(p => p.PackageId!).ToList();
        if (targets.Count == 0)
            Console.WriteLine("Nenhum plugin instalado pelo feed.");
        foreach (var target in targets)
        {
            var result = await installer.UpdateAsync(target);
            if (result is null)
                Console.WriteLine($"{target}: já está na mais nova compatível.");
            else
                Console.WriteLine($"{result.Id}: {result.Previous} → {result.Version}" + (result.Applied ? "" : " (troca quando o manager reabrir)"));
        }
        return 0;
    }

    private static int Remove(PluginInstaller installer, string id)
    {
        Console.WriteLine(installer.Remove(id)
            ? $"Removido: {id}. Reabra o manager."
            : $"{id} está em uso; sai quando o manager reabrir.");
        return 0;
    }

    private static async Task<int> ListAsync(PluginInstaller installer, PluginFeed feed, bool offline)
    {
        var plugins = installer.List();
        if (plugins.Count == 0)
        {
            Console.WriteLine($"Nenhum plugin em {installer.WidgetsDirectory}.");
            return 0;
        }
        foreach (var p in plugins)
        {
            var origin = p.PackageId is null ? "copiado à mão" : $"{p.PackageId} {p.Version}";
            var status = p.PendingRemoval ? " · sai quando o manager reabrir"
                : p.PendingUpdate ? " · troca quando o manager reabrir" : "";
            var update = "";
            if (!offline && p.PackageId is not null && p.Version is not null)
            {
                var latest = PluginFeed.PickCompatible(await feed.GetVersionsAsync(p.PackageId));
                if (latest is not null && latest.Version > p.Version)
                    update = $" · atualização: {latest.Version}";
            }
            Console.WriteLine($"{p.Folder}  ({origin}){update}{status}");
        }
        return 0;
    }

    // Um plugin copiado à mão em outra pasta com a mesma DLL carregaria os mesmos widgets duas vezes.
    private static void WarnDuplicates(PluginInstaller installer, string id)
    {
        var installed = installer.Find(id);
        var entry = installed is null ? null : EntryName(installed.Directory) ?? EntryName(Path.Combine(installer.WidgetsDirectory, PendingChanges.FolderName, id));
        if (entry is null)
            return;
        foreach (var other in installer.List().Where(p => p.PackageId is null && EntryName(p.Directory) == entry))
            Console.WriteLine($"aviso: {other.Folder} tem a mesma DLL ({entry}), copiada à mão. Remova com `owd plugin remove {other.Folder}` para não carregar os widgets duas vezes.");
    }

    private static string? EntryName(string directory)
    {
        var manifest = Path.Combine(directory, "plugin.json");
        if (File.Exists(manifest))
        {
            try
            {
                return (string?)System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifest))?["entry"];
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }
        var byName = Path.GetFileName(directory) + ".dll";
        return File.Exists(Path.Combine(directory, byName)) ? byName : null;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string[] Positional(string[] args)
    {
        var list = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--version" or "--widgets" or "--source")
                i++;
            else if (!args[i].StartsWith("--"))
                list.Add(args[i]);
        }
        return list.ToArray();
    }
}
