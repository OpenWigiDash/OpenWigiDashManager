using System;
using System.Runtime.InteropServices;

namespace OpenWigi.Manager.Services;

/// <summary>
/// Desliga os monitores do PC, a pedido do botão da tela do serviço (sessão bloqueada). Tem que ser daqui, da sessão
/// do usuário no console: o serviço roda na sessão 0, isolada, e o SC_MONITORPOWER mandado de lá não chega aos
/// monitores. Com a sessão bloqueada o manager continua rodando e o Windows aceita o pedido; os monitores voltam
/// sozinhos com o mouse ou o teclado.
/// </summary>
internal static partial class MonitorPower
{
    private const nint HwndBroadcast = 0xFFFF;
    private const uint WmSysCommand = 0x0112;
    private const nint ScMonitorPower = 0xF170;
    private const nint PowerOff = 2;

    /// <summary>Pede o desligamento; false se o Windows recusou a mensagem.</summary>
    public static bool TurnOff() =>
        // PostMessage, não SendMessage: em broadcast, uma janela travada de qualquer programa prenderia o manager.
        PostMessageW(HwndBroadcast, WmSysCommand, ScMonitorPower, PowerOff);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);
}
