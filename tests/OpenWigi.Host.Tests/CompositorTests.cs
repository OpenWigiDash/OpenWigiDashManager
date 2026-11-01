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

        Assert.Equal(0xF800, Pixel(screen, 10, 20));
        Assert.Equal(0, Pixel(screen, 12, 20));
    }

    [Fact]
    public void Regiao_suja_limita_o_que_e_copiado()
    {
        var screen = new byte[WigiDashProtocol.FrameBytes];
        var size = new PixelSize(4, 4);
        var full = WidgetFrame.Solid(size, 255, 255, 255);
        var dirty = new WidgetFrame(size, FramePixelFormat.Bgra32, full.Pixels, dirty: new PixelRect(0, 0, 1, 1));
        Compositor.Blit(screen, new PixelRect(0, 0, 4, 4), dirty, dirty.ChangedRegion);

        Assert.Equal(0xFFFF, Pixel(screen, 0, 0));
        Assert.Equal(0, Pixel(screen, 1, 0));
    }

    [Fact]
    public async Task Flush_manda_so_as_linhas_sujas()
    {
        var sink = new RecordingSink();
        var compositor = new Compositor(sink);
        var bounds = new WidgetPlacement { Column = 2, Row = 4, Size = WidgetSize.Small }.Bounds;

        compositor.Draw(bounds, WidgetFrame.Solid(WidgetSize.Small.CellSpan, 0, 255, 0));
        await compositor.FlushAsync(CancellationToken.None);
        await compositor.FlushAsync(CancellationToken.None); // nada novo: não manda de novo

        var write = Assert.Single(sink.Writes);
        Assert.Equal(bounds.Y * Compositor.RowBytes, write.Offset);
        Assert.Equal(145 * Compositor.RowBytes, write.Length);
    }

    [Fact]
    public async Task Faixas_longe_uma_da_outra_vao_em_envios_separados()
    {
        var sink = new RecordingSink();
        var compositor = new Compositor(sink);
        var top = WidgetGrid.CellBounds(0, 0, WidgetSize.Mini);
        var bottom = WidgetGrid.CellBounds(9, 7, WidgetSize.Mini);

        compositor.Draw(top, WidgetFrame.Solid(new PixelSize(top.Width, top.Height), 255, 0, 0));
        compositor.Draw(bottom, WidgetFrame.Solid(new PixelSize(bottom.Width, bottom.Height), 255, 0, 0));
        Assert.Equal([(0, 70), (521, 592)], compositor.DirtyBands);
        await compositor.FlushAsync(CancellationToken.None);

        Assert.Equal([(0, 70 * Compositor.RowBytes), (521 * Compositor.RowBytes, 71 * Compositor.RowBytes)],
            sink.Writes.Select(w => (w.Offset, w.Length)));
    }

    [Fact]
    public void Faixas_perto_ou_sobrepostas_viram_uma_so()
    {
        var compositor = new Compositor(new RecordingSink());
        var row0 = WidgetGrid.CellBounds(0, 0, WidgetSize.Mini); // 0..70
        var row1 = WidgetGrid.CellBounds(0, 1, WidgetSize.Mini); // 74..145, 4 linhas depois
        var row4 = WidgetGrid.CellBounds(0, 4, WidgetSize.Mini); // 298..368

        compositor.Draw(row4, WidgetFrame.Solid(new PixelSize(row4.Width, row4.Height), 1, 1, 1));
        compositor.Draw(row0, WidgetFrame.Solid(new PixelSize(row0.Width, row0.Height), 1, 1, 1));
        compositor.Draw(row1, WidgetFrame.Solid(new PixelSize(row1.Width, row1.Height), 1, 1, 1));

        Assert.Equal([(0, 145), (298, 368)], compositor.DirtyBands);
    }

    [Fact]
    public async Task Moldura_pinta_fundo_borda_e_corta_o_canto_do_conteudo()
    {
        var sink = new RecordingSink();
        var compositor = new Compositor(sink);
        var frame = TileFrame.Default;
        var shape = new TileShape(WidgetGrid.CellBounds(2, 2, WidgetSize.Small), frame);
        var content = shape.Content;
        var white = WidgetFrame.Solid(new PixelSize(content.Width, content.Height), 255, 255, 255);

        await compositor.ShowPageAsync(frame.Background, [(shape, white)], Transition.None, CancellationToken.None);
        var screen = sink.LastFullPage!;

        var cell = shape.Cell;
        Assert.Equal(frame.Background.ToRgb565(), Pixel(screen, cell.X, cell.Y));                    // respiro
        Assert.Equal(frame.Background.ToRgb565(), Pixel(screen, cell.X + 3, cell.Y + 3));            // canto de fora cortado
        Assert.Equal(frame.BorderColor.ToRgb565(), Pixel(screen, cell.X + 3, cell.Y + 60));          // borda reta
        Assert.Equal(0xFFFF, Pixel(screen, content.X + content.Width / 2, content.Y + content.Height / 2));
        Assert.Equal(frame.BorderColor.ToRgb565(), Pixel(screen, content.X, content.Y));             // canto do conteúdo
        Assert.Equal(0xFFFF, Pixel(screen, content.X + 20, content.Y));                              // reta do conteúdo

        // Um quadro novo do widget não apaga o arredondado.
        compositor.Draw(shape, white);
        await compositor.FlushAsync(CancellationToken.None);
        Assert.Equal(frame.BorderColor.ToRgb565(), Pixel(sink.Merged(compositor.FrontPage), content.X, content.Y));
    }

    [Fact]
    public async Task Sem_moldura_o_conteudo_ocupa_a_celula_inteira()
    {
        var sink = new RecordingSink();
        var compositor = new Compositor(sink);
        var shape = new TileShape(WidgetGrid.CellBounds(0, 0, WidgetSize.Small), TileFrame.None);
        Assert.Equal(shape.Cell, shape.Content);

        var white = WidgetFrame.Solid(WidgetSize.Small.CellSpan, 255, 255, 255);
        await compositor.ShowPageAsync(TileFrame.None.Background, [(shape, white)], Transition.None, CancellationToken.None);
        Assert.Equal(0xFFFF, Pixel(sink.LastFullPage!, 0, 0));
        Assert.Equal(0xFFFF, Pixel(sink.LastFullPage!, 199, 144));
    }

    [Fact]
    public void Mistura_de_cores_vai_de_uma_a_outra()
    {
        Assert.Equal(0xFFFF, Compositor.Mix(0xFFFF, 0x0000, 1f));
        Assert.Equal(0x0000, Compositor.Mix(0xFFFF, 0x0000, 0f));
        var half = Compositor.Mix(0xF800, 0x0000, 0.5f);
        Assert.InRange(half >> 11, 15, 16);
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
        Assert.Equal((4, 0), page.FindFreeCell(WidgetSize.Wide));
        Assert.Null(new LayoutPage { Widgets = [new WidgetPlacement { Size = WidgetSize.FullScreen }] }.FindFreeCell(WidgetSize.Mini));
    }

    [Fact]
    public void Layout_da_grade_5x4_e_convertido_sem_mudar_nada_de_lugar()
    {
        var dir = Path.Combine(Path.GetTempPath(), "openwigi-layout-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "layout.json");
            File.WriteAllText(path, """
                {
                  "pages": [ { "name": "P", "widgets": [
                    { "column": 1, "row": 2, "size": "2x1" },
                    { "column": 4, "row": 3 }
                  ] } ]
                }
                """);
            var store = new LayoutStore(path);
            Assert.True(store.NeedsMigration());
            var layout = store.Load();

            var widgets = layout.Pages[0].Widgets;
            Assert.Equal((2, 4, WidgetSize.Wide), (widgets[0].Column, widgets[0].Row, widgets[0].Size));
            Assert.Equal(new PixelRect(204, 298, 404, 145), widgets[0].Bounds); // o mesmo lugar da grade 5x4
            Assert.Equal((8, 6, WidgetSize.Small), (widgets[1].Column, widgets[1].Row, widgets[1].Size));
            Assert.Equal(TileFrame.Default, layout.Pages[0].Frame);

            store.Save(layout);
            Assert.False(store.NeedsMigration());
            var again = store.Load();
            Assert.Equal((2, 4), (again.Pages[0].Widgets[0].Column, again.Pages[0].Widgets[0].Row));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Moldura_da_pagina_vai_e_volta_do_layout_json_com_cores_em_hex()
    {
        var dir = Path.Combine(Path.GetTempPath(), "openwigi-layout-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "layout.json");
            var store = new LayoutStore(path);
            var frame = new TileFrame { Spacing = 1, Border = 0, Rounded = false, BorderColor = new(1, 2, 3), Background = new(250, 128, 0) };
            store.Save(new DashboardLayout { Pages = [new LayoutPage { Name = "P", Frame = frame }] });

            var json = File.ReadAllText(path);
            Assert.Contains("\"#FA8000\"", json);
            Assert.Contains("\"grid\": 2", json);
            Assert.DoesNotContain("inset", json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(frame, store.Load().Pages[0].Frame);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static ushort Pixel(byte[] screen, int x, int y) =>
        BinaryPrimitives.ReadUInt16LittleEndian(screen.AsSpan(y * Compositor.RowBytes + x * 2));

    private sealed class RecordingSink : IDisplaySink
    {
        private readonly Dictionary<int, byte[]> _pages = [];

        public List<(int Page, int Offset, int Length)> Writes { get; } = [];
        public byte[]? LastFullPage { get; private set; }

        public ValueTask WriteAsync(int page, int byteOffset, ReadOnlyMemory<byte> rgb565, CancellationToken cancellationToken)
        {
            Writes.Add((page, byteOffset, rgb565.Length));
            if (!_pages.TryGetValue(page, out var buffer))
                _pages[page] = buffer = new byte[WigiDashProtocol.FrameBytes];
            rgb565.Span.CopyTo(buffer.AsSpan(byteOffset));
            if (byteOffset == 0 && rgb565.Length == WigiDashProtocol.FrameBytes)
                LastFullPage = rgb565.ToArray();
            return ValueTask.CompletedTask;
        }

        /// <summary>A página do aparelho com tudo que já foi escrito nela.</summary>
        public byte[] Merged(int page) => _pages[page];

        public ValueTask ShowAsync(int page, Transition transition, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
