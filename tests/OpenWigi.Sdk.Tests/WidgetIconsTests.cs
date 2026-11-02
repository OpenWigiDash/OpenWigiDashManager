using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenWigi.Sdk.Tests;

public partial class WidgetIconsTests
{
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KeyPattern();

    [Fact]
    public void Chaves_sao_unicas_e_em_minusculas_com_hifen()
    {
        Assert.Equal(WidgetIcons.All.Count, WidgetIcons.All.Select(i => i.Key).Distinct().Count());
        Assert.All(WidgetIcons.All, i => Assert.Matches(KeyPattern(), i.Key));
        Assert.All(WidgetIcons.Categories, c => Assert.Matches(KeyPattern(), c.Key));
    }

    [Fact]
    public void Todo_icone_tem_categoria_conhecida_e_toda_categoria_tem_icone()
    {
        var categories = WidgetIcons.Categories.Select(c => c.Key).ToHashSet();
        Assert.All(WidgetIcons.All, i => Assert.Contains(i.Category, categories));
        Assert.All(WidgetIcons.Categories, c => Assert.NotEmpty(WidgetIcons.InCategory(c.Key)));
    }

    [Fact]
    public void Glifos_sao_um_caractere_da_area_privada_da_fonte()
    {
        static bool IsGlyph(string? s) => s is { Length: 1 } && s[0] >= '' && s[0] <= '';
        Assert.All(WidgetIcons.All, i =>
        {
            Assert.True(IsGlyph(i.Glyph), i.Key);
            Assert.True(i.Accent is null || IsGlyph(i.Accent), i.Key);
            Assert.False(string.IsNullOrWhiteSpace(i.Name));
        });
    }

    // As chaves e os desenhos que o plugin System já gravou nas configurações das pessoas.
    [Theory]
    [InlineData("speaker", '')]
    [InlineData("headphones", '')]
    [InlineData("headset", '')]
    [InlineData("microphone", '')]
    [InlineData("camera", '')]
    [InlineData("webcam-mic", '')]
    [InlineData("monitor", '')]
    [InlineData("volume", '')]
    [InlineData("bluetooth", '')]
    [InlineData("game", '')]
    [InlineData("music", '')]
    [InlineData("phone", '')]
    [InlineData("laptop", '')]
    [InlineData("volume-high", '')]
    [InlineData("volume-low", '')]
    [InlineData("mute", '')]
    public void Chaves_do_plugin_System_continuam_com_o_mesmo_desenho(string key, char glyph)
    {
        Assert.Equal(glyph.ToString(), WidgetIcons.Find(key)!.Glyph);
    }

    [Fact]
    public void Webcam_com_microfone_e_combinado()
    {
        var icon = WidgetIcons.Find("WEBCAM-MIC")!;
        Assert.True(icon.IsCombined);
        Assert.Equal("", icon.Accent);
        Assert.Null(WidgetIcons.Find("nao-existe"));
        Assert.Null(WidgetIcons.Find(null));
    }

    [Theory]
    [InlineData("microfone", "microphone")]
    [InlineData("MICROFONE", "webcam-mic")]
    [InlineData("camera", "photo-camera")]
    [InlineData("musica", "music")]
    [InlineData("volume alto", "volume-high")]
    [InlineData("cpu", "processor")]
    [InlineData("audio", "speaker")] // pelo nome da categoria
    public void Busca_ignora_acentos_e_maiusculas(string text, string expected)
    {
        Assert.Contains(WidgetIcons.Search(text), i => i.Key == expected);
    }

    [Fact]
    public void Busca_vazia_devolve_tudo_e_palavras_precisam_bater_todas()
    {
        Assert.Equal(WidgetIcons.All.Count, WidgetIcons.Search("  ").Count());
        Assert.DoesNotContain(WidgetIcons.Search("volume alto"), i => i.Key == "volume-low");
        var extra = new WidgetIcon("logo", "Logo do app", "apps", "");
        Assert.Single(WidgetIcons.Search("logo", [extra]));
    }

    [Fact]
    public void Destaque_fica_no_canto_inferior_direito_com_metade_do_tamanho()
    {
        var box = new IconRect(0, 0, 100, 100);
        var single = WidgetIconLayout.Place(WidgetIcons.Find("speaker")!, box, 80);
        Assert.Equal(box, single.Main);
        Assert.Equal(80, single.MainSize);
        Assert.Null(single.AccentCircle);

        var combined = WidgetIconLayout.Place(WidgetIcons.Find("webcam-mic")!, box, 80);
        Assert.True(combined.MainSize < 80);
        Assert.True(combined.Main.CenterX < box.CenterX && combined.Main.CenterY < box.CenterY);
        Assert.Equal(40, combined.AccentSize);
        var circle = combined.AccentCircle!.Value;
        Assert.True(circle.CenterX > box.CenterX && circle.CenterY > box.CenterY);
        Assert.Equal(circle.Width, circle.Height);
        Assert.True(circle.Width > combined.AccentSize);
    }

    [Fact]
    public void Escolha_automatica_nao_tem_chave()
    {
        Assert.True(WidgetIconChoice.Automatic.IsAutomatic);
        Assert.False(new WidgetIconChoice("speaker").IsAutomatic);
    }

    [Fact]
    public void Contrato_3_1_carrega_plugin_3_0()
    {
        Assert.Equal(new Version(3, 1), OpenWigiSdk.Version);
        Assert.True(OpenWigiSdk.IsCompatible(new Version(3, 0)));
        Assert.True(OpenWigiSdk.IsCompatible(new Version(3, 1)));
        Assert.False(OpenWigiSdk.IsCompatible(new Version(3, 2)));
    }

    [Fact]
    public async Task View_mostra_glifo_e_destaque()
    {
        var html = await RenderAsync(new Dictionary<string, object?> { ["IconKey"] = "webcam-mic", ["Size"] = 30 });
        Assert.Contains("", html);
        Assert.Contains("", html);
        Assert.Contains("font-size:30px", html);
        Assert.Contains("font-size:15px", html);
        Assert.Contains("Segoe Fluent Icons", html);
    }

    [Fact]
    public async Task View_desenha_vetor_quando_ha_e_nada_para_chave_desconhecida()
    {
        var vector = new WidgetIcon("x", "X", "symbols", "") { PathData = "M0 0L24 24" };
        var html = await RenderAsync(new Dictionary<string, object?> { ["Icon"] = vector });
        Assert.Contains("<svg", html);
        Assert.Contains("M0 0L24 24", html);
        Assert.DoesNotContain("", html);

        Assert.Equal("", await RenderAsync(new Dictionary<string, object?> { ["IconKey"] = "nao-existe" }));
    }

    private static async Task<string> RenderAsync(Dictionary<string, object?> parameters)
    {
        await using var renderer = new HtmlRenderer(new EmptyServices(), NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            System.Net.WebUtility.HtmlDecode(
                (await renderer.RenderComponentAsync<WidgetIconView>(ParameterView.FromDictionary(parameters))).ToHtmlString()));
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
