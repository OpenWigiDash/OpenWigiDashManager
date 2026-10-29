namespace OpenWigi.Sdk;

public enum FramePixelFormat
{
    /// <summary>4 bytes por pixel, B G R A (o que System.Drawing, SkiaSharp e WPF geram sem conversão).</summary>
    Bgra32,
    /// <summary>2 bytes por pixel, little-endian, o formato nativo da tela. O host manda sem converter.</summary>
    Rgb565,
}

/// <summary>
/// Um quadro do widget, do tamanho do widget, sem dependência de biblioteca de imagem.
/// Depois de entregue ao host o buffer é do host: não reescreva o mesmo array no quadro seguinte.
/// </summary>
public sealed class WidgetFrame
{
    /// <param name="pixels">Linhas de cima para baixo, cada uma com <paramref name="stride"/> bytes.</param>
    /// <param name="dirty">
    /// Área que mudou desde o quadro anterior desta instância. <c>null</c> = o quadro inteiro.
    /// Os pixels fora dela continuam valendo (o quadro é sempre completo); a dica só poupa USB.
    /// </param>
    public WidgetFrame(PixelSize size, FramePixelFormat format, ReadOnlyMemory<byte> pixels,
        int? stride = null, PixelRect? dirty = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size.Width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(size.Height, 1);

        var minStride = size.Width * BytesPerPixel(format);
        var actualStride = stride ?? minStride;
        if (actualStride < minStride)
            throw new ArgumentOutOfRangeException(nameof(stride), $"Stride {actualStride} menor que a linha ({minStride} bytes).");

        var needed = (long)actualStride * (size.Height - 1) + minStride;
        if (pixels.Length < needed)
            throw new ArgumentException($"Buffer com {pixels.Length} bytes; {size} em {format} pede {needed}.", nameof(pixels));

        if (dirty is { } d && (d.IsEmpty || !PixelRect.FromSize(size).Contains(d)))
            throw new ArgumentOutOfRangeException(nameof(dirty), $"Região suja {d} fora de {size} ou vazia.");

        Size = size;
        Format = format;
        Pixels = pixels;
        Stride = actualStride;
        Dirty = dirty;
    }

    public PixelSize Size { get; }
    public FramePixelFormat Format { get; }
    public ReadOnlyMemory<byte> Pixels { get; }
    public int Stride { get; }
    public PixelRect? Dirty { get; }

    /// <summary>Região que o host precisa enviar: a suja, ou o quadro inteiro.</summary>
    public PixelRect ChangedRegion => Dirty ?? PixelRect.FromSize(Size);

    public static int BytesPerPixel(FramePixelFormat format) => format switch
    {
        FramePixelFormat.Bgra32 => 4,
        FramePixelFormat.Rgb565 => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    /// <summary>Quadro de uma cor só (placeholder, "carregando", testes).</summary>
    public static WidgetFrame Solid(PixelSize size, byte r, byte g, byte b)
    {
        var pixels = new byte[size.Width * size.Height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }
        return new WidgetFrame(size, FramePixelFormat.Bgra32, pixels);
    }
}
