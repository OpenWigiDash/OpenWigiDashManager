namespace OpenWigi.Device.Protocol;

public enum DeviceMode
{
    Unknown,
    Application,
    Bootloader,
}

/// <summary>O que o firmware reporta do dedo na última leitura.</summary>
public enum TouchContact : byte
{
    None = 0,
    Down = 1,
    Up = 2,
}

/// <summary>Uma leitura do comando de toque: contato, posição em pixels da tela e estado da tela.</summary>
public readonly record struct TouchSample(TouchContact Contact, int X, int Y, int Screen, bool Sleeping);

/// <summary>Animação usada pelo firmware ao trocar de página.</summary>
public enum Transition : byte
{
    WipeEast = 0,
    WipeWest = 1,
    WipeNorth = 2,
    WipeSouth = 3,
    None = 4,
}

public enum ResetMode : ushort
{
    Application = 0,
    Bootloader = 1,
    SystemBootloader = 2,
}

/// <summary>Identificação do aparelho lida na conexão.</summary>
public sealed record DeviceInfo(Guid Id, string Firmware, DeviceMode Mode);
