using System.Text.Json;
using OpenWigi.Sdk;

namespace OpenWigi.Host.Instances;

/// <summary>
/// Pasta de uma instância: <c>settings.json</c> e os arquivos dela. Fica no perfil do usuário do
/// Windows, então cada pessoa da casa tem as próprias configurações.
/// </summary>
internal sealed class InstanceStorage : IWidgetSettings, IWidgetFiles
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _directory;
    private readonly string _settingsPath;
    private readonly string _filesPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, Entry>? _settings;

    private sealed record Entry(string Value, bool Cloneable);

    public InstanceStorage(string root, Guid instanceId)
    {
        _directory = Path.Combine(root, instanceId.ToString("D"));
        _settingsPath = Path.Combine(_directory, "settings.json");
        _filesPath = Path.Combine(_directory, "files.json");
    }

    public string Directory => _directory;

    public async ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);
        return settings.TryGetValue(key, out var entry) ? entry.Value : null;
    }

    public async ValueTask SetAsync(string key, string value, bool cloneable = true, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var settings = await LoadUnlockedAsync(cancellationToken);
            settings[key] = new Entry(value, cloneable);
            await WriteAsync(_settingsPath, settings, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var settings = await LoadUnlockedAsync(cancellationToken);
            if (!settings.Remove(key))
                return false;
            await WriteAsync(_settingsPath, settings, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyCollection<string>> GetKeysAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).Keys.ToList();

    public async ValueTask<string> ImportAsync(string fileId, string sourcePath, CancellationToken cancellationToken = default)
    {
        var target = await CreateAsync(fileId, Path.GetFileName(sourcePath), cancellationToken);
        File.Copy(sourcePath, target, overwrite: true);
        return target;
    }

    public async ValueTask<string> CreateAsync(string fileId, string fileName, CancellationToken cancellationToken = default)
    {
        var safeName = string.Concat(Path.GetFileName(fileName).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var folder = Path.Combine(_directory, "files", Hash(fileId));
        System.IO.Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, safeName);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var files = await ReadAsync<Dictionary<string, string>>(_filesPath, cancellationToken) ?? [];
            if (files.TryGetValue(fileId, out var previous) && previous != path && File.Exists(previous))
                File.Delete(previous);
            files[fileId] = path;
            await WriteAsync(_filesPath, files, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
        return path;
    }

    public async ValueTask<string?> GetPathAsync(string fileId, CancellationToken cancellationToken = default)
    {
        var files = await ReadAsync<Dictionary<string, string>>(_filesPath, cancellationToken);
        return files is not null && files.TryGetValue(fileId, out var path) && File.Exists(path) ? path : null;
    }

    public async ValueTask<bool> DeleteAsync(string fileId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var files = await ReadAsync<Dictionary<string, string>>(_filesPath, cancellationToken) ?? [];
            if (!files.Remove(fileId, out var path))
                return false;
            if (File.Exists(path))
                File.Delete(path);
            await WriteAsync(_filesPath, files, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Apaga tudo da instância (o widget foi tirado do layout).</summary>
    public void DeleteAll()
    {
        if (System.IO.Directory.Exists(_directory))
            System.IO.Directory.Delete(_directory, recursive: true);
    }

    private async ValueTask<Dictionary<string, Entry>> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return new Dictionary<string, Entry>(await LoadUnlockedAsync(cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<Dictionary<string, Entry>> LoadUnlockedAsync(CancellationToken cancellationToken) =>
        _settings ??= await ReadAsync<Dictionary<string, Entry>>(_settingsPath, cancellationToken) ?? [];

    private static async ValueTask<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return default;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Json, cancellationToken);
    }

    private async ValueTask WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        System.IO.Directory.CreateDirectory(_directory);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, value, Json, cancellationToken);
        File.Move(temp, path, overwrite: true);
    }

    private static string Hash(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16];
}
