namespace OpenWigi.Sdk;

/// <summary>Retângulo em ponto flutuante, para desenhar ícones (não depende de System.Drawing).</summary>
public readonly record struct IconRect(float X, float Y, float Width, float Height)
{
    public float CenterX => X + Width / 2f;
    public float CenterY => Y + Height / 2f;
}

/// <summary>Onde vai cada glifo de um ícone desenhado no tile.</summary>
/// <param name="Main">Caixa do glifo principal; centralize o desenho nela.</param>
/// <param name="MainSize">Maior lado do desenho principal.</param>
/// <param name="AccentCircle">
/// Círculo do glifo de destaque (só em ícone combinado): pinte com a cor de fundo para recortar o principal e
/// desenhe o destaque centrado nele. <c>null</c> quando o ícone não tem destaque.
/// </param>
/// <param name="AccentSize">Maior lado do desenho de destaque.</param>
public readonly record struct WidgetIconPlacement(IconRect Main, float MainSize, IconRect? AccentCircle, float AccentSize);

/// <summary>
/// A posição dos glifos de um ícone combinado (<see cref="WidgetIcon.Accent"/>), igual à da janela de escolha e à do
/// <see cref="WidgetIconView"/>: o principal um pouco menor e para cima e para a esquerda, o destaque com metade do
/// tamanho num círculo no canto inferior direito.
/// </summary>
/// <remarks>
/// Para o glifo encher a caixa, meça o contorno dele (ex.: <c>GraphicsPath.AddString</c> + <c>GetBounds</c>) e
/// escale até o maior lado dar o tamanho pedido: a caixa da fonte tem muita sobra em volta do desenho.
/// </remarks>
public static class WidgetIconLayout
{
    /// <summary>Escala do glifo principal quando há destaque.</summary>
    public const float MainScale = 0.86f;

    /// <summary>Tamanho do destaque em relação ao ícone inteiro.</summary>
    public const float AccentScale = 0.50f;

    /// <summary>Lado do círculo de recorte em relação ao destaque.</summary>
    public const float AccentCircleScale = 1.24f;

    /// <summary>Posições para um ícone centrado em <paramref name="box"/>, com o maior lado igual a <paramref name="size"/>.</summary>
    public static WidgetIconPlacement Place(WidgetIcon icon, IconRect box, float size) => Place(icon.IsCombined, box, size);

    public static WidgetIconPlacement Place(bool combined, IconRect box, float size)
    {
        if (!combined)
            return new(box, size, null, 0f);

        var main = new IconRect(box.X - size * 0.08f, box.Y - size * 0.06f, box.Width, box.Height);
        var accent = size * AccentScale;
        var cx = box.CenterX + size * 0.30f;
        var cy = box.CenterY + size * 0.26f;
        var half = accent * AccentCircleScale / 2f;
        return new(main, size * MainScale, new IconRect(cx - half, cy - half, half * 2f, half * 2f), accent);
    }
}
