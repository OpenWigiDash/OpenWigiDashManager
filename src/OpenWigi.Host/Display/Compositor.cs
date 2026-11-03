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
/// Onde e como um widget aparece na tela: as células que ocupa e a moldura da página. Sem moldura
/// (<see cref="Frame"/> null, como em tela cheia) o conteúdo ocupa as células inteiras.
/// </summary>
public readonly record struct TileShape(PixelRect Cell, TileFrame? Frame = null)
{
    /// <summary>A célula menos o respiro: o que a borda contorna.</summary>
    public PixelRect Tile => Frame is { Spacing: var s and > 0 }
        ? new PixelRect(Cell.X + s, Cell.Y + s, Cell.Width - 2 * s, Cell.Height - 2 * s)
        : Cell;

    /// <summary>Área de conteúdo: o quadro do widget vai aqui.</summary>
    public PixelRect Content => Frame is { } frame ? frame.ContentBounds(Cell) : Cell;

    public int OuterRadius => Frame?.OuterRadius(new PixelSize(Tile.Width, Tile.Height)) ?? 0;

    /// <summary>Raio do canto do conteúdo: o de fora menos a borda, para a borda ter largura igual na curva.</summary>
    public int InnerRadius => Math.Max(0, OuterRadius - (Frame?.Border ?? 0));

    /// <summary>Cor que aparece no canto cortado do conteúdo: a da borda, ou o fundo se não há borda.</summary>
    public WidgetColor InnerOutside => Frame is { } frame && frame.Border > 0 ? frame.BorderColor : Frame?.Background ?? default;
}

/// <summary>
/// Monta a tela em RGB565 a partir dos quadros dos widgets e manda só as linhas que mudaram.
/// Usa duas páginas do hardware (0 e 1): a página do layout que está na tela vive numa, e a próxima
/// é montada na outra antes da troca, para a animação mostrar conteúdo pronto. A página 2 é do serviço.
/// </summary>
/// <remarks>
/// A moldura (fundo, borda e cantos) é pintada ao montar a página. O quadro de um widget só cobre a área
/// de conteúdo, e depois de copiado os cantos dele são cortados de novo, então atualização parcial não
/// apaga o arredondado.
/// </remarks>
public sealed class Compositor(IDisplaySink sink)
{
    public const int RowBytes = WigiDashProtocol.Width * 2;

    /// <summary>Faixas sujas separadas por menos linhas que isto vão num envio só (cada envio tem custo fixo).</summary>
    public const int MergeGap = 8;

    private static readonly int[] HardwarePages = [0, 1];
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, float[]> Coverages = new();

    private readonly byte[][] _buffers = [new byte[WigiDashProtocol.FrameBytes], new byte[WigiDashProtocol.FrameBytes]];
    private readonly Lock _gate = new();
    private readonly List<(int Top, int Bottom)> _dirty = []; // ordenadas, sem sobreposição; Bottom exclusivo
    private int _front;           // índice em HardwarePages da página que está na tela

    // Desenhos que chegam enquanto uma página inteira está a caminho do aparelho: vão para a página da
    // frente (que está saindo) e são refeitos na nova quando ela entra, senão se perdem até o próximo quadro.
    private List<Func<byte[], PixelRect>>? _lateDraws;

    /// <summary>Fundo da página que está na tela: o que <see cref="Clear"/> pinta.</summary>
    public WidgetColor Background { get; set; } = new(0, 0, 0);

    /// <summary>Página do hardware que está na tela.</summary>
    public int FrontPage => HardwarePages[_front];

    /// <summary>Faixas de linhas que ainda não foram enviadas (para os testes).</summary>
    internal IReadOnlyList<(int Top, int Bottom)> DirtyBands
    {
        get
        {
            lock (_gate)
                return _dirty.ToArray();
        }
    }

    /// <summary>Copia o quadro do widget para a área de conteúdo dele e marca as linhas sujas.</summary>
    public void Draw(TileShape shape, WidgetFrame frame)
    {
        lock (_gate)
        {
            var changed = DrawOn(_buffers[_front], shape, frame);
            MarkDirty(changed.Y, changed.Bottom);
            _lateDraws?.Add(screen => DrawOn(screen, shape, frame));
        }
    }

