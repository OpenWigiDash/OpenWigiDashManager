using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace OpenWigi.Plugins.Tests;

/// <summary>Feed V3 em memória: index, busca, registro, folha do catálogo e .nupkg gerado no teste.</summary>
internal sealed class FakeFeed : HttpMessageHandler
{
    public const string Index = "https://feed.test/v3/index.json";
    private readonly Dictionary<string, (byte[] Body, string Type)> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<JsonObject>> _leaves = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Requests { get; } = [];

    /// <summary>Campos a mais no resultado da busca de cada id (owners, iconUrl, projectUrl...).</summary>
    public Dictionary<string, JsonObject> SearchExtras { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sem rede: toda chamada falha como falharia sem internet.</summary>
    public bool Offline { get; set; }

    public FakeFeed()
    {
        Json(Index, new JsonObject
        {
            ["version"] = "3.0.0",
            ["resources"] = new JsonArray(
                Resource("https://feed.test/query", "SearchQueryService/3.5.0"),
                Resource("https://feed.test/registration5-gz-semver2/", "RegistrationsBaseUrl/3.6.0"),
                Resource("https://feed.test/flat/", "PackageBaseAddress/3.0.0")),
        });
    }

    public PluginFeed CreateFeed() => new(new HttpClient(this), Index);

    /// <summary>Publica uma versão. <paramref name="sdk"/> null = sem dependência do SDK.</summary>
    public void Publish(string id, string version, string? sdk = "2.0.0-preview.3", bool listed = true, bool plugin = true,
        IDictionary<string, string>? files = null, string? wrongHash = null)
    {
        var lower = id.ToLowerInvariant();
        var v = version.ToLowerInvariant();
        var nupkg = BuildPackage(id, version, plugin, files);
        var contentUrl = $"https://feed.test/flat/{lower}/{v}/{lower}.{v}.nupkg";
        var catalogUrl = $"https://feed.test/catalog/{lower}.{v}.json";
        _routes[contentUrl] = (nupkg, "application/octet-stream");
        Json(catalogUrl, new JsonObject
        {
            ["packageHash"] = wrongHash ?? Convert.ToBase64String(SHA512.HashData(nupkg)),
            ["packageHashAlgorithm"] = "SHA512",
        });

        var entry = new JsonObject
        {
            ["@id"] = catalogUrl,
            ["id"] = id,
            ["version"] = version,
            ["listed"] = listed,
            ["dependencyGroups"] = sdk is null ? new JsonArray() : new JsonArray(new JsonObject
            {
                ["targetFramework"] = "net10.0-windows7.0",
                ["dependencies"] = new JsonArray(new JsonObject { ["id"] = PluginFeed.SdkPackageId, ["range"] = $"[{sdk}, )" }),
            }),
        };
        if (!_leaves.TryGetValue(lower, out var leaves))
            _leaves[lower] = leaves = [];
        leaves.Add(new JsonObject { ["catalogEntry"] = entry, ["packageContent"] = contentUrl });
        Json($"https://feed.test/registration5-gz-semver2/{lower}/index.json", new JsonObject
        {
            ["items"] = new JsonArray(new JsonObject { ["items"] = new JsonArray(leaves.Select(l => (JsonNode)l.DeepClone()).ToArray()) }),
        });
        RefreshSearch();
    }

    /// <summary>Refaz o resultado da busca (depois de mexer em <see cref="SearchExtras"/>).</summary>
    public void RefreshSearch() =>
        Json("https://feed.test/query", new JsonObject
        {
            ["data"] = new JsonArray(_leaves.Select(kv =>
            {
                var id = (string)kv.Value[^1]["catalogEntry"]!["id"]!;
                var item = new JsonObject
                {
                    ["id"] = id,
                    ["version"] = (string?)kv.Value[^1]["catalogEntry"]!["version"],
                    ["description"] = "Plugin de teste",
                    ["authors"] = new JsonArray("Victor"),
                    ["totalDownloads"] = 42,
                };
                foreach (var (key, value) in SearchExtras.GetValueOrDefault(id) ?? [])
                    item[key] = value?.DeepClone();
                return (JsonNode)item;
            }).ToArray()),
        });

    /// <summary>Responde um endereço qualquer (ícones, por exemplo).</summary>
    public void Route(string url, byte[] body, string type = "image/png") => _routes[url] = (body, type);

    /// <summary>Troca o conteúdo de um .nupkg já publicado (para testar arquivos maliciosos).</summary>
    public void ReplacePackage(string id, string version, byte[] nupkg, bool fixHash = true)
    {
        var lower = id.ToLowerInvariant();
        var v = version.ToLowerInvariant();
        _routes[$"https://feed.test/flat/{lower}/{v}/{lower}.{v}.nupkg"] = (nupkg, "application/octet-stream");
        if (fixHash)
            Json($"https://feed.test/catalog/{lower}.{v}.json", new JsonObject
            {
                ["packageHash"] = Convert.ToBase64String(SHA512.HashData(nupkg)),
                ["packageHashAlgorithm"] = "SHA512",
            });
    }

    public static byte[] BuildPackage(string id, string version, bool plugin = true, IDictionary<string, string>? files = null)
    {
        files ??= new Dictionary<string, string>
        {
            ["plugin/plugin.json"] = "{ \"entry\": \"Teste.dll\" }",
            ["plugin/Teste.dll"] = "dll",
            ["plugin/wwwroot/app.css"] = "css",
            ["icon.png"] = "png",
            ["lib/net10.0/Ignorado.dll"] = "nao-extrair",
        };
        var types = plugin ? $"<packageTypes><packageType name=\"{PluginFeed.PackageType}\" /></packageTypes>" : "";
        var nuspec = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata><id>{id}</id><version>{version}</version><icon>icon.png</icon>{types}</metadata>
            </package>
            """;
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, $"{id}.nuspec", nuspec);
            foreach (var (name, content) in files)
                Add(zip, name, content);
        }
        return memory.ToArray();
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static JsonObject Resource(string id, string type) => new() { ["@id"] = id, ["@type"] = type };

    private void Json(string url, JsonNode body) => _routes[url] = (Encoding.UTF8.GetBytes(body.ToJsonString()), "application/json");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
        Requests.Add(request.RequestUri!.ToString());
        if (Offline)
            throw new HttpRequestException("Sem rede (teste).");
        if (!_routes.TryGetValue(url, out var route))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var content = new ByteArrayContent(route.Body);
        content.Headers.ContentType = new(route.Type);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}
