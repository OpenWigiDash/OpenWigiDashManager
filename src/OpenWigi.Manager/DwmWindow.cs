using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OpenWigi.Manager;

/// <summary>Ajustes das janelas feitos pelo DWM (o compositor do Windows).</summary>
internal static class DwmWindow
{
    // AppbarBackground e AppbarText do OpenWigiDashTheme, em COLORREF (0x00BBGGRR).
    private const int Caption = 0x1B1615; // #15161B
    private const int Text = 0xEDE9E8;    // #E8E9ED

    private const int DwmwaCloak = 13;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    /// <summary>
    /// Barra de título na cor da barra do app (AppbarBackground, escura nos dois temas), em vez da branca
    /// do Windows. No Windows 11 a cor é exata (DWMWA_CAPTION_COLOR); no Windows 10 (20H1 ou mais novo)
    /// vale o modo escuro do sistema, que fica quase igual. Onde um atributo não existe, a chamada só
    /// falha e a barra fica como estava. Aplica já, se a janela tem HWND, ou assim que ela ganhar um.
    /// </summary>
    public static void UseAppTitleBar(Window window)
    {
        if (Handle(window) != IntPtr.Zero)
            ApplyTitleBar(window);
        else
            window.SourceInitialized += (_, _) => ApplyTitleBar(window);
    }

    /// <summary>
    /// Esconde a janela no compositor sem tirá-la da tela para o WPF: ela continua desenhando, mas o
    /// Windows não a mostra. Serve para reabrir só depois de o primeiro quadro estar pronto.
    /// </summary>
    public static void SetCloaked(Window window, bool cloaked)
    {
        var handle = Handle(window);
        if (handle != IntPtr.Zero)
            Set(handle, DwmwaCloak, cloaked ? 1 : 0);
    }

    private static void ApplyTitleBar(Window window)
    {
        var handle = Handle(window);
        if (handle == IntPtr.Zero)
            return;
        Set(handle, DwmwaUseImmersiveDarkMode, 1);
        Set(handle, DwmwaCaptionColor, Caption);
        Set(handle, DwmwaTextColor, Text);
    }

    private static IntPtr Handle(Window window) => new WindowInteropHelper(window).Handle;

    private static void Set(IntPtr handle, int attribute, int value) =>
        _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
