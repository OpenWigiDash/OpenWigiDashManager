using Microsoft.Extensions.Logging;
using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;
using OpenWigi.Host.Display;
using OpenWigi.Host.Instances;
using OpenWigi.Host.Layout;
using OpenWigi.Host.Plugins;
using OpenWigi.Sdk;

namespace OpenWigi.Host;

public sealed record DashboardOptions
{
    /// <summary>Pasta com uma subpasta por plugin.</summary>
    public required string PluginsDirectory { get; init; }

    /// <summary>Layout e dados das instâncias (por usuário do Windows).</summary>
    public required string DataDirectory { get; init; }

    /// <summary>Intervalo de envio das linhas sujas ao aparelho (~30 por segundo).</summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromMilliseconds(33);

    public WidgetTheme Theme { get; init; } = new();
}

/// <summary>
/// O painel de um usuário: carrega os plugins, cria as instâncias do layout, compõe a página na tela,
/// roteia os toques e põe widgets para dormir quando a página some ou a tela vai para outra sessão.
/// </summary>
public sealed class DashboardHost : IAsyncDisposable, IWidgetContextOwner, IWidgetHostFactory
{
    private readonly DashboardOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Compositor _compositor;
    private readonly PluginCatalog _catalog;
    private readonly LayoutStore _store;
    private readonly Dictionary<Guid, WidgetRuntime> _runtimes = [];
    private readonly SemaphoreSlim _state = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private DashboardLayout _layout = new();
    private Task? _flushLoop;
    private WidgetRuntime? _fullscreen;
    private WidgetRuntime? _pressTarget;
    private bool _active;

    public DashboardHost(DashboardOptions options, IDisplaySink sink, ILoggerFactory loggerFactory, TimeProvider? time = null)
    {
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<DashboardHost>();
        _time = time ?? TimeProvider.System;
        _compositor = new Compositor(sink);
        _catalog = new PluginCatalog(loggerFactory);
        _store = new LayoutStore(Path.Combine(options.DataDirectory, "layout.json"));
    }

    public IReadOnlyList<LoadedWidget> AvailableWidgets => _catalog.Widgets;
    public IReadOnlyList<PluginLoadError> PluginErrors => _catalog.Errors;
    public DashboardLayout Layout => _layout;
    public int CurrentPage { get; private set; }

    /// <summary>Carrega plugins e layout, cria as instâncias. Ainda não desenha: isso vem com <see cref="ActivateAsync"/>.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _catalog.LoadAllAsync(_options.PluginsDirectory, this, cancellationToken);
        _layout = _store.Load();
        if (!_layout.AllPlacements.Any() && _catalog.Widgets.Count > 0)
            PlaceDefaults();

        foreach (var placement in _layout.AllPlacements.ToList())
            await CreateRuntimeAsync(placement, cancellationToken);

        // Todos começam dormindo; acordam quando o agente ganha a tela.
        foreach (var runtime in _runtimes.Values)
        {
            runtime.Context.IsAsleep = true;
            runtime.LastFrame = await runtime.RenderAsync(RenderReason.Initial, cancellationToken);
            await runtime.CallAsync("OnSleepAsync", (i, ct) => i.OnSleepAsync(ct), cancellationToken);
        }

