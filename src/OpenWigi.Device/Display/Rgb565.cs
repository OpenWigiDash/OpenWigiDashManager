using System.Buffers.Binary;

namespace OpenWigi.Device.Display;

/// <summary>Conversões para o formato de pixel do WigiDash: RGB565 little-endian.</summary>
public static class Rgb565
{
    public static ushort Pack(byte r, byte g, byte b) => (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));

    /// <summary>
    /// Converte pixels BGRA de 32 bits (o layout de <c>Format32bppArgb</c> e de <c>Bgra8888</c>)
    /// para RGB565. <paramref name="stride"/> é o tamanho em bytes de uma linha da origem.
    /// </summary>
    public static void FromBgra32(ReadOnlySpan<byte> source, int width, int height, int stride, Span<byte> destination)
    {
        if (destination.Length < width * height * 2)
            throw new ArgumentException("Destino menor que o quadro.", nameof(destination));

        for (var y = 0; y < height; y++)
        {
            var row = source.Slice(y * stride, width * 4);
            var output = destination.Slice(y * width * 2, width * 2);
            for (var x = 0; x < width; x++)
            {
                var px = row.Slice(x * 4, 4);
                BinaryPrimitives.WriteUInt16LittleEndian(output[(x * 2)..], Pack(px[2], px[1], px[0]));
            }
        }
    }

    public static void Fill(Span<byte> destination, byte r, byte g, byte b)
    {
        var value = Pack(r, g, b);
        for (var i = 0; i + 1 < destination.Length; i += 2)
            BinaryPrimitives.WriteUInt16LittleEndian(destination[i..], value);
    }
}
