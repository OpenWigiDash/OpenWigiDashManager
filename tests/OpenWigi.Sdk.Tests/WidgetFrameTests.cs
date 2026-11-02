namespace OpenWigi.Sdk.Tests;

public class WidgetFrameTests
{
    private static readonly PixelSize Small = WidgetSize.Small.CellSpan;

    [Fact]
    public void Quadro_sem_regiao_suja_muda_inteiro()
    {
        var frame = new WidgetFrame(Small, FramePixelFormat.Rgb565, new byte[Small.Width * Small.Height * 2]);
        Assert.Equal(Small.Width * 2, frame.Stride);
        Assert.Null(frame.Dirty);
        Assert.Equal(PixelRect.FromSize(Small), frame.ChangedRegion);
    }

    [Fact]
    public void Regiao_suja_vira_a_area_alterada()
    {
        var dirty = new PixelRect(10, 20, 30, 40);
        var frame = new WidgetFrame(Small, FramePixelFormat.Bgra32, new byte[Small.Width * Small.Height * 4], dirty: dirty);
        Assert.Equal(dirty, frame.ChangedRegion);
    }

    [Fact]
    public void Buffer_curto_e_rejeitado()
    {
        Assert.Throws<ArgumentException>(() =>
            new WidgetFrame(Small, FramePixelFormat.Bgra32, new byte[Small.Width * Small.Height * 4 - 1]));
    }

    [Fact]
    public void Stride_maior_aceita_a_ultima_linha_sem_preenchimento()
    {
        var row = Small.Width * 4;
        var stride = row + 32;
        var buffer = new byte[stride * (Small.Height - 1) + row];
        var frame = new WidgetFrame(Small, FramePixelFormat.Bgra32, buffer, stride);
        Assert.Equal(stride, frame.Stride);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WidgetFrame(Small, FramePixelFormat.Bgra32, buffer, stride: row - 1));
    }

    [Theory]
    [InlineData(190, 0, 20, 10)]
    [InlineData(0, 0, 0, 10)]
    [InlineData(-1, 0, 10, 10)]
    public void Regiao_suja_fora_do_quadro_ou_vazia_e_rejeitada(int x, int y, int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WidgetFrame(Small, FramePixelFormat.Bgra32, new byte[Small.Width * Small.Height * 4],
                dirty: new PixelRect(x, y, width, height)));
    }

    [Fact]
    public void Solid_preenche_em_bgra()
    {
        var frame = WidgetFrame.Solid(new PixelSize(2, 1), 0x11, 0x22, 0x33);
        Assert.Equal(new byte[] { 0x33, 0x22, 0x11, 255, 0x33, 0x22, 0x11, 255 }, frame.Pixels.ToArray());
    }

    [Fact]
    public void Cor_ida_e_volta_em_rgb565()
    {
        var color = new WidgetColor(0xF8, 0xFC, 0xF8);
        Assert.Equal(0xFFFF, color.ToRgb565());
        Assert.Equal(color, WidgetColor.FromRgb565(0xFFFF));
        Assert.Equal(0xF800, new WidgetColor(255, 0, 0).ToRgb565());
    }
}
