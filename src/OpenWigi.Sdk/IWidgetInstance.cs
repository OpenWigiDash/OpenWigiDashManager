using Microsoft.AspNetCore.Components;

namespace OpenWigi.Sdk;

/// <summary>Por que o host está pedindo um quadro inteiro.</summary>
public enum RenderReason
{
    /// <summary>Primeiro quadro, logo depois de criar a instância.</summary>
    Initial,
    /// <summary>A instância pediu com <see cref="IWidgetContext.RequestRender"/>.</summary>
    Requested,
    /// <summary>A tela voltou do repouso ou a página do widget voltou a aparecer.</summary>
    Resumed,
    /// <summary>Entrou ou saiu de tela cheia: o tamanho do quadro mudou.</summary>
    FullscreenChanged,
    /// <summary>O tema global mudou.</summary>
    ThemeChanged,
}

/// <summary>
/// Um widget posto na tela. Tudo é assíncrono e chamado pelo host, uma chamada por vez por instância
/// (o host não chama dois métodos da mesma instância ao mesmo tempo). Os ganchos opcionais já têm
/// implementação vazia.
/// </summary>
/// <remarks>
/// Quadros saem de dois jeitos: o host pede com <see cref="RenderAsync"/> (quadro inteiro, sem região suja),
/// ou a instância empurra por conta própria com <see cref="IWidgetContext.PresentAsync"/> (aí pode
/// mandar só a região suja). Widget estático só implementa o primeiro.
/// </remarks>
public interface IWidgetInstance : IAsyncDisposable
{
    /// <summary>
    /// Quadro completo do tamanho atual (<see cref="IWidgetContext.PixelSize"/>). A região suja é ignorada.
    /// </summary>
    ValueTask<WidgetFrame> RenderAsync(RenderReason reason, CancellationToken cancellationToken);

    /// <summary>Gesto dentro do widget, já em coordenadas do widget.</summary>
    ValueTask OnTouchAsync(TouchEvent touch, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>Tela em repouso ou página fora de vista: pare captura, timers e rede.</summary>
    ValueTask OnSleepAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>Saiu do repouso. O host pede um <see cref="RenderReason.Resumed"/> em seguida.</summary>
    ValueTask OnWakeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>Entrou ou saiu de tela cheia. O host pede um <see cref="RenderReason.FullscreenChanged"/> em seguida.</summary>
    ValueTask OnFullscreenChangedAsync(bool fullscreen, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>O tema global mudou (<see cref="IWidgetContext.Theme"/> já tem o novo).</summary>
    ValueTask OnThemeChangedAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>
    /// Uma ação que esta instância registrou (<see cref="IWidgetActions.RegisterActionAsync"/>) foi
    /// disparada pela central de ações.
    /// </summary>
    ValueTask OnActionAsync(Guid actionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>
    /// A pessoa tirou o widget do layout: apague o que for só dele (arquivos, credenciais).
    /// Depois vem o <see cref="IAsyncDisposable.DisposeAsync"/>. Descarregar o plugin não chama isto.
    /// </summary>
    ValueTask OnRemovedAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>
    /// Componente Razor da tela de configurações, ou <c>null</c> se não houver. O host renderiza com o
    /// parâmetro <c>Instance</c> (ver <see cref="WidgetSettingsComponent{TInstance}"/>) e com MudBlazor
    /// já carregado.
    /// </summary>
    Type? SettingsComponent => null;
}

/// <summary>
/// Base para a tela de configurações. O host passa a instância no parâmetro <see cref="Instance"/> e o
/// contexto em cascata; salve pelo <see cref="IWidgetContext.Settings"/>.
/// </summary>
public abstract class WidgetSettingsComponent<TInstance> : ComponentBase
    where TInstance : IWidgetInstance
{
    [Parameter, EditorRequired]
    public TInstance Instance { get; set; } = default!;

    [CascadingParameter]
    public IWidgetContext Context { get; set; } = default!;
}
