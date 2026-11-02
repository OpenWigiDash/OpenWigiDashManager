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

        builder.OpenElement(10, "span");
        builder.AddAttribute(11, "class", Class);
        builder.AddAttribute(12, "title", icon.Name);
        builder.AddAttribute(13, "style",
            $"position:relative;display:inline-block;font-family:{WidgetIcons.FontFamily};font-size:{Px(Size)};"
            + $"line-height:1.2;width:{Px(Size * 1.15)};text-align:center;font-style:normal");
        builder.AddContent(14, icon.Glyph);
        if (icon.Accent is { } accent)
        {
            builder.OpenElement(15, "span");
            builder.AddAttribute(16, "style",
                $"position:absolute;right:{Px(-Size * 0.12)};bottom:{Px(-Size * 0.02)};font-size:{Px(Size * WidgetIconLayout.AccentScale)};"
                + $"line-height:1;padding:1px;border-radius:50%;background:{Background}");
            builder.AddContent(17, accent);
            builder.CloseElement();
        }
        builder.CloseElement();
    }

    private static string Px(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "px";
}
