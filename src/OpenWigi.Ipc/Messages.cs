using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;

namespace OpenWigi.Ipc;

/// <summary>
/// Mensagens entre o serviço (dono do USB) e o agente de cada sessão. O agente só desenha
/// quando tem a posse; o serviço decide quem tem a posse.
/// </summary>
public abstract record IpcMessage;

/// <summary>Com que aparelho o serviço fala.</summary>
public enum DeviceKind : byte
{
    /// <summary>O WigiDash de verdade, pelo WinUSB.</summary>
    Usb = 0,

    /// <summary>Um WigiDash de mentira, para quem não tem o aparelho: a tela aparece na prévia.</summary>
    Virtual = 1,
}

// Agente -> serviço

/// <summary>Primeira mensagem do agente. A sessão do Windows não vem daqui: o serviço descobre pelo processo do pipe.</summary>
public sealed record Hello(ushort ProtocolVersion, string UserName) : IpcMessage;

/// <summary>Pixels RGB565 para uma página do hardware; o trecho começa em <paramref name="ByteOffset"/>.</summary>
public sealed record Frame(int Page, int ByteOffset, byte[] Pixels) : IpcMessage;

public sealed record ShowPage(int Page, Transition Transition) : IpcMessage;

public sealed record SetBrightness(int Percent) : IpcMessage;

public sealed record WakeDisplay : IpcMessage;

public sealed record SleepDisplay : IpcMessage;

/// <summary>Liga ou desliga o envio de <see cref="PreviewFrame"/>. Só o dono recebe a prévia.</summary>
public sealed record SubscribePreview(bool Enabled) : IpcMessage;

/// <summary>
/// Toque emulado (ex.: mouse na janela de prévia), em pixels da tela. Entra no mesmo reconhecedor
/// de gestos do toque real: mande Down repetido enquanto segura e um Up ao soltar. Só vale do dono.
/// </summary>
public sealed record EmulatedTouch(TouchContact Contact, int X, int Y) : IpcMessage;

/// <summary>Troca o aparelho ativo (USB ou virtual). Vale para a máquina toda e fica gravado. Só vale do dono.</summary>
public sealed record SelectDevice(DeviceKind Kind) : IpcMessage;

// Serviço -> agente

public sealed record Welcome(int SessionId, DeviceStatus Device) : IpcMessage;

/// <summary>
/// Mudança de posse. Ao ganhar a posse o agente deve mandar o quadro inteiro das páginas que usa
/// e um <see cref="ShowPage"/>, porque outro agente pode ter desenhado nelas.
/// </summary>
public sealed record Ownership(bool IsOwner, string Reason) : IpcMessage;

/// <summary>
/// Estado do aparelho ativo (<paramref name="Kind"/>). <paramref name="UsbPresent"/> diz se o WigiDash de verdade
/// está ligado, mesmo com o virtual ativo, para a UI mostrar as duas opções. <paramref name="Reconnecting"/> diz que
/// o aparelho está no cabo mas a conexão vem caindo e o serviço está tentando de novo (pode estar conectado ou não
/// neste instante): a UI mostra "reconectando" em vez de alternar entre conectado e desconectado.
/// </summary>
public sealed record DeviceStatus(bool Connected, Guid DeviceId, string Firmware, DeviceKind Kind = DeviceKind.Usb,
    bool UsbPresent = false, bool Reconnecting = false) : IpcMessage
{
    public static DeviceStatus Disconnected { get; } = new(false, Guid.Empty, string.Empty);
}

public sealed record Gesture(GestureEvent Event) : IpcMessage;

/// <summary>Brilho atual do aparelho, lido dele. Vai ao agente quando ele ganha a posse.</summary>
public sealed record BrightnessReport(int Percent) : IpcMessage;

/// <summary>Cópia do que está na tela do aparelho: a página mostrada, em RGB565.</summary>
public sealed record PreviewFrame(int Page, byte[] Pixels) : IpcMessage;

public sealed record IpcError(string Message) : IpcMessage;
