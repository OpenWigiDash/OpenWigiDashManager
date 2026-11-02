using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenWigi.Manager.Services;

/// <summary>
/// Diário curto da abertura da janela em <c>%LOCALAPPDATA%\OpenWigiDash\logs\manager.log</c>: em que passo a
/// janela e o WebView2 estavam, com hora e sessão do Windows. Serve para entender uma janela que não abre
/// (vazia, presa na tela de abertura) no perfil de outro usuário, onde não dá para depurar ao vivo.
/// </summary>
public static class StartupLog
{
    private const long MaxSize = 512 * 1024;
    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(ErrorReport.Directory, "manager.log");
    private static bool _started;

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                if (!_started)
                {
                    _started = true;
                    Directory.CreateDirectory(ErrorReport.Directory);
                    // Cada abertura do manager começa um arquivo novo se o de antes cresceu demais.
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxSize)
                        File.Delete(FilePath);
                    File.AppendAllText(FilePath, $"{Environment.NewLine}{Stamp()} OpenWigiDash " +
                        $"{typeof(StartupLog).Assembly.GetName().Version}, sessão {Session()}{Environment.NewLine}");
                }
                File.AppendAllText(FilePath, $"{Stamp()} {message}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // Sem onde gravar o diário, o manager segue igual.
            }
        }
    }

    private static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

    // A sessão deste processo e se é ela que está no console (na frente) agora.
    private static string Session()
    {
        var current = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        var console = WTSGetActiveConsoleSessionId();
        return current == console ? $"{current} (no console)" : $"{current} (console é a {console})";
    }

    [DllImport("kernel32.dll")]
    private static extern int WTSGetActiveConsoleSessionId();
}
