using System;
using System.IO;
using System.Windows;

namespace OpenWigi.Manager.Services;

/// <summary>
/// Erro que derruba o manager: grava os detalhes num arquivo e avisa a pessoa numa janela, para o programa nunca
/// sumir sem dizer nada.
/// </summary>
public static class ErrorReport
{
    /// <summary>Pasta dos relatórios: <c>%LOCALAPPDATA%\OpenWigiDash\logs</c>.</summary>
    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenWigiDash", "logs");

    /// <summary>Grava <paramref name="error"/> em <c>erro-AAAAMMDD-HHMMSS.txt</c>. Devolve o caminho, ou null se não deu.</summary>
    public static string? Write(Exception error, string when)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = Path.Combine(Directory, $"erro-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path,
                $"OpenWigiDash {typeof(ErrorReport).Assembly.GetName().Version} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                $"{when}{Environment.NewLine}{Environment.NewLine}{error}{Environment.NewLine}");
            return path;
        }
        catch (Exception)
        {
            return null; // sem onde gravar, a janela ainda mostra a mensagem
        }
    }

    /// <summary>Grava o relatório e mostra a janela de erro (bloqueia até a pessoa fechar).</summary>
    public static void Show(Exception error, string when)
    {
        var path = Write(error, when);
        var details = path is null ? "" : $"{Environment.NewLine}{Environment.NewLine}Detalhes em:{Environment.NewLine}{path}";
        try
        {
            MessageBox.Show($"{when}{Environment.NewLine}{Environment.NewLine}{error.Message}{details}",
                "OpenWigiDash", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // Sem interface (ex.: sessão sem vídeo): fica só o arquivo.
        }
    }
}
