namespace OpenWigi.Device.Protocol;

/// <summary>Valores de bRequest do firmware em modo aplicação. Ver docs/protocol.md.</summary>
internal static class Commands
{
    public const byte Mode = 0x00;
    public const byte Firmware = 0x04;
    public const byte Uid = 0x05;
    public const byte Reset = 0x06;
    public const byte ConfigGet = 0x10;
    public const byte ConfigSet = 0x11;
    public const byte Wake = 0x12;
    public const byte Sleep = 0x13;
    public const byte ConfigStore = 0x14;
    public const byte ConfigReset = 0x15;
    public const byte TouchState = 0x33;
    public const byte BrightnessGet = 0x50;
    public const byte BrightnessSet = 0x51;
    public const byte RegionWrite = 0x61;
    public const byte RegionWriteAbort = 0x63;
    public const byte Screen = 0x70;
    public const byte RegionAdd = 0x91;
    public const byte RegionRemove = 0x92;
}
