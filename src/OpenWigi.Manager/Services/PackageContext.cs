using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenWigi.Manager.Services;

/// <summary>
/// Diz se o manager está rodando dentro de um app da Microsoft Store (MSIX), como acontece quando um app desses
/// (o Claude Desktop, por exemplo) abre o manager. Nesse caso o Windows desvia o que ele grava em %APPDATA% e
/// %LOCALAPPDATA% para uma cópia só daquele app, e o manager aberto pelo menu Iniciar não enxerga nada disso:
/// páginas e plugins parecem sumir. A janela avisa (MainLayout).
/// </summary>
public static partial class PackageContext
{
    private const int ErrorInsufficientBuffer = 122;

    /// <summary>Família do pacote em que o manager está (ex.: "Claude_pzs8sxrjxfjjc"), ou null fora de um.</summary>
    public static string? PackageFamilyName { get; } = Read();

    /// <summary>Pasta para onde o Windows desvia o %APPDATA% do manager, ou null fora de um pacote.</summary>
    public static string? RedirectedAppData => PackageFamilyName is { } family
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", family, "LocalCache", "Roaming")
        : null;

    private static unsafe string? Read()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8))
            return null;
        try
        {
            uint length = 0;
            var result = GetCurrentPackageFamilyName(&length, null);
            if (result != ErrorInsufficientBuffer || length == 0) // fora de pacote: APPMODEL_ERROR_NO_PACKAGE
                return null;
            var buffer = new char[length];
            fixed (char* name = buffer)
            {
                if (GetCurrentPackageFamilyName(&length, name) != 0)
                    return null;
            }
            return new string(buffer, 0, (int)length - 1); // o tamanho conta o \0 do fim
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetCurrentPackageFamilyName(uint* packageFamilyNameLength, char* packageFamilyName);
}
