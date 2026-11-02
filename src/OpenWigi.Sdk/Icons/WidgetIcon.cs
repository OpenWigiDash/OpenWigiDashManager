using System.Globalization;
using System.Text;

namespace OpenWigi.Sdk;

/// <summary>Uma aba da biblioteca de ícones.</summary>
/// <param name="Key">Chave estável (ex.: <c>"audio"</c>).</param>
/// <param name="Name">Nome mostrado na aba.</param>
public sealed record WidgetIconCategory(string Key, string Name);

/// <summary>
/// Um ícone da biblioteca compartilhada (<see cref="WidgetIcons"/>). Hoje todos são glifos da fonte de ícones do
/// Windows (<see cref="WidgetIcons.FontFamily"/>).
/// </summary>
/// <param name="Key">
/// Chave estável, que o widget guarda nas configurações. Nunca muda nem é reaproveitada para outro desenho.
/// </param>
/// <param name="Name">Nome mostrado na janela de escolha.</param>
/// <param name="Category">Chave da <see cref="WidgetIconCategory"/>.</param>
/// <param name="Glyph">Glifo principal (um caractere da fonte de ícones).</param>
/// <param name="Accent">
/// Segundo glifo, opcional, desenhado pequeno num círculo no canto inferior direito (ex.: microfone sobre a webcam).
/// Posições em <see cref="WidgetIconLayout"/>.
/// </param>
public sealed record WidgetIcon(string Key, string Name, string Category, string Glyph, string? Accent = null)
{
    /// <summary>Palavras a mais para a busca (sinônimos, em português e inglês).</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Desenho em vetor: o atributo <c>d</c> de um <c>&lt;path&gt;</c> SVG numa caixa de 24x24, preenchido com a cor
    /// do texto. Quando existe, vale no lugar de <see cref="Glyph"/>. Nenhum ícone da biblioteca usa ainda; serve para
    /// ícones que a fonte do Windows não tem (ex.: logos), e para os que o plugin traz por conta própria
    /// (<see cref="WidgetIconPickerOptions.Extra"/>).
    /// </summary>
    public string? PathData { get; init; }

    /// <summary>O ícone tem um segundo glifo de destaque (ícone combinado).</summary>
    public bool IsCombined => Accent is not null;
}

/// <summary>
/// A biblioteca de ícones que o host oferece a todos os widgets, separada em categorias. Cresce com o SDK; as chaves
/// que já existem não mudam. Para a pessoa escolher um, use o <see cref="IWidgetIconPicker"/> na tela de configurações
/// e mostre o escolhido com o <see cref="WidgetIconView"/>.
/// </summary>
public static partial class WidgetIcons
{
    /// <summary>
    /// Fonte dos glifos, para CSS: Segoe Fluent Icons no Windows 11 e Segoe MDL2 Assets no Windows 10 (mesmos códigos).
    /// Para desenhar no tile com <c>System.Drawing</c>, use <see cref="FontFamilyNames"/> e fique com a primeira instalada.
    /// </summary>
    public const string FontFamily = "'Segoe Fluent Icons', 'Segoe MDL2 Assets'";

    /// <summary>As fontes de <see cref="FontFamily"/>, em ordem de preferência.</summary>
    public static IReadOnlyList<string> FontFamilyNames { get; } = ["Segoe Fluent Icons", "Segoe MDL2 Assets"];

    /// <summary>As categorias, na ordem das abas.</summary>
    public static IReadOnlyList<WidgetIconCategory> Categories { get; } = BuildCategories();

    /// <summary>Todos os ícones, agrupados pela ordem de <see cref="Categories"/>.</summary>
    public static IReadOnlyList<WidgetIcon> All { get; } = BuildIcons();

    private static readonly Dictionary<string, WidgetIcon> ByKey =
        All.ToDictionary(i => i.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>O ícone da chave (sem diferenciar maiúsculas), ou <c>null</c> se não existe.</summary>
    public static WidgetIcon? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var icon) ? icon : null;

    /// <summary>A categoria da chave, ou <c>null</c>.</summary>
    public static WidgetIconCategory? FindCategory(string? key) =>
        Categories.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ícones de uma categoria, na ordem da biblioteca.</summary>
    public static IEnumerable<WidgetIcon> InCategory(string category) =>
        All.Where(i => string.Equals(i.Category, category, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ícones cujo nome, chave, categoria ou palavras extras contêm todas as palavras de <paramref name="text"/>,
    /// sem diferenciar maiúsculas nem acentos ("camera" acha "Câmera"). Texto vazio devolve tudo.
    /// </summary>
    /// <param name="icons">Onde procurar; <c>null</c> = <see cref="All"/>.</param>
    public static IEnumerable<WidgetIcon> Search(string? text, IEnumerable<WidgetIcon>? icons = null)
    {
        icons ??= All;
        var words = Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return icons;
        return icons.Where(icon =>
        {
            var haystack = Normalize(string.Join(' ',
                [icon.Name, icon.Key.Replace('-', ' '), FindCategory(icon.Category)?.Name ?? icon.Category, .. icon.Tags]));
            return words.All(haystack.Contains);
        });
    }

    /// <summary>Minúsculas e sem acentos, para comparar textos digitados.</summary>
    internal static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
