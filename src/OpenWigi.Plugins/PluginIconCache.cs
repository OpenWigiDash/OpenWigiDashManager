namespace OpenWigi.Plugins;

/// <summary>
/// Ícones dos pacotes para a loja. O plugin instalado usa o <c>icon.png</c> que o instalador guardou na
/// pasta dele; o do feed é baixado uma vez para <c>Cache\icons\&lt;id&gt;\&lt;versão&gt;.png</c> e serve offline.
/// Devolve null quando não há ícone: a loja mostra o genérico.
/// </summary>
public sealed class PluginIconCache(HttpClient http, PluginFeed feed, string cacheDirectory)
{
    /// <summary>Ícone maior que isso não é ícone; não baixa.</summary>
    public const int MaxBytes = 1024 * 1024;

    public static string DefaultCacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenWigiDash", "Cache", "icons");

    /// <summary>O <c>icon.png</c> de um plugin instalado, sem rede.</summary>
    public static string? Installed(InstalledPlugin plugin)
    {
        var icon = Path.Combine(plugin.Directory, "icon.png");
        return File.Exists(icon) ? icon : null;
    }

    /// <summary>Ícone de um pacote do feed: cache, iconUrl da busca, ícone embutido no pacote, ou o último em cache.</summary>
    public async Task<string?> GetAsync(FeedPackage package, CancellationToken cancellationToken = default)
    {
        var version = package.CompatibleVersion ?? package.LatestVersion;
        var directory = Path.Combine(cacheDirectory, package.Id.ToLowerInvariant());
        var file = Path.Combine(directory, version.Original.ToLowerInvariant() + ".png");
        if (File.Exists(file))
            return file;

        var urls = new List<string>();
        if (package.IconUrl is { } iconUrl)
            urls.Add(iconUrl);
        try
        {
            urls.Add(await feed.PackageIconUrlAsync(package.Id, version, cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
        }

        foreach (var url in urls)
        {
            try
            {
                if (await DownloadAsync(url, cancellationToken) is { } bytes)
                {
                    Directory.CreateDirectory(directory);
                    await File.WriteAllBytesAsync(file, bytes, cancellationToken);
                    return file;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
            }
        }

        // Offline ou o ícone saiu do feed: o de outra versão já baixada serve.
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.png").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
    }

    private async Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
            return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + read > MaxBytes)
                return null;
            memory.Write(buffer, 0, read);
        }
        var bytes = memory.ToArray();
        return ImageType(bytes) is null ? null : bytes;
    }

    /// <summary>MIME pelo começo do arquivo (PNG ou JPEG, os que o nuget.org aceita); null se não for imagem.</summary>
    public static string? ImageType(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47]) ? "image/png"
        : bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]) ? "image/jpeg"
        : null;
}
