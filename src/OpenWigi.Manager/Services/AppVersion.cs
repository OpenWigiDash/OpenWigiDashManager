using System.Reflection;
using OpenWigi.Plugins;

namespace OpenWigi.Manager.Services;

/// <summary>Versão em execução, para o usuário saber qual build está aberta (Ajustes e dica do tray).</summary>
public static class AppVersion
{
    /// <summary>
    /// "0.1.0 (a1b2c3d)": a versão vem da tag na release (Build-Installer passa -p:Version) e o SDK
    /// completa a InformationalVersion com "+commit". Fora do instalador fica 1.0.0, a padrão.
    /// </summary>
    public static string Display { get; } = Format(Informational);

    /// <summary>A versão para comparar com as releases (sem o commit). null se não der para ler.</summary>
    public static PackageVersion? Current { get; } = PackageVersion.TryParse(Informational, out var version) ? version : null;

    private static string? Informational =>
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    internal static string Format(string? informational)
    {
        if (string.IsNullOrEmpty(informational))
            return "?";
        var plus = informational.IndexOf('+');
        if (plus < 0)
            return informational;
        var commit = informational[(plus + 1)..];
        return $"{informational[..plus]} ({(commit.Length > 7 ? commit[..7] : commit)})";
    }
}
