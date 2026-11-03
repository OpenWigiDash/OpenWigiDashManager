using System.Text.Json;
using OpenWigi.Ipc;

namespace OpenWigi.Service.Device;

/// <summary>
/// Guarda os ajustes da tela do serviço (ex.: mostrar ou não as sessões abertas), feitos no manager. São da
/// máquina, como o aparelho escolhido. Sem arquivo, ou com <paramref name="path"/> null (testes), vale o padrão.
/// </summary>
public sealed class ServiceScreenSettingsStore(string? path, ILogger<ServiceScreenSettingsStore> logger)
{
    /// <summary>%ProgramData%\OpenWigiDash\Service\service-screen.json.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OpenWigiDash", "Service", "service-screen.json");

    public static ServiceScreenSettings Default { get; } = new(ShowSessions: true);

    public ServiceScreenSettings Load()
    {
        if (path is null)
            return Default;
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(path)) is { } stored)
                return new ServiceScreenSettings(stored.ShowSessions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning("Não deu para ler os ajustes da tela do serviço em {Path}: {Message}", path, ex.Message);
        }
        return Default;
    }

    /// <summary>Grava os ajustes. Se falhar, eles valem só até o serviço reiniciar.</summary>
    public void Save(ServiceScreenSettings settings)
    {
        if (path is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new Stored(settings.ShowSessions)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Não deu para gravar os ajustes da tela do serviço em {Path}: {Message}", path, ex.Message);
        }
    }

    private sealed record Stored(bool ShowSessions);
}
