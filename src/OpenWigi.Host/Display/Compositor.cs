using System.Buffers.Binary;
using OpenWigi.Device.Display;
using OpenWigi.Device.Protocol;
using OpenWigi.Sdk;

namespace OpenWigi.Host.Display;

/// <summary>Para onde vão os pixels compostos: o serviço (via IPC) ou um falso nos testes.</summary>
public interface IDisplaySink
{
    /// <summary>Escreve um trecho de uma página do hardware, a partir de <paramref name="byteOffset"/>.</summary>
    ValueTask WriteAsync(int page, int byteOffset, ReadOnlyMemory<byte> rgb565, CancellationToken cancellationToken);

    ValueTask ShowAsync(int page, Transition transition, CancellationToken cancellationToken);
}

/// <summary>
/// Monta a tela em RGB565 a partir dos quadros dos widgets e manda só as linhas que mudaram.
/// Usa duas páginas do hardware (0 e 1): a página do layout que está na tela vive numa, e a próxima
/// é montada na outra antes da troca, para a animação mostrar conteúdo pronto. A página 2 é do serviço.
/// </summary>
public sealed class Compositor(IDisplaySink sink)
{
    public const int RowBytes = WigiDashProtocol.Width * 2;
    private static readonly int[] HardwarePages = [0, 1];

    private readonly byte[][] _buffers = [new byte[WigiDashProtocol.FrameBytes], new byte[WigiDashProtocol.FrameBytes]];
    private readonly Lock _gate = new();
    private int _front;           // índice em HardwarePages da página que está na tela
    private int _dirtyTop = int.MaxValue;
    private int _dirtyBottom = -1; // exclusivo

    public WidgetColor Background { get; set; } = new(0, 0, 0);

    /// <summary>Página do hardware que está na tela.</summary>
    public int FrontPage => HardwarePages[_front];

    /// <summary>Copia o quadro do widget para a posição dele na tela e marca as linhas sujas.</summary>
    public void Draw(PixelRect bounds, WidgetFrame frame)
    {
        lock (_gate)
        {
            var region = frame.ChangedRegion;
            Blit(_buffers[_front], bounds, frame, region);
            MarkDirty(bounds.Y + region.Y, bounds.Y + region.Bottom);
        }
    }

    /// <summary>Pinta o fundo numa área (widget removido, espaço vazio).</summary>
    public void Clear(PixelRect bounds)
    {
        lock (_gate)
        {
            Fill(_buffers[_front], bounds, Background);
            MarkDirty(bounds.Y, bounds.Bottom);
        }
    }

    /// <summary>Manda ao aparelho as linhas que mudaram desde o último envio.</summary>
    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> chunk;
        int page, offset;
        lock (_gate)
        {
            if (_dirtyBottom <= _dirtyTop)
                return;
            offset = _dirtyTop * RowBytes;
            chunk = _buffers[_front].AsSpan(offset, (_dirtyBottom - _dirtyTop) * RowBytes).ToArray();
            page = HardwarePages[_front];
            _dirtyTop = int.MaxValue;
            _dirtyBottom = -1;
        }
        await sink.WriteAsync(page, offset, chunk, cancellationToken);
    }

    /// <summary>
    /// Monta uma página inteira na página de trás do hardware, manda e mostra com a animação.
    /// <paramref name="widgets"/> traz a posição e o último quadro de cada widget da página nova.
    /// </summary>
    public async ValueTask ShowPageAsync(IEnumerable<(PixelRect Bounds, WidgetFrame? Frame)> widgets, Transition transition,
        CancellationToken cancellationToken)
    {
        byte[] back;
        int backIndex;
        lock (_gate)
        {
            backIndex = 1 - _front;
            back = _buffers[backIndex];
            Fill(back, new PixelRect(0, 0, WigiDashProtocol.Width, WigiDashProtocol.Height), Background);
            foreach (var (bounds, frame) in widgets)
            {
                if (frame is not null)
                    Blit(back, bounds, frame, PixelRect.FromSize(frame.Size));
            }
        }

        await sink.WriteAsync(HardwarePages[backIndex], 0, back.ToArray(), cancellationToken);
        await sink.ShowAsync(HardwarePages[backIndex], transition, cancellationToken);
        lock (_gate)
        {
            _front = backIndex;
            _dirtyTop = int.MaxValue;
            _dirtyBottom = -1;
        }
    }

    /// <summary>Reenvia a página da frente inteira e mostra (o dono da tela mudou ou o aparelho reconectou).</summary>
    public async ValueTask RepaintAsync(CancellationToken cancellationToken)
    {
        byte[] copy;
        lock (_gate)
        {
            copy = _buffers[_front].ToArray();
            _dirtyTop = int.MaxValue;
            _dirtyBottom = -1;
        }
        await sink.WriteAsync(FrontPage, 0, copy, cancellationToken);
        await sink.ShowAsync(FrontPage, Transition.None, cancellationToken);
    }

    private void MarkDirty(int top, int bottom)
    {
        _dirtyTop = Math.Min(_dirtyTop, Math.Max(0, top));
        _dirtyBottom = Math.Max(_dirtyBottom, Math.Min(WigiDashProtocol.Height, bottom));
    }

    /// <summary>Copia <paramref name="region"/> do quadro para a tela, cortando o que sair dela.</summary>
    internal static void Blit(byte[] screen, PixelRect bounds, WidgetFrame frame, PixelRect region)
    {
        var width = Math.Min(region.Width, Math.Min(frame.Size.Width, bounds.Width) - region.X);
        var height = Math.Min(region.Height, Math.Min(frame.Size.Height, bounds.Height) - region.Y);
        var pixels = frame.Pixels.Span;
        var bpp = WidgetFrame.BytesPerPixel(frame.Format);

        for (var y = 0; y < height; y++)
        {
            var screenY = bounds.Y + region.Y + y;
            if (screenY is < 0 or >= WigiDashProtocol.Height)
                continue;
            var source = pixels.Slice((region.Y + y) * frame.Stride + region.X * bpp);
            for (var x = 0; x < width; x++)
            {
                var screenX = bounds.X + region.X + x;
                if (screenX is < 0 or >= WigiDashProtocol.Width)
                    continue;
                var target = screen.AsSpan(screenY * RowBytes + screenX * 2, 2);
                if (frame.Format == FramePixelFormat.Rgb565)
                    source.Slice(x * 2, 2).CopyTo(target);
                else
                    BinaryPrimitives.WriteUInt16LittleEndian(target, Rgb565.Pack(source[x * 4 + 2], source[x * 4 + 1], source[x * 4]));
            }
        }
    }

    private static void Fill(byte[] screen, PixelRect bounds, WidgetColor color)
    {
        var value = color.ToRgb565();
        for (var y = Math.Max(0, bounds.Y); y < Math.Min(WigiDashProtocol.Height, bounds.Bottom); y++)
            for (var x = Math.Max(0, bounds.X); x < Math.Min(WigiDashProtocol.Width, bounds.Right); x++)
                BinaryPrimitives.WriteUInt16LittleEndian(screen.AsSpan(y * RowBytes + x * 2, 2), value);
    }
}
