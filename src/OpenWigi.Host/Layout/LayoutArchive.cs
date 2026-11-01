using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenWigi.Host.Layout;

/// <summary>
/// Cópia do layout para guardar fora do OpenWigiDash: um .zip com o <c>layout.json</c> e, em <c>instances/&lt;id&gt;/</c>,
/// as configurações e os arquivos de cada widget do layout, para a restauração trazer os widgets já configurados.
/// </summary>
public static class LayoutArchive
{
    public const string Extension = ".zip";

    private const string LayoutEntry = "layout.json";
    private const string InstancesPrefix = "instances/";
    private const string FilesIndex = "files.json";

    /// <summary>Nome sugerido para a cópia: <c>OpenWigiDash-layout-AAAAMMDD-HHMM.zip</c>.</summary>
    public static string SuggestedFileName(DateTimeOffset now) => $"OpenWigiDash-layout-{now.LocalDateTime:yyyyMMdd-HHmm}{Extension}";

    /// <summary>Grava a cópia em <paramref name="destination"/> (escreve ao lado e troca, como o layout).</summary>
    public static void Export(string destination, DashboardLayout layout, string instancesRoot)
    {
        var temp = destination + ".tmp";
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            WriteText(zip, LayoutEntry, LayoutStore.Serialize(layout));
            foreach (var id in layout.AllPlacements.Select(p => p.InstanceId).Distinct())
            {
                var folder = Path.Combine(instancesRoot, id.ToString("D"));
                if (!Directory.Exists(folder))
                    continue;
                foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                    if (relative.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var name = $"{InstancesPrefix}{id:D}/{relative}";
                    // O índice dos arquivos guarda caminhos absolutos; na cópia vão relativos à pasta da instância.
                    if (relative == FilesIndex)
                        WriteText(zip, name, RebaseFiles(File.ReadAllText(file), path => RelativeIfInside(folder, path)));
                    else
                        zip.CreateEntryFromFile(file, name, CompressionLevel.Optimal);
                }
            }
        }
        File.Move(temp, destination, overwrite: true);
    }

    /// <summary>
    /// Lê o layout de um arquivo para restaurar: a cópia exportada (.zip) ou um <c>layout.json</c> solto (como as cópias
    /// automáticas). Arquivo que não é layout, ou de versão mais nova, dá <see cref="InvalidDataException"/>.
    /// </summary>
    public static DashboardLayout Read(string path)
    {
        if (!IsArchive(path))
            return LayoutStore.Parse(File.ReadAllText(path));
        using var zip = OpenArchive(path);
        var entry = zip.GetEntry(LayoutEntry) ?? throw new InvalidDataException("O .zip não é uma cópia de layout do OpenWigiDash (falta o layout.json).");
        using var reader = new StreamReader(entry.Open());
        return LayoutStore.Parse(reader.ReadToEnd());
    }

    /// <summary>
    /// Põe em <paramref name="instancesRoot"/> as pastas das instâncias que vieram na cópia, trocando as que já existem.
    /// Só as dos ids em <paramref name="instanceIds"/>. Arquivo que não é .zip não tem pastas: não faz nada.
    /// </summary>
    public static void ExtractInstances(string path, string instancesRoot, IEnumerable<Guid> instanceIds)
    {
        if (!IsArchive(path))
            return;
        var wanted = instanceIds.ToHashSet();
        using var zip = OpenArchive(path);
        var byInstance = new Dictionary<Guid, List<(ZipArchiveEntry Entry, string Relative)>>();
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith(InstancesPrefix, StringComparison.Ordinal) || entry.FullName.EndsWith('/'))
                continue;
            var parts = entry.FullName[InstancesPrefix.Length..].Split('/', 2);
            if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "D", out var id) || !wanted.Contains(id))
                continue;
            if (!byInstance.TryGetValue(id, out var list))
                byInstance[id] = list = [];
            list.Add((entry, parts[1]));
        }

        foreach (var group in byInstance)
        {
            var folder = Path.GetFullPath(Path.Combine(instancesRoot, group.Key.ToString("D")));
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
            foreach (var (entry, relative) in group.Value)
            {
                var target = Path.GetFullPath(Path.Combine(folder, relative));
                // Nada sai da pasta da instância, nem com "../" no nome da entrada.
                if (!target.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (relative == FilesIndex)
                {
                    using var reader = new StreamReader(entry.Open());
                    File.WriteAllText(target, RebaseFiles(reader.ReadToEnd(), p => Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(folder, p))));
                }
                else
                    entry.ExtractToFile(target, overwrite: true);
            }
        }
    }

    private static bool IsArchive(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> magic = stackalloc byte[2];
        return stream.Read(magic) == 2 && magic[0] == (byte)'P' && magic[1] == (byte)'K';
    }

    private static ZipArchive OpenArchive(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("O .zip está com defeito.", ex);
        }
    }

    private static void WriteText(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open());
        writer.Write(text);
    }

    private static string RelativeIfInside(string folder, string path)
    {
        if (!Path.IsPathRooted(path))
            return path;
        var relative = Path.GetRelativePath(folder, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative.Replace('\\', '/');
    }

    // files.json: { "id do arquivo": "caminho" }. Índice que não se entende vai como está.
    private static string RebaseFiles(string json, Func<string, string> rebase)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject files)
                return json;
            foreach (var (key, value) in files.ToList())
                if (value is JsonValue v && v.TryGetValue<string>(out var path))
                    files[key] = rebase(path);
            return files.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
