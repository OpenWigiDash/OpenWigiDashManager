using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenWigi.Updater;

/// <summary>
/// OpenWigiDash.Updater.exe --setup &lt;Setup.exe&gt; --wait &lt;pid do manager&gt; --manager &lt;OpenWigiDashManager.exe&gt; [--log &lt;arquivo&gt;]
///
/// 1. Espera o manager que pediu a atualização sair (ele solta as DLLs dos widgets e o tray).
/// 2. Roda o Setup.exe como administrador (UAC), em /passive: só a barra de progresso.
/// 3. Abre o manager de novo pelo explorer.exe, como o "Abrir" do instalador, para ele rodar como o
///    usuário e não como o processo elevado. Se o usuário recusou o UAC, abre a versão que já estava.
/// </summary>
internal static partial class Program
{
    private const int ErrorCancelled = 1223;    // o usuário disse "Não" no UAC
    private const int SuccessRebootRequired = 3010;
    private const int UserExit = 1602;           // cancelou na janela do instalador

    private static int Main(string[] args)
    {
        var setup = Argument(args, "--setup");
        var manager = Argument(args, "--manager");
        if (setup is null || !File.Exists(setup))
        {
            Show("OpenWigiDash.Updater é aberto pelo OpenWigiDash Manager, em Ajustes, para instalar uma versão nova.", Warning);
            return 2;
        }

        if (int.TryParse(Argument(args, "--wait"), out var pid))
            WaitForExit(pid, TimeSpan.FromSeconds(30));

        var log = Argument(args, "--log");
        var arguments = log is null ? "/passive /norestart" : $"/passive /norestart /log \"{log}\"";
        int code;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(setup, arguments) { UseShellExecute = true, Verb = "runas" })
                ?? throw new InvalidOperationException("O instalador não abriu.");
            process.WaitForExit();
            code = process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            code = UserExit;
        }
        catch (Exception ex)
        {
            Show($"Não foi possível abrir o instalador: {ex.Message}", Error);
            code = -1;
        }

        if (code is not (0 or SuccessRebootRequired or UserExit) && code != -1)
            Show($"A atualização do OpenWigiDash não terminou (código {code}).{(log is null ? "" : $"\n\nLog do instalador: {log}")}", Error);
        else if (code == SuccessRebootRequired)
            Show("O OpenWigiDash foi atualizado. Reinicie o Windows para terminar.", Info);

        if (manager is not null && File.Exists(manager))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{manager}\"") { UseShellExecute = false });
        return code is 0 or SuccessRebootRequired ? 0 : 1;
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void WaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.WaitForExit(timeout);
        }
        catch (ArgumentException)
        {
            // Já saiu.
        }
    }

    private const uint Error = 0x10;
    private const uint Warning = 0x30;
    private const uint Info = 0x40;

    private static void Show(string text, uint icon) => MessageBox(0, text, "OpenWigiDash", icon);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint owner, string text, string caption, uint type);
}
