using System.Net;
using System.Text.Json.Nodes;
using OpenWigi.Sdk;

namespace OpenWigi.Plugins;

/// <summary>
/// Um resultado da busca: o pacote com a versão mais nova que este host carrega. Sem versão compatível,
/// <see cref="RequiredSdk"/> diz qual SDK a mais nova pede (para a loja mostrar "requer OpenWigiDash X.Y").
/// </summary>
public sealed record FeedPackage(
    string Id,
    string Title,
    string Description,
    string Authors,
    long TotalDownloads,
    string? IconUrl,
    PackageVersion LatestVersion,
    PackageVersion? CompatibleVersion,
    string Owners = "",
    string? ProjectUrl = null,
    string? LicenseUrl = null,
    PackageVersion? RequiredSdk = null,
    IReadOnlyList<PackageVersion>? Versions = null)
{
    /// <summary>Página do pacote no nuget.org.</summary>
    public string GalleryUrl => $"https://www.nuget.org/packages/{Id}";

    /// <summary>
    /// Publicado por uma conta confiável do nuget.org (<see cref="PluginFeed.VerifiedOwners"/>). Quem é dono
    /// de um pacote no nuget.org não dá para falsificar, ao contrário do id ou dos autores.
    /// </summary>
    public bool Verified => Owners.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Any(owner => PluginFeed.VerifiedOwners.Contains(owner, StringComparer.OrdinalIgnoreCase));
}

/// <summary>Uma versão publicada de um pacote, com o que precisa para decidir e baixar.</summary>
public sealed record FeedVersion(
    string Id,
    PackageVersion Version,
    bool Listed,
    PackageVersion? SdkVersion,
    string PackageContentUrl,
    string? CatalogUrl)
{
    /// <summary>Este host carrega a versão: depende do SDK com o mesmo major e minor menor ou igual.</summary>
    public bool IsCompatible => SdkVersion is { } sdk && OpenWigiSdk.IsCompatible(new Version(sdk.Major, sdk.Minor));
}

/// <summary>
/// Cliente mínimo do protocolo V3 do NuGet (o do nuget.org): index de serviços, busca filtrada pelo
/// packageType dos plugins, registro (versões e dependências), folha do catálogo (hash) e download.
/// </summary>
public sealed class PluginFeed
{
    public const string NuGetOrg = "https://api.nuget.org/v3/index.json";
    public const string PackageType = "OpenWigiDashPlugin";
    public const string SdkPackageId = "victorperez2911.OpenWigi.Sdk";

    /// <summary>Contas do nuget.org cujos pacotes a loja mostra como verificados (a lista curada é a #48).</summary>
    public static readonly IReadOnlyList<string> VerifiedOwners = ["victorperez2911"];

    private readonly HttpClient _http;
    private readonly string _indexUrl;
    private Services? _services;

    private sealed record Services(string Search, string Registrations, string PackageBase);

    public PluginFeed(HttpClient http, string indexUrl = NuGetOrg)
    {
        _http = http;
        _indexUrl = indexUrl;
    }

