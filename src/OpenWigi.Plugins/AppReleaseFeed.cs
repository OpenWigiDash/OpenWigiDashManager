using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace OpenWigi.Plugins;

/// <summary>Uma release do OpenWigiDash no GitHub, com o que o aviso de atualização mostra.</summary>
/// <param name="SetupUrl">Download do <see cref="AppReleaseFeed.SetupAssetName"/>; null se a release não tiver.</param>
/// <param name="ChecksumsUrl">Download do <see cref="AppReleaseFeed.ChecksumsAssetName"/>; null nas releases de antes dele.</param>
public sealed record AppRelease(
    PackageVersion Version,
    string Tag,
    string Name,
    bool IsPrerelease,
    string PageUrl,
    string Notes,
    DateTimeOffset? PublishedAt,
    string? SetupUrl,
    string? ChecksumsUrl = null)
{
    /// <summary>Dá para baixar e conferir o instalador desta release (as antigas só têm a página).</summary>
    public bool CanDownload => SetupUrl is not null && ChecksumsUrl is not null;
}

/// <summary>
/// Releases do próprio OpenWigiDash (não dos plugins) pela API do GitHub. A tag é <c>vX.Y.Z</c>, a mesma
/// que o release.yml usa para gerar o instalador. Só lê: não manda nada além do pedido HTTP, sem token.
/// </summary>
public sealed class AppReleaseFeed
{
    public const string Repository = "victorperez2911/OpenWigiDashManager";
    public const string ReleasesUrl = $"https://api.github.com/repos/{Repository}/releases?per_page=20";
    public const string SetupAssetName = "OpenWigiDash-Setup.exe";

    /// <summary>Somas SHA-256 dos arquivos da release, no formato do sha256sum (<c>hash  arquivo</c>).</summary>
    public const string ChecksumsAssetName = "SHA256SUMS.txt";

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
            var assets = (node["assets"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
            string? AssetUrl(string fileName) =>
                assets.FirstOrDefault(a => string.Equals(Text(a, "name"), fileName, StringComparison.OrdinalIgnoreCase)) is { } asset
                && Text(asset, "browser_download_url") is { Length: > 0 } url
                    ? url
                    : null;
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
                AssetUrl(SetupAssetName),
                AssetUrl(ChecksumsAssetName)));
        }
        return releases;
    }

    /// <summary>
    /// Baixa o <see cref="SetupAssetName"/> da release para <paramref name="directory"/> e confere o SHA-256 com
    /// o <see cref="ChecksumsAssetName"/> dela. Arquivo que não bate é apagado e a exceção sobe.
    /// </summary>
    /// <param name="progress">Bytes baixados e o total, quando o servidor diz.</param>
    public async Task<string> DownloadSetupAsync(AppRelease release, string directory, IProgress<(long Done, long? Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (release.SetupUrl is not { } setupUrl || release.ChecksumsUrl is not { } checksumsUrl)
            throw new InvalidOperationException($"A release {release.Tag} não tem o instalador e as somas SHA-256 para baixar.");

        var expected = FindChecksum(await _http.GetStringAsync(checksumsUrl, cancellationToken), SetupAssetName)
            ?? throw new InvalidDataException($"O {ChecksumsAssetName} da release {release.Tag} não tem a soma do {SetupAssetName}.");

        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, SetupAssetName);
        var partial = target + ".part";
        using (var response = await _http.GetAsync(setupUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = File.Create(partial))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    done += read;
                    progress?.Report((done, total));
                }
            }
            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partial);
                throw new InvalidDataException($"O {SetupAssetName} baixado não bate com o SHA-256 da release (veio {actual}, esperado {expected}).");
            }
        }
        File.Move(partial, target, overwrite: true);
        return target;
    }

    /// <summary>A soma de <paramref name="fileName"/> num arquivo do sha256sum (<c>hash  nome</c> ou <c>hash *nome</c>), ou null.</summary>
    internal static string? FindChecksum(string sums, string fileName)
    {
        foreach (var line in sums.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit)
                && string.Equals(parts[1].Trim().TrimStart('*'), fileName, StringComparison.OrdinalIgnoreCase))
                return parts[0].ToLowerInvariant();
        }
        return null;
    }

    private static string Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";

    private static bool Bool(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
}