    private static PixelRect DrawOn(byte[] screen, TileShape shape, WidgetFrame frame)
    {
        var content = shape.Content;
        var region = frame.ChangedRegion;
        Blit(screen, content, frame, region);
        var changed = new PixelRect(content.X + region.X, content.Y + region.Y, region.Width, region.Height);
        RoundCorners(screen, content, shape.InnerRadius, shape.InnerOutside, changed);
        return changed;
    }

    /// <summary>Copia o quadro para um retângulo sem moldura.</summary>
    public void Draw(PixelRect bounds, WidgetFrame frame) => Draw(new TileShape(bounds), frame);

    /// <summary>Pinta o fundo numa área (widget removido, espaço vazio).</summary>
    public void Clear(PixelRect bounds)
    {
        lock (_gate)
        {
            Fill(_buffers[_front], bounds, Background);
            MarkDirty(bounds.Y, bounds.Bottom);
            _lateDraws?.Add(screen =>
            {
                Fill(screen, bounds, Background);
                return bounds;
            });
        }
    }

    /// <summary>
    /// Manda ao aparelho as linhas que mudaram desde o último envio: uma escrita por faixa, então dois
    /// widgets longe um do outro não levam junto as linhas do meio.
    /// </summary>
    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        List<(int Offset, byte[] Chunk)> chunks;
        int page;
        lock (_gate)
        {
            if (_dirty.Count == 0)
                return;
            var front = _buffers[_front];
            chunks = _dirty.Select(b => (b.Top * RowBytes, front.AsSpan(b.Top * RowBytes, (b.Bottom - b.Top) * RowBytes).ToArray())).ToList();
            page = HardwarePages[_front];
            _dirty.Clear();
        }
        foreach (var (offset, chunk) in chunks)
            await sink.WriteAsync(page, offset, chunk, cancellationToken);
    }

    /// <summary>
    /// Monta uma página inteira na página de trás do hardware, manda e mostra com a animação.
    /// <paramref name="widgets"/> traz o lugar e o último quadro de cada widget da página nova.
    /// </summary>
    public async ValueTask ShowPageAsync(WidgetColor background, IEnumerable<(TileShape Shape, WidgetFrame? Frame)> widgets,
        Transition transition, CancellationToken cancellationToken)
    {
        byte[] back;
        int backIndex;
        lock (_gate)
        {
            backIndex = 1 - _front;
            back = _buffers[backIndex];
            Fill(back, new PixelRect(0, 0, WigiDashProtocol.Width, WigiDashProtocol.Height), background);
            foreach (var (shape, frame) in widgets)
                PaintTile(back, shape, frame);
            _lateDraws = [];
        }

        try
        {
            await sink.WriteAsync(HardwarePages[backIndex], 0, back.ToArray(), cancellationToken);
            await sink.ShowAsync(HardwarePages[backIndex], transition, cancellationToken);
            lock (_gate)
            {
                _front = backIndex;
                Background = background;
                _dirty.Clear();
                // O que mudou durante o envio ainda não está na página nova: refaz por cima, na ordem em
                // que chegou, e marca para o próximo FlushAsync mandar.
                foreach (var draw in _lateDraws!)
                {
                    var changed = draw(back);
                    MarkDirty(changed.Y, changed.Bottom);
                }
            }
        }
        finally
        {
            lock (_gate)
                _lateDraws = null;
        }
    }

    /// <summary>Página sem moldura (fundo preto), como antes: para quem só tem retângulos.</summary>
    public ValueTask ShowPageAsync(IEnumerable<(PixelRect Bounds, WidgetFrame? Frame)> widgets, Transition transition,
        CancellationToken cancellationToken) =>
        ShowPageAsync(new WidgetColor(0, 0, 0), widgets.Select(w => (new TileShape(w.Bounds), w.Frame)), transition, cancellationToken);

    /// <summary>Reenvia a página da frente inteira e mostra (o dono da tela mudou ou o aparelho reconectou).</summary>
    public async ValueTask RepaintAsync(CancellationToken cancellationToken)
    {
        byte[] copy;
        lock (_gate)
        {
            copy = _buffers[_front].ToArray();
            _dirty.Clear();
        }
        await sink.WriteAsync(FrontPage, 0, copy, cancellationToken);
        await sink.ShowAsync(FrontPage, Transition.None, cancellationToken);
    }

    private void MarkDirty(int top, int bottom)
    {
        top = Math.Max(0, top);
        bottom = Math.Min(WigiDashProtocol.Height, bottom);
        if (bottom <= top)
            return;
        var index = 0;
        while (index < _dirty.Count && _dirty[index].Bottom + MergeGap <= top)
            index++;
        // Junta com todas as faixas que encostam (ou quase) na nova.
        while (index < _dirty.Count && _dirty[index].Top <= bottom + MergeGap - 1)
        {
            top = Math.Min(top, _dirty[index].Top);
            bottom = Math.Max(bottom, _dirty[index].Bottom);
            _dirty.RemoveAt(index);
        }
        _dirty.Insert(index, (top, bottom));
    }

    // Fundo já pintado: borda (com o canto de fora suavizado contra o fundo), conteúdo, e o canto do
    // conteúdo suavizado contra a borda.
    private static void PaintTile(byte[] screen, TileShape shape, WidgetFrame? frame)
    {
        var content = shape.Content;
        if (shape.Frame is { } tileFrame)
        {
            var tile = shape.Tile;
            if (tileFrame.Border > 0)
                Fill(screen, tile, tileFrame.BorderColor);
            RoundCorners(screen, tile, shape.OuterRadius, tileFrame.Background, tile);
        }
        if (frame is null)
            return;
        Blit(screen, content, frame, PixelRect.FromSize(frame.Size));
        RoundCorners(screen, content, shape.InnerRadius, shape.InnerOutside, content);
    }

    /// <summary>
    /// Corta os cantos de <paramref name="rect"/> num arco de raio <paramref name="radius"/>, misturando a
    /// borda do arco com <paramref name="outside"/> conforme a fração do pixel que fica fora. Só mexe nos
    /// pixels dentro de <paramref name="clip"/>.
    /// </summary>
    internal static void RoundCorners(byte[] screen, PixelRect rect, int radius, WidgetColor outside, PixelRect clip)
    {
        radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        if (radius <= 0)
            return;
        var coverage = Coverages.GetOrAdd(radius, CornerCoverage);
        var color = outside.ToRgb565();
        for (var j = 0; j < radius; j++)
            for (var i = 0; i < radius; i++)
            {
                var inside = coverage[j * radius + i];
                if (inside >= 1f)
                    continue;
                BlendAt(screen, rect.X + i, rect.Y + j, color, inside, clip);
                BlendAt(screen, rect.Right - 1 - i, rect.Y + j, color, inside, clip);
                BlendAt(screen, rect.X + i, rect.Bottom - 1 - j, color, inside, clip);
                BlendAt(screen, rect.Right - 1 - i, rect.Bottom - 1 - j, color, inside, clip);
            }
    }

    // Fração de cada pixel do canto de cima à esquerda que fica dentro do arco (4x4 amostras por pixel).
    private static float[] CornerCoverage(int radius)
    {
        const int samples = 4;
        var table = new float[radius * radius];
        var r2 = (double)radius * radius;
        for (var j = 0; j < radius; j++)
            for (var i = 0; i < radius; i++)
            {
                var hits = 0;
                for (var sy = 0; sy < samples; sy++)
                    for (var sx = 0; sx < samples; sx++)
                    {
                        var dx = radius - (i + (sx + 0.5) / samples);
                        var dy = radius - (j + (sy + 0.5) / samples);
                        if (dx * dx + dy * dy <= r2)
                            hits++;
                    }
                table[j * radius + i] = hits / (float)(samples * samples);
            }
        return table;
    }

    private static void BlendAt(byte[] screen, int x, int y, ushort outside, float inside, PixelRect clip)
    {
        if (!clip.Contains(x, y) || x is < 0 or >= WigiDashProtocol.Width || y is < 0 or >= WigiDashProtocol.Height)
            return;
        var span = screen.AsSpan(y * RowBytes + x * 2, 2);
        var pixel = BinaryPrimitives.ReadUInt16LittleEndian(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span, Mix(pixel, outside, inside));
    }

    /// <summary>Mistura duas cores RGB565: <paramref name="amount"/> de <paramref name="a"/> e o resto de <paramref name="b"/>.</summary>
    internal static ushort Mix(ushort a, ushort b, float amount)
    {
        int Channel(int shift, int mask) =>
            (int)MathF.Round(((a >> shift) & mask) * amount + ((b >> shift) & mask) * (1 - amount));
        return (ushort)((Channel(11, 0x1F) << 11) | (Channel(5, 0x3F) << 5) | Channel(0, 0x1F));
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
