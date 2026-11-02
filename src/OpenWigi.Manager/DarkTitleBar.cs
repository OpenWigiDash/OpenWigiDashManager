using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OpenWigi.Manager;

/// <summary>
/// Barra de título das janelas na cor da barra do app (AppbarBackground do OpenWigiDashTheme, escura nos
/// dois temas), em vez da branca do Windows. No Windows 11 a cor é exata (DWMWA_CAPTION_COLOR); no
/// Windows 10 (20H1 ou mais novo) vale o modo escuro do sistema, que fica quase igual. Onde um atributo
/// não existe, a chamada só falha e a barra fica como estava.
/// </summary>
internal static class DarkTitleBar
{
    // AppbarBackground e AppbarText do OpenWigiDashTheme, em COLORREF (0x00BBGGRR).
    private const int Caption = 0x1B1615; // #15161B
    private const int Text = 0xEDE9E8;    // #E8E9ED

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    /// <summary>Aplica já, se a janela tem HWND, ou assim que ela ganhar um.</summary>
    public static void Attach(Window window)
    {
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Apply(window);
        else
            window.SourceInitialized += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;
        Set(handle, DwmwaUseImmersiveDarkMode, 1);
        Set(handle, DwmwaCaptionColor, Caption);
        Set(handle, DwmwaTextColor, Text);
    }

    private static void Set(IntPtr handle, int attribute, int value) =>
        _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
