using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;

namespace OpenWigi.Ipc;

/// <summary>
/// Mensagens entre o serviço (dono do USB) e o agente de cada sessão. O agente só desenha
/// quando tem a posse; o serviço decide quem tem a posse.
/// </summary>
public abstract record IpcMessage;

// Agente -> serviço

/// <summary>Primeira mensagem do agente. A sessão do Windows não vem daqui: o serviço descobre pelo processo do pipe.</summary>
public sealed record Hello(ushort ProtocolVersion, string UserName) : IpcMessage;

/// <summary>Pixels RGB565 para uma página do hardware; o trecho começa em <paramref name="ByteOffset"/>.</summary>
public sealed record Frame(int Page, int ByteOffset, byte[] Pixels) : IpcMessage;

public sealed record ShowPage(int Page, Transition Transition) : IpcMessage;

public sealed record SetBrightness(int Percent) : IpcMessage;

public sealed record WakeDisplay : IpcMessage;

public sealed record SleepDisplay : IpcMessage;

// Serviço -> agente

public sealed record Welcome(int SessionId, DeviceStatus Device) : IpcMessage;

/// <summary>
/// Mudança de posse. Ao ganhar a posse o agente deve mandar o quadro inteiro das páginas que usa
/// e um <see cref="ShowPage"/>, porque outro agente pode ter desenhado nelas.
/// </summary>
public sealed record Ownership(bool IsOwner, string Reason) : IpcMessage;

public sealed record DeviceStatus(bool Connected, Guid DeviceId, string Firmware) : IpcMessage
{
    public static DeviceStatus Disconnected { get; } = new(false, Guid.Empty, string.Empty);
}

public sealed record Gesture(GestureEvent Event) : IpcMessage;

public sealed record IpcError(string Message) : IpcMessage;
