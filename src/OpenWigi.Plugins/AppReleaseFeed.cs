using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace OpenWigi.Plugins;

/// <summary>Uma release do OpenWigiDash no GitHub, com o que o aviso de atualização mostra.</summary>
/// <param name="SetupUrl">Download do <see cref="AppReleaseFeed.SetupAssetName"/>; null se a release não tiver.</param>
public sealed record AppRelease(
    PackageVersion Version,
    string Tag,
    string Name,
    bool IsPrerelease,
    string PageUrl,
    string Notes,
    DateTimeOffset? PublishedAt,
    string? SetupUrl);

/// <summary>
/// Releases do próprio OpenWigiDash (não dos plugins) pela API do GitHub. A tag é <c>vX.Y.Z</c>, a mesma
/// que o release.yml usa para gerar o instalador. Só lê: não manda nada além do pedido HTTP, sem token.
/// </summary>
public sealed class AppReleaseFeed
{
    public const string Repository = "victorperez2911/OpenWigiDashManager";
    public const string ReleasesUrl = $"https://api.github.com/repos/{Repository}/releases?per_page=20";
    public const string SetupAssetName = "OpenWigiDash-Setup.exe";

    private readonly HttpClient _http;
    private readonly string _url;

    /// <param name="http">Precisa de User-Agent, que a API do GitHub exige (<see cref="PluginFeed.CreateHttpClient"/> já põe).</param>
    public AppReleaseFeed(HttpClient http, string releasesUrl = ReleasesUrl)
    {
        _http = http;
        _url = releasesUrl;
    }

    /// <summary>A release mais nova que <paramref name="current"/>, ou null se já está na última. Sem rede, a exceção sobe.</summary>
    public async Task<AppRelease?> CheckAsync(PackageVersion current, bool includePrerelease, CancellationToken cancellationToken = default) =>
        PickUpdate(current, await ListAsync(cancellationToken), includePrerelease);

    /// <summary>As releases publicadas (sem rascunhos e sem tags que não são versão), na ordem que o GitHub devolve.</summary>
    public async Task<IReadOnlyList<AppRelease>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>
    /// A mais nova acima de <paramref name="current"/>. Pré-release (marcada no GitHub ou com <c>-beta</c>,
    /// <c>-rc</c>... na tag) só entra com <paramref name="includePrerelease"/>.
    /// </summary>
    public static AppRelease? PickUpdate(PackageVersion current, IEnumerable<AppRelease> releases, bool includePrerelease) =>
        releases.Where(r => r.Version > current && (includePrerelease || !r.IsPrerelease)).MaxBy(r => r.Version);

    internal static IReadOnlyList<AppRelease> Parse(string json)
    {
        var releases = new List<AppRelease>();
        if (JsonNode.Parse(json) is not JsonArray array)
            return releases;
        foreach (var node in array.OfType<JsonObject>())
        {
            if (Bool(node, "draft"))
                continue;
            var tag = Text(node, "tag_name");
            if (!PackageVersion.TryParse(tag.TrimStart('v', 'V'), out var version))
                continue;
            var setup = (node["assets"] as JsonArray)?.OfType<JsonObject>()
                .FirstOrDefault(a => string.Equals(Text(a, "name"), SetupAssetName, StringComparison.OrdinalIgnoreCase));
            var setupUrl = setup is null ? "" : Text(setup, "browser_download_url");
            var published = DateTimeOffset.TryParse(Text(node, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                ? at
                : (DateTimeOffset?)null;
            var name = Text(node, "name");
            releases.Add(new AppRelease(
                version,
                tag,
                string.IsNullOrWhiteSpace(name) ? tag : name,
                Bool(node, "prerelease") || version.IsPrerelease,
                Text(node, "html_url"),
                Text(node, "body"),
                published,
                setupUrl.Length > 0 ? setupUrl : null));
        }
        return releases;
    }

    private static string Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";

    private static bool Bool(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
}
