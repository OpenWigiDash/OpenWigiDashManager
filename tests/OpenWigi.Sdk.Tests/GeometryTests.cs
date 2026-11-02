namespace OpenWigi.Sdk.Tests;

public class GeometryTests
{
    [Theory]
    [InlineData(1, 1, 97, 69)]
    [InlineData(2, 2, 198, 143)]
    [InlineData(4, 2, 400, 143)]
    [InlineData(4, 4, 400, 290)]
    [InlineData(3, 2, 299, 143)]
    [InlineData(10, 8, 1006, 584)]
    public void Celulas_somam_meias_celulas_e_espacamento(int columns, int rows, int width, int height)
    {
        Assert.Equal(new PixelSize(width, height), new WidgetSize(columns, rows).CellSpan);
    }

    [Fact]
    public void Tamanhos_com_nome()
    {
        Assert.Equal(new PixelSize(198, 143), WidgetSize.Small.CellSpan);
        Assert.Equal(new PixelSize(400, 143), WidgetSize.Wide.CellSpan);
        Assert.Equal(new PixelSize(400, 290), WidgetSize.Large.CellSpan);
        Assert.Equal(new PixelSize(97, 69), WidgetSize.Mini.CellSpan);
    }

    [Fact]
    public void Grade_cheia_deixa_nas_bordas_a_mesma_folga_que_entre_as_celulas()
    {
        Assert.Equal(new PixelSize(1016, 592), WidgetGrid.Screen);
        var full = WidgetGrid.CellBounds(0, 0, WidgetSize.FullScreen);
        Assert.Equal(WidgetGrid.Area, full);
        Assert.Equal(new PixelRect(5, 4, 1006, 584), full);
        // 5 px nas laterais e 4 px em cima e embaixo: o espaço entre duas células é 4.
        Assert.Equal(WidgetGrid.Screen.Width - full.Right, full.X);
        Assert.Equal(WidgetGrid.Screen.Height - full.Bottom, full.Y);
        Assert.Equal(WidgetGrid.Gap, WidgetGrid.CellBounds(1, 0, WidgetSize.Mini).X - WidgetGrid.CellBounds(0, 0, WidgetSize.Mini).Right);
        Assert.Equal(WidgetGrid.Gap, WidgetGrid.CellBounds(0, 1, WidgetSize.Mini).Y - WidgetGrid.CellBounds(0, 0, WidgetSize.Mini).Bottom);
        Assert.Equal(WidgetGrid.Gap, full.Y);
    }

    [Fact]
    public void Linhas_alternam_69_e_70()
    {
        Assert.Equal(new PixelRect(5, 4, 97, 69), WidgetGrid.CellBounds(0, 0, WidgetSize.Mini));
        Assert.Equal(new PixelRect(106, 77, 97, 70), WidgetGrid.CellBounds(1, 1, WidgetSize.Mini));
        Assert.Equal(new PixelRect(914, 518, 97, 70), WidgetGrid.CellBounds(9, 7, WidgetSize.Mini));
        for (var c = 0; c < 5; c++)
            for (var r = 0; r < 4; r++)
                Assert.Equal(new PixelRect(5 + c * 202, 4 + r * 147, 198, 143), WidgetGrid.CellBounds(c * 2, r * 2, WidgetSize.Small));
    }

    [Fact]
    public void Numero_par_de_linhas_tem_a_mesma_altura_em_qualquer_linha()
    {
        for (var row = 0; row + 2 <= WidgetGrid.Rows; row++)
            Assert.Equal(143, WidgetGrid.CellBounds(0, row, WidgetSize.Small).Height);
        Assert.Equal(69, WidgetGrid.CellBounds(0, 2, new WidgetSize(1, 3)).Height - 147);
        Assert.Equal(70, WidgetGrid.CellBounds(0, 3, new WidgetSize(1, 3)).Height - 147);
    }

    [Fact]
    public void Celula_fora_da_grade_e_rejeitada()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WidgetGrid.CellBounds(9, 0, WidgetSize.Small));
        Assert.Throws<ArgumentOutOfRangeException>(() => WidgetGrid.CellBounds(0, 7, WidgetSize.Small));
        Assert.Throws<ArgumentOutOfRangeException>(() => WidgetGrid.CellBounds(-1, 0, WidgetSize.Mini));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(11, 1)]
    [InlineData(1, 9)]
    public void Tamanho_fora_da_grade_e_rejeitado(int columns, int rows)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WidgetSize(columns, rows));
    }

    [Fact]
    public void Parse_le_o_formato_do_ToString()
    {
        var size = new WidgetSize(3, 2);
        Assert.Equal(size, WidgetSize.Parse(size.ToString()));
        Assert.Equal(size, WidgetSize.Parse("3X2"));
        Assert.Throws<FormatException>(() => WidgetSize.Parse("3by2"));
    }

    [Fact]
    public void Moldura_tira_respiro_e_borda_de_cada_lado()
    {
        var cell = WidgetSize.Small.CellSpan;
        Assert.Equal(cell, TileFrame.Default.ContentSize(cell)); // padrão: igual a antes da moldura
        Assert.Equal(new PixelSize(190, 135), TileFrame.Framed.ContentSize(cell));
        Assert.Equal(cell, TileFrame.None.ContentSize(cell));
        Assert.Equal(new PixelSize(88, 60), TileFrame.Framed.ContentSize(WidgetSize.Mini.CellSpan));
        Assert.Equal(new PixelRect(107, 79, 88, 61), TileFrame.Framed.ContentBounds(WidgetGrid.CellBounds(1, 1, WidgetSize.Mini)));
    }

    [Fact]
    public void Moldura_so_aceita_os_valores_da_lista()
    {
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], TileFrame.InsetOptions);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileFrame { Spacing = 4 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileFrame { Border = 5 });
        Assert.Throws<ArgumentOutOfRangeException>(() => TileFrame.Default with { Spacing = -1 });
    }

    [Fact]
    public void Canto_arredondado_encolhe_em_tile_pequeno()
    {
        var frame = TileFrame.Framed;
        Assert.Equal(12, frame.OuterRadius(new PixelSize(194, 139)));
        Assert.Equal(12, frame.OuterRadius(new PixelSize(92, 64))); // botão 1x1: 64 / 4 = 16, fica 12
        Assert.Equal(0, (frame with { Rounded = false }).OuterRadius(new PixelSize(194, 139)));
        Assert.Equal(5, frame.OuterRadius(new PixelSize(40, 20)));
    }

    [Fact]
    public void Union_cobre_os_dois_e_ignora_vazio()
    {
        var a = new PixelRect(10, 10, 20, 20);
        var b = new PixelRect(25, 5, 10, 10);
        Assert.Equal(new PixelRect(10, 5, 25, 25), a.Union(b));
        Assert.Equal(a, a.Union(default));
        Assert.Equal(a, default(PixelRect).Union(a));
    }
}
