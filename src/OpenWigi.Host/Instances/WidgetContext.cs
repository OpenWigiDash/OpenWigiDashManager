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
    public PixelSize PixelSize => IsFullscreen ? WidgetGrid.Screen : Size.PixelSize;
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
        if (frame.Size != PixelSize)
            throw new ArgumentException($"Quadro {frame.Size}, esperado {PixelSize}.", nameof(frame));
        return _owner.PresentAsync(this, frame, cancellationToken);
    }

    public void RequestRender() => _owner.RequestRender(this);

    public ValueTask<bool> RequestFullscreenAsync(bool fullscreen, CancellationToken cancellationToken = default) =>
        _owner.RequestFullscreenAsync(this, fullscreen, cancellationToken);

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
