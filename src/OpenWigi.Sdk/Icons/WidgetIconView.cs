using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace OpenWigi.Sdk;

/// <summary>
/// Mostra um ícone da biblioteca na tela de configurações, com o destaque no canto quando é combinado. Na cor do
/// texto em volta. Ex.: <c>&lt;WidgetIconView IconKey="@key" Size="28" /&gt;</c>.
/// </summary>
public sealed class WidgetIconView : ComponentBase
{
    /// <summary>O ícone. Tem preferência sobre <see cref="IconKey"/>.</summary>
    [Parameter] public WidgetIcon? Icon { get; set; }

    /// <summary>Chave na <see cref="WidgetIcons"/>; chave desconhecida não mostra nada.</summary>
    [Parameter] public string? IconKey { get; set; }

    /// <summary>Tamanho em pixels.</summary>
    [Parameter] public int Size { get; set; } = 22;

    /// <summary>Cor do círculo atrás do destaque: a mesma do fundo onde o ícone está.</summary>
    [Parameter] public string Background { get; set; } = "var(--mud-palette-surface)";

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if ((Icon ?? WidgetIcons.Find(IconKey)) is not { } icon)
            return;

        if (icon.PathData is { } path)
        {
            builder.OpenElement(0, "svg");
            builder.AddAttribute(1, "class", Class);
            builder.AddAttribute(2, "width", Size);
            builder.AddAttribute(3, "height", Size);
            builder.AddAttribute(4, "viewBox", "0 0 24 24");
            builder.AddAttribute(5, "fill", "currentColor");
            builder.AddAttribute(6, "aria-label", icon.Name);
            builder.OpenElement(7, "path");
            builder.AddAttribute(8, "d", path);
            builder.CloseElement();
            builder.CloseElement();
            return;
        }

        // Caixa quadrada de Size x Size, com os glifos nas mesmas posições do tile (WidgetIconLayout): num ícone
        // combinado, o principal um pouco menor e para cima e para a esquerda, o destaque no círculo do canto de baixo.
        var place = WidgetIconLayout.Place(icon, new IconRect(0, 0, Size, Size), Size);
        builder.OpenElement(10, "span");
        builder.AddAttribute(11, "class", Class);
        builder.AddAttribute(12, "title", icon.Name);
        builder.AddAttribute(13, "style",
            $"position:relative;display:inline-block;flex:0 0 auto;width:{Px(Size)};height:{Px(Size)};"
            + $"font-family:{WidgetIcons.FontFamily};font-style:normal;line-height:1");
        Glyph(builder, 20, icon.Glyph, place.Main, place.MainSize, null);
        if (icon.Accent is { } accent && place.AccentCircle is { } circle)
            Glyph(builder, 30, accent, circle, place.AccentSize, Background);
        builder.CloseElement();
    }

    private static void Glyph(RenderTreeBuilder builder, int seq, string glyph, IconRect box, float size, string? background)
    {
        builder.OpenElement(seq, "span");
        builder.AddAttribute(seq + 1, "style",
            $"position:absolute;left:{Px(box.X)};top:{Px(box.Y)};width:{Px(box.Width)};height:{Px(box.Height)};"
            + $"display:flex;align-items:center;justify-content:center;font-size:{Px(size)}"
            + (background is null ? "" : $";border-radius:50%;background:{background}"));
        builder.AddContent(seq + 2, glyph);
        builder.CloseElement();
    }

    private static string Px(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "px";
}
