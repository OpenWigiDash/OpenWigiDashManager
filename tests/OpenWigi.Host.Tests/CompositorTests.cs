using System.Buffers.Binary;
using OpenWigi.Device.Protocol;
using OpenWigi.Host.Display;
using OpenWigi.Host.Layout;
using OpenWigi.Sdk;

namespace OpenWigi.Host.Tests;

public class CompositorTests
{
    [Fact]
    public void Blit_converte_bgra_e_posiciona_no_lugar_do_widget()
    {
        var screen = new byte[WigiDashProtocol.FrameBytes];
        var frame = WidgetFrame.Solid(new PixelSize(2, 2), 255, 0, 0);
        Compositor.Blit(screen, new PixelRect(10, 20, 2, 2), frame, PixelRect.FromSize(frame.Size));

        Assert.Equal(0xF800, BinaryPrimitives.ReadUInt16LittleEndian(screen.AsSpan(20 * Compositor.RowBytes + 10 * 2)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(screen.AsSpan(20 * Compositor.RowBytes + 12 * 2)));
    }

    [Fact]
    public void Regiao_suja_limita_o_que_e_copiado()
    {
        var screen = new byte[WigiDashProtocol.FrameBytes];
        var size = new PixelSize(4, 4);
        var full = WidgetFrame.Solid(size, 255, 255, 255);
        var dirty = new WidgetFrame(size, FramePixelFormat.Bgra32, full.Pixels, dirty: new PixelRect(0, 0, 1, 1));
        Compositor.Blit(screen, new PixelRect(0, 0, 4, 4), dirty, dirty.ChangedRegion);

        Assert.Equal(0xFFFF, BinaryPrimitives.ReadUInt16LittleEndian(screen));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(screen.AsSpan(2)));
    }

    [Fact]
    public async Task Flush_manda_so_as_linhas_sujas()
    {
        var sink = new RecordingSink();
        var compositor = new Compositor(sink);
        var bounds = new WidgetPlacement { Column = 1, Row = 2, Size = WidgetSize.Small }.Bounds;

        compositor.Draw(bounds, WidgetFrame.Solid(WidgetSize.Small.PixelSize, 0, 255, 0));
        await compositor.FlushAsync(CancellationToken.None);
        await compositor.FlushAsync(CancellationToken.None); // nada novo: não manda de novo

        var write = Assert.Single(sink.Writes);
        Assert.Equal(bounds.Y * Compositor.RowBytes, write.Offset);
        Assert.Equal(WidgetGrid.CellHeight * Compositor.RowBytes, write.Length);
    }

    [Fact]
    public void Grade_fecha_a_tela_do_aparelho()
    {
        Assert.Equal(new PixelSize(WigiDashProtocol.Width, WigiDashProtocol.Height), WidgetGrid.Screen);
    }

    [Fact]
    public void Layout_acha_espaco_livre_sem_sobrepor()
    {
        var page = new LayoutPage { Widgets = [new WidgetPlacement { Column = 0, Row = 0, Size = WidgetSize.Large }] };
        Assert.Equal((2, 0), page.FindFreeCell(WidgetSize.Wide));
        Assert.Null(new LayoutPage { Widgets = [new WidgetPlacement { Size = WidgetSize.FullScreen }] }.FindFreeCell(WidgetSize.Small));
    }

    private sealed class RecordingSink : IDisplaySink
    {
        public List<(int Page, int Offset, int Length)> Writes { get; } = [];

        public ValueTask WriteAsync(int page, int byteOffset, ReadOnlyMemory<byte> rgb565, CancellationToken cancellationToken)
        {
            Writes.Add((page, byteOffset, rgb565.Length));
            return ValueTask.CompletedTask;
        }

        public ValueTask ShowAsync(int page, Transition transition, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