    /// <summary>HttpClient com descompressão automática: o registro SemVer 2.0 do nuget.org vem em gzip.</summary>
    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenWigiDashManager");
        return client;
    }

    /// <summary>Busca plugins (pré-release incluídas enquanto o SDK for preview) e resolve a versão compatível de cada um.</summary>
    public async Task<IReadOnlyList<FeedPackage>> SearchAsync(string? query, int take = 20, CancellationToken cancellationToken = default)
    {
        var services = await GetServicesAsync(cancellationToken);
        var url = $"{services.Search}?q={Uri.EscapeDataString(query ?? "")}&packageType={PackageType}&prerelease=true&semVerLevel=2.0.0&take={take}";
        var json = await GetJsonAsync(url, cancellationToken);
        var results = new List<FeedPackage>();
        foreach (var item in json["data"]?.AsArray() ?? [])
        {
            if (item is null || (string?)item["id"] is not { } id || !PackageVersion.TryParse((string?)item["version"], out var latest))
                continue;
            var versions = await GetVersionsAsync(id, cancellationToken);
            var compatible = PickCompatible(versions);
            var listed = versions.Where(v => v.Listed).ToList();
            results.Add(new FeedPackage(
                id,
                (string?)item["title"] is { Length: > 0 } title ? title : id,
                (string?)item["description"] ?? "",
                Join(item["authors"]),
                (long?)item["totalDownloads"] ?? 0,
                Url((string?)item["iconUrl"]),
                latest,
                compatible?.Version,
                Join(item["owners"]),
                Url((string?)item["projectUrl"]),
                Url((string?)item["licenseUrl"]),
                compatible is null ? listed.LastOrDefault()?.SdkVersion : null,
                listed.Select(v => v.Version).Reverse().ToList()));
        }
        return results;
    }

    /// <summary>Todas as versões do pacote no registro, da mais antiga para a mais nova. Vazio se o id não existe.</summary>
    public async Task<IReadOnlyList<FeedVersion>> GetVersionsAsync(string id, CancellationToken cancellationToken = default)
    {
        var services = await GetServicesAsync(cancellationToken);
        var lower = id.ToLowerInvariant();
        var index = await TryGetJsonAsync($"{services.Registrations}{lower}/index.json", cancellationToken);
        if (index is null)
            return [];
        var versions = new List<FeedVersion>();
        foreach (var page in index["items"]?.AsArray() ?? [])
        {
            if (page is null)
                continue;
            var leaves = page["items"]?.AsArray();
            if (leaves is null && (string?)page["@id"] is { } pageUrl)
                leaves = (await GetJsonAsync(pageUrl, cancellationToken))["items"]?.AsArray();
            foreach (var leaf in leaves ?? [])
            {
                var entry = leaf?["catalogEntry"];
                if (entry is null || !PackageVersion.TryParse((string?)entry["version"], out var version))
                    continue;
                var packageId = (string?)entry["id"] ?? id;
                versions.Add(new FeedVersion(
                    packageId,
                    version,
                    (bool?)entry["listed"] ?? true,
                    SdkLowerBound(entry),
                    (string?)leaf!["packageContent"]
                        ?? $"{services.PackageBase}{lower}/{version.Original.ToLowerInvariant()}/{lower}.{version.Original.ToLowerInvariant()}.nupkg",
                    (string?)entry["@id"]));
            }
        }
        versions.Sort((a, b) => a.Version.CompareTo(b.Version));
        return versions;
    }

    /// <summary>A mais nova listada e compatível; null se nenhuma servir.</summary>
    public static FeedVersion? PickCompatible(IEnumerable<FeedVersion> versions) =>
        versions.Where(v => v.Listed && v.IsCompatible).MaxBy(v => v.Version, Comparer<PackageVersion>.Default);

    /// <summary>Hash SHA-512 (base64) que o catálogo publica para o pacote; null se o feed não informa.</summary>
    public async Task<string?> GetPackageHashAsync(FeedVersion version, CancellationToken cancellationToken = default)
    {
        if (version.CatalogUrl is null)
            return null;
        var leaf = await TryGetJsonAsync(version.CatalogUrl, cancellationToken);
        if (leaf is null)
            return null;
        var algorithm = (string?)leaf["packageHashAlgorithm"];
        if (algorithm is not null && !algorithm.Equals("SHA512", StringComparison.OrdinalIgnoreCase))
            return null;
        return (string?)leaf["packageHash"];
    }

    /// <summary>Endereço do ícone embutido no pacote (flat container), para quando a busca não traz o iconUrl.</summary>
    public async Task<string> PackageIconUrlAsync(string id, PackageVersion version, CancellationToken cancellationToken = default)
    {
        var services = await GetServicesAsync(cancellationToken);
        var lower = id.ToLowerInvariant();
        return $"{services.PackageBase}{lower}/{version.Original.ToLowerInvariant()}/icon";
    }

    private static string Join(JsonNode? node) =>
        node is JsonArray array ? string.Join(", ", array.Select(a => (string?)a).Where(a => !string.IsNullOrWhiteSpace(a))) : (string?)node ?? "";

    // Só http(s): o endereço vai para um link na loja, e o pacote é de terceiros.
    private static string? Url(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri.ToString() : null;

    public async Task DownloadAsync(FeedVersion version, string destination, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".download";
        using (var response = await _http.GetAsync(version.PackageContentUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var file = File.Create(temp);
            await source.CopyToAsync(file, cancellationToken);
        }
        File.Move(temp, destination, overwrite: true);
    }

    private static PackageVersion? SdkLowerBound(JsonNode entry)
    {
        foreach (var group in entry["dependencyGroups"]?.AsArray() ?? [])
            foreach (var dependency in group?["dependencies"]?.AsArray() ?? [])
                if (string.Equals((string?)dependency?["id"], SdkPackageId, StringComparison.OrdinalIgnoreCase))
                    return PackageVersion.LowerBound((string?)dependency!["range"]);
        return null;
    }

    private async Task<Services> GetServicesAsync(CancellationToken cancellationToken)
    {
        if (_services is not null)
            return _services;
        var index = await GetJsonAsync(_indexUrl, cancellationToken);
        var resources = index["resources"]?.AsArray() ?? [];
        string Find(params string[] types) =>
            types.Select(t => resources.FirstOrDefault(r => (string?)r?["@type"] == t)?["@id"]).Select(n => (string?)n).FirstOrDefault(u => u is not null)
            ?? throw new InvalidOperationException($"O feed {_indexUrl} não publica {types[0]}.");
        static string Slash(string url) => url.EndsWith('/') ? url : url + "/";
        // O registro 3.6.0 inclui as versões SemVer 2.0 (como 2.0.0-preview.2); os anteriores as escondem.
        return _services = new Services(
            Find("SearchQueryService/3.5.0", "SearchQueryService/3.0.0-rc", "SearchQueryService"),
            Slash(Find("RegistrationsBaseUrl/3.6.0", "RegistrationsBaseUrl/Versioned", "RegistrationsBaseUrl")),
            Slash(Find("PackageBaseAddress/3.0.0")));
    }

    private async Task<JsonNode> GetJsonAsync(string url, CancellationToken cancellationToken) =>
        await TryGetJsonAsync(url, cancellationToken) ?? throw new HttpRequestException($"{url} não encontrado.", null, HttpStatusCode.NotFound);

    private async Task<JsonNode?> TryGetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException($"{url} devolveu JSON vazio.");
    }
}
