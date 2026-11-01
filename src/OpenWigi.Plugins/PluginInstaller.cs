using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace OpenWigi.Plugins;

/// <summary>Plugin na pasta do usuário. <see cref="PackageId"/> é null quando foi copiado à mão.</summary>
public sealed record InstalledPlugin(string Folder, string Directory, string? PackageId, PackageVersion? Version, bool PendingRemoval, bool PendingUpdate);

/// <summary>Resultado de instalar/atualizar: <see cref="Applied"/> é false quando a troca espera o manager reabrir.</summary>
public sealed record InstallResult(string Id, PackageVersion Version, PackageVersion? Previous, bool Applied);

/// <summary>Falha esperada (pacote inexistente, incompatível, hash errado...), com mensagem para o usuário.</summary>
public sealed class PluginInstallException(string message) : Exception(message);

/// <summary>
/// Instala, atualiza e remove plugins do feed na pasta do usuário (<c>%LOCALAPPDATA%\OpenWigiDash\Widgets</c>).
/// Cada pacote vira <c>Widgets\&lt;id&gt;</c>; a troca passa pela pasta de espera (<see cref="PendingChanges"/>).
/// </summary>
public sealed class PluginInstaller(PluginFeed feed, string widgetsDirectory, string cacheDirectory)
{
    public static string DefaultWidgetsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenWigiDash", "Widgets");

    public static string DefaultCacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenWigiDash", "Cache", "packages");

    public string WidgetsDirectory => widgetsDirectory;

    /// <summary>Instala a versão pedida ou, sem ela, a mais nova compatível. Versão pedida pode estar fora da busca (unlisted).</summary>
    public async Task<InstallResult> InstallAsync(string id, string? version = null, CancellationToken cancellationToken = default)
    {
        var versions = await feed.GetVersionsAsync(id, cancellationToken);
        if (versions.Count == 0)
            throw new PluginInstallException($"Pacote {id} não encontrado no feed.");

        FeedVersion chosen;
        if (version is not null)
        {
            var wanted = PackageVersion.TryParse(version, out var parsed) ? parsed : throw new PluginInstallException($"Versão inválida: {version}");
            chosen = versions.FirstOrDefault(v => v.Version.Equals(wanted))
                ?? throw new PluginInstallException($"{id} não tem a versão {version}.");
            if (!chosen.IsCompatible)
                throw new PluginInstallException($"{id} {chosen.Version} {Requirement(chosen)}");
        }
        else
        {
            chosen = PluginFeed.PickCompatible(versions)
                ?? throw new PluginInstallException($"Nenhuma versão de {id} serve para este OpenWigiDash: a mais nova {Requirement(versions[^1])}");
        }

        var previous = Find(chosen.Id)?.Version;
        var applied = await InstallVersionAsync(chosen, cancellationToken);
        return new InstallResult(chosen.Id, chosen.Version, previous, applied);
    }

    /// <summary>Atualiza um plugin instalado pelo feed. Null quando já está na mais nova compatível.</summary>
    public async Task<InstallResult?> UpdateAsync(string id, CancellationToken cancellationToken = default)
    {
        var installed = Find(id) ?? throw new PluginInstallException($"{id} não está instalado.");
        if (installed.PackageId is null || installed.Version is null)
            throw new PluginInstallException($"{installed.Folder} foi copiado à mão; instale pelo feed para poder atualizar.");
        var latest = PluginFeed.PickCompatible(await feed.GetVersionsAsync(installed.PackageId, cancellationToken));
        if (latest is null || !(latest.Version > installed.Version))
            return null;
        var applied = await InstallVersionAsync(latest, cancellationToken);
        return new InstallResult(latest.Id, latest.Version, installed.Version, applied);
    }

    /// <summary>Marca o plugin para sair e tenta tirar já. Dados e configurações das instâncias ficam.</summary>
    public bool Remove(string id)
    {
        var installed = Find(id) ?? throw new PluginInstallException($"{id} não está instalado.");
        Directory.CreateDirectory(PendingChanges.PendingDirectory(widgetsDirectory));
        var staged = PendingChanges.StagingDirectory(widgetsDirectory, installed.Folder);
        if (Directory.Exists(staged))
            Directory.Delete(staged, recursive: true);
        File.WriteAllText(PendingChanges.RemoveMarker(widgetsDirectory, installed.Folder), "");
        PendingChanges.Apply(widgetsDirectory);
        return !Directory.Exists(installed.Directory);
    }

