using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenWigi.Plugins;

/// <summary>Quanto confiar num pacote: da comunidade (sem revisão), verificado (na lista) ou oficial (conta do projeto).</summary>
public enum PluginTrust
{
    Community,
    Verified,
    Official,
}

/// <summary>Uma entrada da lista: plugin revisado (com <see cref="Owner"/>) ou só bloqueio de versões.</summary>
public sealed record VerifiedEntry(string Id, string? Owner, string? Repository, IReadOnlyList<(string Text, VersionRange Range)> Blocked,
    IReadOnlyDictionary<string, string> Reasons)
{
    /// <summary>Revisado: entrada com dono. Sem dono, a entrada só bloqueia versões.</summary>
    public bool Verified => !string.IsNullOrWhiteSpace(Owner);
}

/// <summary>
/// Lista de plugins verificados e de versões bloqueadas (o nuget.org não apaga versões, só as tira da busca).
/// O selo vale só quando o dono do pacote no nuget.org bate com o <c>owner</c> da entrada: um id parecido
/// publicado por outra conta não ganha o selo.
/// </summary>
public sealed class VerifiedList
{
    /// <summary>Endereço público (sem token) de onde o manager baixa a lista.</summary>
    public const string DefaultUrl = "https://raw.githubusercontent.com/victorperez2911/OpenWigiDashManager/main/plugins/verified.json";

    public static readonly VerifiedList Empty = new([]);

    private readonly Dictionary<string, VerifiedEntry> _entries;

    private VerifiedList(IEnumerable<VerifiedEntry> entries) =>
        _entries = entries.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<VerifiedEntry> Entries => _entries.Values;

    public VerifiedEntry? Find(string id) => _entries.GetValueOrDefault(id);

    /// <summary>Lê o JSON (formato da versão 1). Entrada sem id ou com faixa inválida faz a lista toda ser recusada.</summary>
    public static VerifiedList Parse(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("verified.json não é um objeto.");
        if ((int?)root["version"] != 1)
            throw new InvalidDataException($"verified.json versão {(string?)root["version"]?.ToString()} não suportada (esperado 1).");
        var entries = new List<VerifiedEntry>();
        foreach (var node in root["plugins"]?.AsArray() ?? [])
        {
            var id = (string?)node?["id"];
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException("verified.json tem plugin sem id.");
            var blocked = new List<(string, VersionRange)>();
            foreach (var item in node!["blockedVersions"]?.AsArray() ?? [])
            {
                var text = (string?)item;
                if (!VersionRange.TryParse(text, out var range))
                    throw new InvalidDataException($"verified.json: versão ou faixa inválida em {id}: {text}");
                blocked.Add((text!, range));
            }
            var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (node["reason"] is JsonObject reasonObject)
                foreach (var (key, value) in reasonObject)
                    if ((string?)value is { } reason)
                        reasons[key] = reason;
            entries.Add(new VerifiedEntry(id, (string?)node["owner"], (string?)node["repository"], blocked, reasons));
        }
        return new VerifiedList(entries);
    }

    /// <summary>Revisado: está na lista com dono, e esse dono é um dos donos do pacote no nuget.org.</summary>
    public bool IsVerified(string id, string owners) =>
        Find(id) is { Verified: true } entry && Split(owners).Contains(entry.Owner!.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Oficial pela conta do projeto; senão verificado pela lista (dono conferido); senão comunidade.</summary>
    public PluginTrust TrustOf(FeedPackage package) =>
        package.Official ? PluginTrust.Official
        : IsVerified(package.Id, package.Owners) ? PluginTrust.Verified
        : PluginTrust.Community;

    /// <summary>Motivo do bloqueio da versão, ou null se ela não está bloqueada.</summary>
    public string? BlockReason(string id, PackageVersion version)
    {
        if (Find(id) is not { } entry)
            return null;
        foreach (var (text, range) in entry.Blocked)
            if (range.Contains(version))
                return entry.Reasons.GetValueOrDefault(text) ?? entry.Reasons.GetValueOrDefault(version.Original) ?? "Versão bloqueada pelos mantenedores do OpenWigiDash.";
        return null;
    }

    public bool IsBlocked(string id, PackageVersion version) => BlockReason(id, version) is not null;

    internal static IEnumerable<string> Split(string owners) =>
        owners.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Baixa a lista e guarda uma cópia para uso offline. Sem rede e sem cópia, a lista fica vazia.</summary>
public sealed class VerifiedListSource(HttpClient http, string cacheFile, string url = VerifiedList.DefaultUrl)
{
    public static string DefaultCacheFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenWigiDash", "Cache", "verified.json");

    /// <summary>A lista do servidor; se falhar, a última que deu certo; se não houver, a vazia.</summary>
    public async Task<VerifiedList> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var list = VerifiedList.Parse(json);
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            await File.WriteAllTextAsync(cacheFile, json, cancellationToken);
            return list;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or JsonException or IOException
                                   && !cancellationToken.IsCancellationRequested)
        {
            return LoadCached();
        }
    }

    public VerifiedList LoadCached()
    {
        try
        {
            return File.Exists(cacheFile) ? VerifiedList.Parse(File.ReadAllText(cacheFile)) : VerifiedList.Empty;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            return VerifiedList.Empty;
        }
    }
}
