namespace OpenWigi.Device.Protocol;

/// <summary>CRC-16/CCITT (polinômio 0x1021, semente 0xFFFF) usado na config gravada no aparelho.</summary>
internal static class Crc16
{
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var value in data)
        {
            var x = (byte)((crc >> 8) ^ value);
            x ^= (byte)(x >> 4);
            crc = (ushort)((crc << 8) ^ (x << 12) ^ (x << 5) ^ x);
        }
        return crc;
    }
}
