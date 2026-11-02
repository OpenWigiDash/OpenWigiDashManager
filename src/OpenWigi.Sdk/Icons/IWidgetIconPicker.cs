namespace OpenWigi.Sdk;

/// <summary>
/// A janela de escolha de ícone que o manager oferece, com uma aba por categoria e busca. Peça na tela de
/// configurações com <c>@inject IWidgetIconPicker IconPicker</c>. A janela abre na raiz do manager, por cima do
/// canvas, e não dentro do painel de configurações.
/// </summary>
/// <example>
/// <code>
/// var choice = await IconPicker.PickAsync(new WidgetIconPickerOptions { Title = "Ícone do botão", Current = key });
/// if (choice is not null)
///     await Context.Settings.SetAsync("icon", choice.Key ?? "");
/// </code>
/// </example>
public interface IWidgetIconPicker
{
    /// <summary>Abre a janela e espera a pessoa escolher.</summary>
    /// <returns>O que a pessoa escolheu, ou <c>null</c> se ela fechou sem escolher.</returns>
    ValueTask<WidgetIconChoice?> PickAsync(WidgetIconPickerOptions options, CancellationToken cancellationToken = default);
}

/// <summary>Como abrir a janela de escolha de ícone.</summary>
public sealed record WidgetIconPickerOptions
{
    /// <summary>Título da janela.</summary>
    public string Title { get; init; } = "Escolher ícone";

    /// <summary>Chave do ícone escolhido agora, que aparece marcado; <c>null</c> = nenhum (ou Automático).</summary>
    public string? Current { get; init; }

    /// <summary>
    /// Mostra a opção "Automático" no começo, com este ícone de prévia (o que o widget usaria sozinho). Escolhida,
    /// volta como <see cref="WidgetIconChoice.Automatic"/>. <c>null</c> = sem essa opção.
    /// </summary>
    public WidgetIcon? Automatic { get; init; }

    /// <summary>Categorias que aparecem, na ordem das abas; <c>null</c> = todas.</summary>
    public IReadOnlyList<string>? Categories { get; init; }

    /// <summary>
    /// Ícones do próprio plugin, mostrados junto com os da biblioteca. Use uma categoria que existe ou uma nova (a
    /// aba nova leva o nome da chave). O widget é quem desenha esses no tile, porque <see cref="WidgetIcons.Find"/>
    /// não os conhece.
    /// </summary>
    public IReadOnlyList<WidgetIcon> Extra { get; init; } = [];

    /// <summary>Texto pequeno embaixo dos ícones (ex.: "Para usar uma imagem sua, feche e ...").</summary>
    public string? Hint { get; init; }
}

/// <summary>O que a pessoa escolheu na janela.</summary>
/// <param name="Key">Chave do ícone, ou <c>null</c> quando escolheu "Automático".</param>
public sealed record WidgetIconChoice(string? Key)
{
    /// <summary>A opção "Automático".</summary>
    public static WidgetIconChoice Automatic { get; } = new((string?)null);

    public bool IsAutomatic => Key is null;
}
