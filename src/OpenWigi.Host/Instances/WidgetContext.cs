using Microsoft.Extensions.Logging;
using OpenWigi.Sdk;

namespace OpenWigi.Host.Instances;

/// <summary>O lado do host que uma instância enxerga. As decisões ficam no <see cref="DashboardHost"/>.</summary>
internal sealed class WidgetContext : IWidgetContext
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly IWidgetContextOwner _owner;

    public WidgetContext(Guid instanceId, WidgetSize size, IWidgetHost host, InstanceStorage storage,
        IWidgetActions actions, ILogger logger, IWidgetContextOwner owner)
    {
        InstanceId = instanceId;
        Size = size;
        Host = host;
        Storage = storage;
        Actions = actions;
        Logger = logger;
        _owner = owner;
    }

    public Guid InstanceId { get; }
    public IWidgetHost Host { get; }
    public WidgetSize Size { get; }
    public PixelSize PixelSize => IsFullscreen ? WidgetGrid.Screen : ContentSize;

    /// <summary>Área de conteúdo fora de tela cheia: a célula menos a moldura da página. O host mantém.</summary>
    public PixelSize ContentSize
    {
        get => _contentSize;
        set
        {
            if (value == _contentSize)
                return;
            _staleSize = _contentSize;
            _contentSize = value;
        }
    }

    private PixelSize _contentSize;
    private PixelSize _staleSize; // tamanho de antes da última troca de moldura ou posição

    public TileFrame Frame { get; set; } = TileFrame.Default;
    public bool IsFullscreen { get; set; }
    public bool IsAsleep { get; set; }
    public WidgetTheme Theme => Host.Theme;
    public InstanceStorage Storage { get; }
    public IWidgetSettings Settings => Storage;
    public IWidgetFiles Files => Storage;
    public IWidgetActions Actions { get; }
    public ILogger Logger { get; }
    public CancellationToken Stopping => _stopping.Token;

    public ValueTask PresentAsync(WidgetFrame frame, CancellationToken cancellationToken = default)
    {
        // Quadro que já estava sendo desenhado quando a moldura mudou: descarta em silêncio. O widget
        // recebe OnSizeChangedAsync e um RenderAsync(SizeChanged) logo em seguida.
        if (frame.Size != PixelSize && !IsFullscreen && frame.Size == _staleSize)
            return ValueTask.CompletedTask;
        if (frame.Size != PixelSize)
            throw new ArgumentException($"Quadro {frame.Size}, esperado {PixelSize}.", nameof(frame));
        return _owner.PresentAsync(this, frame, cancellationToken);
    }

    public void RequestRender() => _owner.RequestRender(this);

    public ValueTask<bool> RequestFullscreenAsync(bool fullscreen, CancellationToken cancellationToken = default) =>
        _owner.RequestFullscreenAsync(this, fullscreen, cancellationToken);

    public ValueTask<bool> OpenPluginSettingsAsync(CancellationToken cancellationToken = default) =>
        _owner.OpenPluginSettingsAsync(this, cancellationToken);

    public void Stop()
    {
        if (!_stopping.IsCancellationRequested)
            _stopping.Cancel();
    }
}

/// <summary>O que o contexto pede ao host.</summary>
internal interface IWidgetContextOwner
{
    ValueTask PresentAsync(WidgetContext context, WidgetFrame frame, CancellationToken cancellationToken);
    void RequestRender(WidgetContext context);
    ValueTask<bool> RequestFullscreenAsync(WidgetContext context, bool fullscreen, CancellationToken cancellationToken);
    ValueTask<bool> OpenPluginSettingsAsync(WidgetContext context, CancellationToken cancellationToken);
}

/// <summary>Central de ações ainda sem as regras do usuário (fase 7): registra e loga.</summary>
internal sealed class LoggingActions(ILogger logger) : IWidgetActions
{
    public ValueTask RegisterTriggerAsync(Guid triggerId, string name, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Gatilho registrado: {Name} ({Id})", name, triggerId);
        return ValueTask.CompletedTask;
    }

    public ValueTask UnregisterTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask FireTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Gatilho disparado: {Id} (central de ações chega na fase 7)", triggerId);
        return ValueTask.CompletedTask;
    }

    public ValueTask RegisterActionAsync(Guid actionId, string name, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask UnregisterActionAsync(Guid actionId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask<IReadOnlyList<WidgetActionInfo>> GetBoundActionsAsync(int setId = 0, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<WidgetActionInfo>>([]);

    public ValueTask InvokeAsync(Guid actionId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

internal sealed class WidgetHostInfo(string resourceDirectory, ILoggerFactory loggerFactory, TimeProvider time, WidgetTheme theme)
    : IWidgetHost
{
    public string ResourceDirectory { get; } = resourceDirectory;
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
    public TimeProvider Time { get; } = time;
    public WidgetTheme Theme { get; } = theme;
}
