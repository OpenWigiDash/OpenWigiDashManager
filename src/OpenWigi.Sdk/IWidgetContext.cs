using Microsoft.Extensions.Logging;

namespace OpenWigi.Sdk;

/// <summary>
/// O que o host oferece a uma instância. Um contexto por instância, válido até o
/// <see cref="IAsyncDisposable.DisposeAsync"/> dela. Pode ser chamado de qualquer thread.
/// </summary>
public interface IWidgetContext
{
    Guid InstanceId { get; }
    IWidgetHost Host { get; }

    /// <summary>Tamanho no layout, em meias células. Desenhe em <see cref="PixelSize"/>, não neste.</summary>
    WidgetSize Size { get; }

    /// <summary>
    /// Tamanho que o quadro deve ter agora: a área de conteúdo do widget (a célula menos a moldura da
    /// página), ou a tela inteira em tela cheia. Muda com a moldura da página ou com a posição; o host
    /// avisa por <see cref="IWidgetInstance.OnSizeChangedAsync"/>.
    /// </summary>
    PixelSize PixelSize { get; }

    /// <summary>Moldura da página onde o widget está. Em tela cheia o host não pinta moldura.</summary>
    TileFrame Frame { get; }

    bool IsFullscreen { get; }
    bool IsAsleep { get; }
    WidgetTheme Theme { get; }

    IWidgetSettings Settings { get; }
    IWidgetFiles Files { get; }
    IWidgetActions Actions { get; }
    ILogger Logger { get; }

    /// <summary>Cancela quando a instância está sendo descartada.</summary>
    CancellationToken Stopping { get; }

    /// <summary>
    /// Entrega um quadro sem o host ter pedido (animação, captura, dado novo). Completa quando o host
    /// aceitou o quadro; quem empurra mais rápido que o USB espera aqui. Durante o repouso o host
    /// descarta e completa na hora. Quadro de tamanho errado é rejeitado com <see cref="ArgumentException"/>.
    /// </summary>
    ValueTask PresentAsync(WidgetFrame frame, CancellationToken cancellationToken = default);

    /// <summary>Pede um <see cref="IWidgetInstance.RenderAsync"/> com <see cref="RenderReason.Requested"/>. Pedidos seguidos se juntam.</summary>
    void RequestRender();

    /// <summary>Pede para ocupar a tela inteira (ou voltar). <c>false</c> se o host recusou.</summary>
    ValueTask<bool> RequestFullscreenAsync(bool fullscreen, CancellationToken cancellationToken = default);
}

/// <summary>
/// Configurações da instância, chave e valor em texto. Ficam no perfil do usuário do Windows que é dono
/// do layout. Para objetos, use as extensões JSON de <see cref="WidgetSettingsExtensions"/>.
/// </summary>
public interface IWidgetSettings
{
    ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <param name="cloneable">
    /// Copia junto ao duplicar o widget. Use <c>false</c> para o que é único da instância (token, id de sessão).
    /// </param>
    ValueTask SetAsync(string key, string value, bool cloneable = true, CancellationToken cancellationToken = default);

    ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyCollection<string>> GetKeysAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Arquivos da instância, guardados pelo host numa pasta só dela e identificados por um id escolhido
/// pelo widget. Somem no <see cref="IWidgetInstance.OnRemovedAsync"/> se o widget não apagar antes.
/// </summary>
public interface IWidgetFiles
{
    /// <summary>Copia um arquivo de fora (ex.: imagem escolhida na tela de configurações). Devolve o caminho guardado.</summary>
    ValueTask<string> ImportAsync(string fileId, string sourcePath, CancellationToken cancellationToken = default);

    /// <summary>Reserva um caminho para o widget escrever (cache, banco local). Não cria o arquivo.</summary>
    ValueTask<string> CreateAsync(string fileId, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Caminho guardado, ou <c>null</c> se o id não existe.</summary>
    ValueTask<string?> GetPathAsync(string fileId, CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(string fileId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A central de ações do host. Gatilho = algo que o widget avisa que aconteceu; ação = algo que o widget
/// sabe fazer quando mandam. A pessoa liga gatilhos a ações no manager.
/// </summary>
public interface IWidgetActions
{
    /// <summary>Anuncia um gatilho que esta instância pode disparar (ex.: "botão 3 apertado").</summary>
    ValueTask RegisterTriggerAsync(Guid triggerId, string name, CancellationToken cancellationToken = default);
    ValueTask UnregisterTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default);

    /// <summary>Dispara o gatilho; o host roda as ações ligadas a ele.</summary>
    ValueTask FireTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default);

    /// <summary>Anuncia uma ação; quando disparada, chega em <see cref="IWidgetInstance.OnActionAsync"/>.</summary>
    ValueTask RegisterActionAsync(Guid actionId, string name, CancellationToken cancellationToken = default);
    ValueTask UnregisterActionAsync(Guid actionId, CancellationToken cancellationToken = default);

    /// <summary>Ações que a pessoa ligou a esta instância no conjunto <paramref name="setId"/> (ex.: um por botão).</summary>
    ValueTask<IReadOnlyList<WidgetActionInfo>> GetBoundActionsAsync(int setId = 0, CancellationToken cancellationToken = default);

    /// <summary>Roda uma ação qualquer da central (de outro widget ou do host).</summary>
    ValueTask InvokeAsync(Guid actionId, CancellationToken cancellationToken = default);
}

public sealed record WidgetActionInfo(Guid Id, string Name);

/// <summary>Cor RGB de 8 bits por canal.</summary>
public readonly record struct WidgetColor(byte R, byte G, byte B)
{
    /// <summary>A cor como a tela mostra (RGB565).</summary>
    public ushort ToRgb565() => (ushort)(((R >> 3) << 11) | ((G >> 2) << 5) | (B >> 3));

    public static WidgetColor FromRgb565(ushort value) =>
        new((byte)((value >> 11) << 3), (byte)(((value >> 5) & 0x3F) << 2), (byte)((value & 0x1F) << 3));

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>Tema global do manager; widget que quiser combinar com os outros usa estes valores.</summary>
public sealed record WidgetTheme
{
    public string PrimaryFontFamily { get; init; } = "Segoe UI";
    public string SecondaryFontFamily { get; init; } = "Segoe UI Semibold";
    public WidgetColor PrimaryForeground { get; init; } = new(255, 255, 255);
    public WidgetColor SecondaryForeground { get; init; } = new(255, 0, 0);
    public WidgetColor PrimaryBackground { get; init; } = new(48, 48, 48);
    public WidgetColor SecondaryBackground { get; init; } = new(128, 128, 128);
    /// <summary>Raio sugerido para painéis dentro do widget. O canto de fora do tile é do host.</summary>
    public int CornerRadius { get; init; } = 15;
}
