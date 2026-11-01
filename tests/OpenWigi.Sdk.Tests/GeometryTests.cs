namespace OpenWigi.Sdk.Tests;

public class GeometryTests
{
    [Theory]
    [InlineData(1, 1, 98, 70)]
    [InlineData(2, 2, 200, 145)]
    [InlineData(4, 2, 404, 145)]
    [InlineData(4, 4, 404, 294)]
    [InlineData(3, 2, 302, 145)]
    [InlineData(10, 8, 1016, 592)]
    public void Celulas_somam_meias_celulas_e_espacamento(int columns, int rows, int width, int height)
    {
        Assert.Equal(new PixelSize(width, height), new WidgetSize(columns, rows).CellSpan);
    }

    [Fact]
    public void Tamanhos_da_grade_5x4_tem_os_mesmos_pixels()
    {
        Assert.Equal(new PixelSize(200, 145), WidgetSize.Small.CellSpan);
        Assert.Equal(new PixelSize(404, 145), WidgetSize.Wide.CellSpan);
        Assert.Equal(new PixelSize(404, 294), WidgetSize.Large.CellSpan);
        Assert.Equal(new PixelSize(98, 70), WidgetSize.Mini.CellSpan);
    }

    [Fact]
    public void Grade_cheia_fecha_a_tela_do_WigiDash()
    {
        Assert.Equal(new PixelSize(1016, 592), WidgetGrid.Screen);
        Assert.Equal(WidgetGrid.Screen, WidgetSize.FullScreen.CellSpan);
        Assert.Equal(new PixelRect(0, 0, 1016, 592), WidgetGrid.CellBounds(0, 0, WidgetSize.FullScreen));
    }

    [Fact]
    public void Linhas_alternam_70_e_71_e_cada_par_fecha_uma_celula_antiga()
    {
        Assert.Equal(new PixelRect(0, 0, 98, 70), WidgetGrid.CellBounds(0, 0, WidgetSize.Mini));
        Assert.Equal(new PixelRect(102, 74, 98, 71), WidgetGrid.CellBounds(1, 1, WidgetSize.Mini));
        Assert.Equal(new PixelRect(918, 521, 98, 71), WidgetGrid.CellBounds(9, 7, WidgetSize.Mini));
        // Posições pares são as células da grade 5x4.
        for (var c = 0; c < 5; c++)
            for (var r = 0; r < 4; r++)
                Assert.Equal(new PixelRect(c * 204, r * 149, 200, 145), WidgetGrid.CellBounds(c * 2, r * 2, WidgetSize.Small));
    }

    [Fact]
    public void Numero_par_de_linhas_tem_a_mesma_altura_em_qualquer_linha()
    {
        for (var row = 0; row + 2 <= WidgetGrid.Rows; row++)
            Assert.Equal(145, WidgetGrid.CellBounds(0, row, WidgetSize.Small).Height);
        Assert.Equal(70, WidgetGrid.CellBounds(0, 2, new WidgetSize(1, 3)).Height - 149);
        Assert.Equal(71, WidgetGrid.CellBounds(0, 3, new WidgetSize(1, 3)).Height - 149);
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
        Assert.Equal(new PixelSize(190, 135), TileFrame.Default.ContentSize(cell));
        Assert.Equal(cell, TileFrame.None.ContentSize(cell));
        Assert.Equal(new PixelSize(88, 60), TileFrame.Default.ContentSize(WidgetSize.Mini.CellSpan));
        Assert.Equal(new PixelRect(107, 79, 88, 61), TileFrame.Default.ContentBounds(WidgetGrid.CellBounds(1, 1, WidgetSize.Mini)));
    }

    [Fact]
    public void Moldura_so_aceita_os_valores_da_lista()
    {
        Assert.Equal([0, 1, 2, 3, 4, 5], TileFrame.InsetOptions);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileFrame { Spacing = 4 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileFrame { Border = 3 });
        Assert.Throws<ArgumentOutOfRangeException>(() => TileFrame.Default with { Spacing = -1 });
    }

    [Fact]
    public void Canto_arredondado_encolhe_em_tile_pequeno()
    {
        var frame = TileFrame.Default;
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
