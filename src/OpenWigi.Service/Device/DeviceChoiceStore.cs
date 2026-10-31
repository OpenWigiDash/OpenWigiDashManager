using OpenWigi.Ipc;

namespace OpenWigi.Service.Device;

/// <summary>
/// Guarda o aparelho escolhido no manager (USB ou virtual), para valer depois de reiniciar. É da máquina,
/// não de um usuário: o aparelho é um só. Sem escolha gravada vale <c>OpenWigi:Device</c> da configuração.
/// </summary>
public sealed class DeviceChoiceStore(string path, DeviceKind fallback, ILogger<DeviceChoiceStore> logger)
{
    /// <summary>%ProgramData%\OpenWigiDash\Service\device.txt: uma linha, <c>Usb</c> ou <c>Virtual</c>.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OpenWigiDash", "Service", "device.txt");

    public DeviceKind Load()
    {
        try
        {
            if (File.Exists(path)
                && Enum.TryParse<DeviceKind>(File.ReadAllText(path).Trim(), ignoreCase: true, out var kind)
                && Enum.IsDefined(kind))
                return kind;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Não deu para ler o aparelho escolhido em {Path}: {Message}", path, ex.Message);
        }
        return fallback;
    }

    /// <summary>Grava a escolha. Se falhar, ela vale só até o serviço reiniciar.</summary>
    public void Save(DeviceKind kind)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, kind.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Não deu para gravar o aparelho escolhido em {Path}: {Message}", path, ex.Message);
        }
    }
}
