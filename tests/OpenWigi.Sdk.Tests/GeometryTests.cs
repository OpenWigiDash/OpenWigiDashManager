namespace OpenWigi.Sdk.Tests;

public class GeometryTests
{
    [Theory]
    [InlineData(1, 1, 200, 145)]
    [InlineData(2, 1, 404, 145)]
    [InlineData(2, 2, 404, 294)]
    [InlineData(5, 4, 1016, 592)]
    public void Tamanho_em_pixels_soma_celulas_e_espacamento(int columns, int rows, int width, int height)
    {
        Assert.Equal(new PixelSize(width, height), new WidgetSize(columns, rows).PixelSize);
    }

    [Fact]
    public void Grade_cheia_fecha_a_tela_do_WigiDash()
    {
        Assert.Equal(new PixelSize(1016, 592), WidgetGrid.Screen);
        Assert.Equal(WidgetGrid.Screen, WidgetSize.FullScreen.PixelSize);
    }

    [Fact]
    public void Origem_da_celula_pula_o_espacamento()
    {
        Assert.Equal((0, 0), WidgetGrid.CellOrigin(0, 0));
        Assert.Equal((204, 149), WidgetGrid.CellOrigin(1, 1));
        Assert.Equal((816, 447), WidgetGrid.CellOrigin(4, 3));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(6, 1)]
    [InlineData(1, 5)]
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
    public void Union_cobre_os_dois_e_ignora_vazio()
    {
        var a = new PixelRect(10, 10, 20, 20);
        var b = new PixelRect(25, 5, 10, 10);
        Assert.Equal(new PixelRect(10, 5, 25, 25), a.Union(b));
        Assert.Equal(a, a.Union(default));
        Assert.Equal(a, default(PixelRect).Union(a));
    }
}
