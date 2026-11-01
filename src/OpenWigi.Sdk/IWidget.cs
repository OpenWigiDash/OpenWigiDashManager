using Microsoft.Extensions.Logging;

namespace OpenWigi.Sdk;

/// <summary>
/// Marca a classe de entrada do plugin. O host lê o atributo em vez de varrer o assembly.
/// <code>[assembly: OpenWigiWidget(typeof(MeuWidget))]</code>
/// Um assembly pode declarar mais de um widget.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class OpenWigiWidgetAttribute(Type widgetType) : Attribute
{
    /// <summary>Classe que implementa <see cref="IWidget"/>, com construtor sem parâmetros.</summary>
    public Type WidgetType { get; } = widgetType;
}

/// <summary>A ficha do widget, lida pelo host antes de criar qualquer instância.</summary>
public sealed record WidgetManifest
{
    /// <summary>Identidade estável: é por ela que o layout salvo encontra o widget.</summary>
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required Version Version { get; init; }
    public required IReadOnlyList<WidgetSize> SupportedSizes { get; init; }
    public string? Author { get; init; }
    public string? Website { get; init; }
    public string? Description { get; init; }

    /// <summary>Versão do contrato contra a qual o widget foi compilado.</summary>
    public Version SdkVersion { get; init; } = OpenWigiSdk.Version;

    /// <summary>A instância sabe ocupar a tela inteira (<see cref="IWidgetContext.RequestFullscreenAsync"/>).</summary>
    public bool SupportsFullscreen { get; init; }

    /// <summary>Problemas que impedem o host de carregar o widget; vazio = ok.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (Id == Guid.Empty)
            problems.Add("Id vazio.");
        if (string.IsNullOrWhiteSpace(Name))
            problems.Add("Nome vazio.");
        if (SupportedSizes is null || SupportedSizes.Count == 0)
            problems.Add("Nenhum tamanho suportado.");
        else if (SupportedSizes.Any(s => s.Columns == 0))
            problems.Add("Tamanho não inicializado (default) na lista.");
        if (!OpenWigiSdk.IsCompatible(SdkVersion))
            problems.Add($"SDK {SdkVersion} não é compatível com o host ({OpenWigiSdk.Version}).");
        return problems;
    }
}

/// <summary>O que o host oferece ao widget como um todo (não a uma instância).</summary>
public interface IWidgetHost
{
    /// <summary>Pasta do plugin, para ler recursos que vieram junto (imagens, fontes).</summary>
    string ResourceDirectory { get; }
    ILoggerFactory LoggerFactory { get; }
    TimeProvider Time { get; }
    WidgetTheme Theme { get; }
}

/// <summary>
/// Entrada do plugin: descreve o widget e fabrica instâncias. Um objeto por widget carregado.
/// </summary>
public interface IWidget
{
    WidgetManifest Manifest { get; }

    /// <summary>Chamado uma vez, depois de carregar e antes da primeira instância.</summary>
    ValueTask LoadAsync(IWidgetHost host, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>Chamado ao descarregar o plugin, depois de todas as instâncias serem descartadas.</summary>
    ValueTask UnloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>
    /// Ícone do catálogo, do tamanho <paramref name="pixelSize"/> (a área de conteúdo de um widget de
    /// <paramref name="size"/> na moldura padrão). <c>null</c> = o host desenha um genérico.
    /// </summary>
    ValueTask<WidgetFrame?> GetPreviewAsync(WidgetSize size, PixelSize pixelSize, CancellationToken cancellationToken) =>
        ValueTask.FromResult<WidgetFrame?>(null);

    /// <summary>
    /// Cria uma instância. O host só pede tamanhos de <see cref="WidgetManifest.SupportedSizes"/> e chama
    /// <see cref="IWidgetInstance.RenderAsync"/> logo depois.
    /// </summary>
    ValueTask<IWidgetInstance> CreateInstanceAsync(IWidgetContext context, CancellationToken cancellationToken);
}