    /// <summary>Plugins na pasta do usuário, com o que veio do feed e o que está pendente.</summary>
    public IReadOnlyList<InstalledPlugin> List()
    {
        if (!Directory.Exists(widgetsDirectory))
            return [];
        var pending = PendingChanges.List(widgetsDirectory);
        var folders = Directory.GetDirectories(widgetsDirectory)
            .Where(d => !Path.GetFileName(d).StartsWith('.'))
            .Select(d => Read(d, pending))
            .ToList();
        // Instalação nova ainda na espera: aparece com a versão que vai entrar.
        foreach (var (id, _) in pending.Where(p => !p.Remove && !folders.Any(f => f.Folder.Equals(p.Id, StringComparison.OrdinalIgnoreCase))))
        {
            var staged = Read(PendingChanges.StagingDirectory(widgetsDirectory, id), pending);
            folders.Add(staged with { Directory = Path.Combine(widgetsDirectory, id), PendingUpdate = true });
        }
        return folders.OrderBy(f => f.Folder, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public InstalledPlugin? Find(string id) =>
        List().FirstOrDefault(p => id.Equals(p.PackageId, StringComparison.OrdinalIgnoreCase) || id.Equals(p.Folder, StringComparison.OrdinalIgnoreCase));

    private static InstalledPlugin Read(string directory, IReadOnlyList<(string Id, bool Remove)> pending)
    {
        var folder = Path.GetFileName(directory);
        string? packageId = null;
        PackageVersion? version = null;
        var manifest = Path.Combine(directory, "plugin.json");
        if (File.Exists(manifest))
        {
            try
            {
                var package = JsonNode.Parse(File.ReadAllText(manifest))?["package"];
                packageId = (string?)package?["id"];
                PackageVersion.TryParse((string?)package?["version"], out version);
            }
            catch (JsonException)
            {
            }
        }
        var removal = pending.Any(p => p.Remove && p.Id.Equals(folder, StringComparison.OrdinalIgnoreCase));
        var update = pending.Any(p => !p.Remove && p.Id.Equals(folder, StringComparison.OrdinalIgnoreCase));
        return new InstalledPlugin(folder, directory, packageId, version, removal, update);
    }

    private static string Requirement(FeedVersion version) => version.SdkVersion is { } sdk
        ? $"requer OpenWigiDash com SDK {sdk.Major}.{sdk.Minor} (este é {OpenWigi.Sdk.OpenWigiSdk.Version.Major}.{OpenWigi.Sdk.OpenWigiSdk.Version.Minor})."
        : "não depende do SDK do OpenWigiDash, então não é um plugin.";

    private async Task<bool> InstallVersionAsync(FeedVersion version, CancellationToken cancellationToken)
    {
        var lower = version.Id.ToLowerInvariant();
        var file = Path.Combine(cacheDirectory, $"{lower}.{version.Version.Original.ToLowerInvariant()}.nupkg");
        var expected = await feed.GetPackageHashAsync(version, cancellationToken);
        if (!File.Exists(file) || (expected is not null && Hash(file) != expected))
            await feed.DownloadAsync(version, file, cancellationToken);
        if (expected is not null && Hash(file) != expected)
        {
            File.Delete(file);
            throw new PluginInstallException($"O hash de {version.Id} {version.Version} não confere com o do feed; nada foi instalado.");
        }

        var staging = PendingChanges.StagingDirectory(widgetsDirectory, version.Id);
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        var marker = PendingChanges.RemoveMarker(widgetsDirectory, version.Id);
        if (File.Exists(marker))
            File.Delete(marker);
        Directory.CreateDirectory(staging);
        try
        {
            Extract(file, staging, version);
        }
        catch
        {
            Directory.Delete(staging, recursive: true);
            throw;
        }

        PendingChanges.Apply(widgetsDirectory);
        return !Directory.Exists(staging);
    }

    private static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToBase64String(SHA512.HashData(stream));
    }

    // Extrai só plugin/, recusando caminho que saia da pasta (zip slip). Confere o packageType no .nuspec,
    // copia o ícone do pacote para icon.png (a loja usa offline) e marca a origem no plugin.json.
    internal static void Extract(string nupkg, string staging, FeedVersion version)
    {
        using var zip = ZipFile.OpenRead(nupkg);
        var nuspecEntry = zip.Entries.FirstOrDefault(e => !e.FullName.Contains('/') && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            ?? throw new PluginInstallException($"{version.Id} {version.Version} não tem .nuspec.");
        XDocument nuspec;
        using (var stream = nuspecEntry.Open())
            nuspec = XDocument.Load(stream);
        if (!nuspec.Descendants().Any(e => e.Name.LocalName == "packageType" && (string?)e.Attribute("name") == PluginFeed.PackageType))
            throw new PluginInstallException($"{version.Id} não é um plugin do OpenWigiDash (packageType {PluginFeed.PackageType} ausente).");

        var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        var extracted = 0;
        foreach (var entry in zip.Entries)
        {
            var name = Uri.UnescapeDataString(entry.FullName).Replace('\\', '/');
            if (!name.StartsWith("plugin/", StringComparison.OrdinalIgnoreCase) || name.EndsWith('/'))
                continue;
            var target = Path.GetFullPath(Path.Combine(root, name["plugin/".Length..]));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new PluginInstallException($"{version.Id} tem um arquivo fora da pasta do plugin ({entry.FullName}); nada foi instalado.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            extracted++;
        }
        if (extracted == 0)
            throw new PluginInstallException($"{version.Id} {version.Version} não tem a pasta plugin/.");

        var icon = nuspec.Descendants().FirstOrDefault(e => e.Name.LocalName == "icon")?.Value.Trim().Replace('\\', '/');
        var iconTarget = Path.Combine(root, "icon.png");
        if (!string.IsNullOrEmpty(icon) && !File.Exists(iconTarget)
            && zip.Entries.FirstOrDefault(e => Uri.UnescapeDataString(e.FullName).Equals(icon, StringComparison.OrdinalIgnoreCase)) is { } iconEntry)
            iconEntry.ExtractToFile(iconTarget);

        var manifestPath = Path.Combine(root, "plugin.json");
        var manifest = File.Exists(manifestPath) ? JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject ?? [] : [];
        manifest["package"] = new JsonObject { ["id"] = version.Id, ["version"] = version.Version.Original };
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