        _flushLoop = Task.Run(() => FlushLoopAsync(_stopping.Token), CancellationToken.None);
    }

    /// <summary>O agente ganhou (true) ou perdeu (false) a tela do aparelho.</summary>
    public async Task ActivateAsync(bool active, CancellationToken cancellationToken = default)
    {
        await _state.WaitAsync(cancellationToken);
        try
        {
            _active = active;
            _pressTarget = null;
            if (active)
            {
                await WakePageAsync(CurrentPage, cancellationToken);
                await ShowCurrentAsync(Transition.None, cancellationToken);
            }
            else
            {
                foreach (var runtime in _runtimes.Values.Where(r => !r.Context.IsAsleep))
                    await SleepAsync(runtime, cancellationToken);
            }
        }
        finally
        {
            _state.Release();
        }
    }

    /// <summary>Troca de página do layout, com a animação no sentido da troca.</summary>
    public async Task GoToPageAsync(int page, Transition transition, CancellationToken cancellationToken = default)
    {
        await _state.WaitAsync(cancellationToken);
        try
        {
            await GoToPageUnlockedAsync(page, transition, cancellationToken);
        }
        finally
        {
            _state.Release();
        }
    }

    /// <summary>Gesto vindo do aparelho (ou do toque emulado), em coordenadas da tela.</summary>
    public async Task HandleGestureAsync(GestureEvent gesture, CancellationToken cancellationToken = default)
    {
        if (!_active)
            return;

        var kind = (TouchKind)(int)gesture.Kind;
        WidgetRuntime? target;
        await _state.WaitAsync(cancellationToken);
        try
        {
            if (_fullscreen is null && kind is TouchKind.SwipeLeft or TouchKind.SwipeRight && _layout.Pages.Count > 1)
            {
                // Swipe horizontal fora de tela cheia troca de página, como no manager antigo.
                var count = _layout.Pages.Count;
                var next = kind == TouchKind.SwipeLeft ? (CurrentPage + 1) % count : (CurrentPage - 1 + count) % count;
                await GoToPageUnlockedAsync(next, kind == TouchKind.SwipeLeft ? Transition.WipeWest : Transition.WipeEast, cancellationToken);
                _pressTarget = null;
                return;
            }

            target = kind switch
            {
                TouchKind.PressStarted => _pressTarget = HitTest(gesture.X, gesture.Y),
                TouchKind.PressProgress or TouchKind.LongPress => _pressTarget,
                TouchKind.PressEnded => Take(ref _pressTarget),
                _ => HitTest(gesture.X, gesture.Y),
            };
        }
        finally
        {
            _state.Release();
        }

        if (target is null)
            return;
        var bounds = BoundsOf(target);
        var touch = new TouchEvent(kind, gesture.X - bounds.X, gesture.Y - bounds.Y, gesture.Duration, gesture.Progress);
        await target.CallAsync($"OnTouchAsync({kind})", (i, ct) => i.OnTouchAsync(touch, ct), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _stopping.Cancel();
        if (_flushLoop is not null)
            await _flushLoop;
        foreach (var runtime in _runtimes.Values)
            await runtime.DisposeAsync();
        _runtimes.Clear();
        await _catalog.DisposeAsync();
    }

    // --- IWidgetContextOwner

    ValueTask IWidgetContextOwner.PresentAsync(WidgetContext context, WidgetFrame frame, CancellationToken cancellationToken)
    {
        if (!_runtimes.TryGetValue(context.InstanceId, out var runtime))
            return ValueTask.CompletedTask;
        Accept(runtime, frame);
        return ValueTask.CompletedTask;
    }

    void IWidgetContextOwner.RequestRender(WidgetContext context)
    {
        if (!_runtimes.TryGetValue(context.InstanceId, out var runtime) || !runtime.TryRequestRender())
            return;
        _ = Task.Run(async () =>
        {
            if (await runtime.RenderAsync(RenderReason.Requested, _stopping.Token) is { } frame)
                Accept(runtime, frame, wholeFrame: true);
        });
    }

    async ValueTask<bool> IWidgetContextOwner.RequestFullscreenAsync(WidgetContext context, bool fullscreen, CancellationToken cancellationToken)
    {
        if (!_runtimes.TryGetValue(context.InstanceId, out var runtime) || !runtime.Widget.Manifest.SupportsFullscreen)
            return false;

        await _state.WaitAsync(cancellationToken);
        try
        {
            if (fullscreen == context.IsFullscreen)
                return true;
            if (fullscreen && _fullscreen is not null)
                return false;

            context.IsFullscreen = fullscreen;
            _fullscreen = fullscreen ? runtime : null;
            await runtime.CallAsync("OnFullscreenChangedAsync", (i, ct) => i.OnFullscreenChangedAsync(fullscreen, ct), cancellationToken);
            runtime.LastFrame = await runtime.RenderAsync(RenderReason.FullscreenChanged, cancellationToken);
            await ShowCurrentAsync(Transition.None, cancellationToken);
            return true;
        }
        finally
        {
            _state.Release();
        }
    }

    // --- IWidgetHostFactory

    IWidgetHost IWidgetHostFactory.Create(WidgetManifest manifest, string pluginDirectory) =>
        new WidgetHostInfo(pluginDirectory, _loggerFactory, _time, _options.Theme);

    // --- interno

    // Quadro de RenderAsync vale inteiro (o contrato ignora a região suja); o de PresentAsync pode vir parcial.
    private void Accept(WidgetRuntime runtime, WidgetFrame frame, bool wholeFrame = false)
    {
        runtime.LastFrame = frame;
        if (!_active || runtime.Context.IsAsleep || !IsVisible(runtime))
            return;
        var drawn = wholeFrame && frame.Dirty is not null ? new WidgetFrame(frame.Size, frame.Format, frame.Pixels, frame.Stride) : frame;
        _compositor.Draw(BoundsOf(runtime), drawn);
    }

    private bool IsVisible(WidgetRuntime runtime) =>
        _fullscreen is { } full ? ReferenceEquals(full, runtime) : PageOf(runtime) == CurrentPage;

    private int PageOf(WidgetRuntime runtime) =>
        _layout.Pages.FindIndex(p => p.Widgets.Contains(runtime.Placement));

    private PixelRect BoundsOf(WidgetRuntime runtime) =>
        runtime.Context.IsFullscreen ? PixelRect.FromSize(WidgetGrid.Screen) : runtime.Placement.Bounds;

    private WidgetRuntime? HitTest(int x, int y)
    {
        if (_fullscreen is not null)
            return _fullscreen;
        if (CurrentPage >= _layout.Pages.Count)
            return null;
        var placement = _layout.Pages[CurrentPage].Widgets.FirstOrDefault(w => w.Bounds.Contains(x, y));
        return placement is not null && _runtimes.TryGetValue(placement.InstanceId, out var runtime) ? runtime : null;
    }

    private static WidgetRuntime? Take(ref WidgetRuntime? slot)
    {
        var value = slot;
        slot = null;
        return value;
    }

    private async Task GoToPageUnlockedAsync(int page, Transition transition, CancellationToken cancellationToken)
    {
        if (page < 0 || page >= _layout.Pages.Count || page == CurrentPage)
            return;
        foreach (var runtime in RuntimesOn(CurrentPage))
            await SleepAsync(runtime, cancellationToken);
        CurrentPage = page;
        if (_active)
        {
            await WakePageAsync(page, cancellationToken);
            await ShowCurrentAsync(transition, cancellationToken);
        }
    }

    private async Task WakePageAsync(int page, CancellationToken cancellationToken)
    {
        foreach (var runtime in RuntimesOn(page).Where(r => r.Context.IsAsleep))
        {
            runtime.Context.IsAsleep = false;
            await runtime.CallAsync("OnWakeAsync", (i, ct) => i.OnWakeAsync(ct), cancellationToken);
            runtime.LastFrame = await runtime.RenderAsync(RenderReason.Resumed, cancellationToken) ?? runtime.LastFrame;
        }
    }

    private async Task SleepAsync(WidgetRuntime runtime, CancellationToken cancellationToken)
    {
        runtime.Context.IsAsleep = true;
        await runtime.CallAsync("OnSleepAsync", (i, ct) => i.OnSleepAsync(ct), cancellationToken);
    }

    private Task ShowCurrentAsync(Transition transition, CancellationToken cancellationToken)
    {
        var widgets = _fullscreen is { } full
            ? [(BoundsOf(full), full.LastFrame)]
            : RuntimesOn(CurrentPage).Select(r => (BoundsOf(r), r.LastFrame)).ToList();
        return _compositor.ShowPageAsync(widgets, transition, cancellationToken).AsTask();
    }

    private IEnumerable<WidgetRuntime> RuntimesOn(int page) =>
        page < _layout.Pages.Count
            ? _layout.Pages[page].Widgets.Select(w => _runtimes.GetValueOrDefault(w.InstanceId)).OfType<WidgetRuntime>()
            : [];

    private async Task CreateRuntimeAsync(WidgetPlacement placement, CancellationToken cancellationToken)
    {
        if (_catalog.Find(placement.WidgetId) is not { } widget)
        {
            _logger.LogWarning("Widget {WidgetId} do layout não está instalado; a posição fica vazia", placement.WidgetId);
            return;
        }
        if (!widget.Manifest.SupportedSizes.Contains(placement.Size))
            placement.Size = widget.Manifest.SupportedSizes[0];

        var logger = _loggerFactory.CreateLogger($"Widget.{widget.Manifest.Name}");
        var storage = new InstanceStorage(Path.Combine(_options.DataDirectory, "instances"), placement.InstanceId);
        var context = new WidgetContext(placement.InstanceId, placement.Size,
            new WidgetHostInfo(widget.PluginDirectory, _loggerFactory, _time, _options.Theme),
            storage, new LoggingActions(logger), logger, this);
        var runtime = new WidgetRuntime(widget, placement, context, logger);
        if (await runtime.CreateAsync(cancellationToken))
            _runtimes[placement.InstanceId] = runtime;
    }

    // Primeira execução: um de cada widget instalado, na primeira posição livre.
    private void PlaceDefaults()
    {
        var page = _layout.Pages[0];
        foreach (var widget in _catalog.Widgets)
        {
            var size = widget.Manifest.SupportedSizes[0];
            if (page.FindFreeCell(size) is not { } cell)
                break;
            page.Widgets.Add(new WidgetPlacement { WidgetId = widget.Manifest.Id, Column = cell.Column, Row = cell.Row, Size = size });
        }
        _store.Save(_layout);
        _logger.LogInformation("Layout inicial criado com {Count} widgets", page.Widgets.Count);
    }

    private async Task FlushLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.FlushInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!_active)
                    continue;
                try
                {
                    await _compositor.FlushAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    _logger.LogDebug("Envio ao aparelho falhou: {Message}", ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
