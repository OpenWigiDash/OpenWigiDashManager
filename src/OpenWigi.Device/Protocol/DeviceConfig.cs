using System.Buffers.Binary;
using System.Text;

namespace OpenWigi.Device.Protocol;

/// <summary>
/// Configuração persistida no aparelho (layout v1, 48 bytes). Campos que não conhecemos
/// a fundo (VCOM, AVDD, versão) são preservados como vieram.
/// </summary>
public sealed record DeviceConfig
{
    internal const int Size = 48;
    private const int NicknameOffset = 8;
    private const int NicknameLength = 32;
    private const int CrcOffset = 46;

    public byte Version { get; init; }
    public byte DefaultBrightness { get; init; }
    public uint ScreenTimeoutSeconds { get; init; }
    public string Nickname { get; init; } = string.Empty;
    public ushort Vcom { get; init; }
    public ushort Avdd { get; init; }
    public byte DisplayOffsetX { get; init; }
    public byte DisplayOffsetY { get; init; }

    /// <summary>Bytes como vieram do aparelho; a gravação parte deles para não mexer no que não entendemos.</summary>
    internal byte[]? Original { get; init; }

    internal static DeviceConfig Parse(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < Size)
            throw new InvalidDataException($"Config com {raw.Length} bytes, esperado {Size}.");

        var name = raw.Slice(NicknameOffset, NicknameLength);
        var end = name.IndexOf((byte)0);
        return new DeviceConfig
        {
            Version = raw[0],
            DefaultBrightness = raw[1],
            ScreenTimeoutSeconds = BinaryPrimitives.ReadUInt32LittleEndian(raw[4..]),
            Nickname = Encoding.ASCII.GetString(end >= 0 ? name[..end] : name),
            Vcom = BinaryPrimitives.ReadUInt16LittleEndian(raw[40..]),
            Avdd = BinaryPrimitives.ReadUInt16LittleEndian(raw[42..]),
            DisplayOffsetX = raw[44],
            DisplayOffsetY = raw[45],
            Original = raw[..Size].ToArray(),
        };
    }

    internal byte[] Serialize()
    {
        var raw = Original is { Length: Size } ? (byte[])Original.Clone() : new byte[Size];
        raw[0] = Version;
        raw[1] = Math.Min(DefaultBrightness, (byte)100);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), ScreenTimeoutSeconds);

        // Apelido: ASCII, até 31 caracteres mais o terminador nulo.
        var nickname = Encoding.ASCII.GetBytes(Nickname);
        raw.AsSpan(NicknameOffset, NicknameLength).Clear();
        nickname.AsSpan(0, Math.Min(nickname.Length, NicknameLength - 1)).CopyTo(raw.AsSpan(NicknameOffset));

        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(40), Vcom);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(42), Avdd);
        raw[44] = DisplayOffsetX;
        raw[45] = DisplayOffsetY;
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(CrcOffset), Crc16.Compute(raw.AsSpan(0, CrcOffset)));
        return raw;
    }
}
