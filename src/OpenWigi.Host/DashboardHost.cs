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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Task<WidgetFrame?>> _icons = [];
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

    /// <summary>
    /// Ícone do catálogo: a prévia do plugin no menor tamanho que ele suporta, pedida uma vez.
    /// <c>null</c> = o plugin não tem prévia (ou ela falhou) e a UI usa um ícone genérico.
    /// </summary>
    public Task<WidgetFrame?> GetCatalogIconAsync(Guid widgetId) =>
        _icons.GetOrAdd(widgetId, LoadCatalogIconAsync);

    private async Task<WidgetFrame?> LoadCatalogIconAsync(Guid widgetId)
    {
        if (_catalog.Find(widgetId) is not { } widget)
            return null;
        var size = widget.Manifest.SupportedSizes.MinBy(s => s.Columns * s.Rows);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            return await widget.Widget.GetPreviewAsync(size, timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Prévia de {Name} falhou; o catálogo usa o ícone genérico", widget.Manifest.Name);
            return null;
        }
    }
    public DashboardLayout Layout => _layout;

    /// <summary>Página do layout que está (ou estaria) no aparelho. É a mesma no editor e na prévia.</summary>
    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (_currentPage == value)
                return;
            _currentPage = value;
            PageChanged?.Invoke();
        }
    }

    /// <summary>A página atual mudou: pelo editor, por swipe no aparelho ou na prévia, ou por exclusão de página.</summary>
    public event Action? PageChanged;

    private int _currentPage;

    /// <summary>Carrega plugins e layout, cria as instâncias. Ainda não desenha: isso vem com <see cref="ActivateAsync"/>.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _catalog.LoadAllAsync(_options.PluginsDirectory, this, cancellationToken);
        _layout = _store.Load();
        if (FixDuplicatePageNames())
            _store.Save(_layout);
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

    /// <summary>O layout mudou (widget adicionado, removido, movido; página nova). Para a UI se atualizar.</summary>
public event Action? LayoutChanged;

    /// <summary>
    /// Uma instância tem quadro novo (de qualquer página, com ou sem a tela do aparelho). Vem da thread
    /// do widget e pode vir muitas vezes por segundo: quem desenha deve juntar os avisos.
    /// </summary>
    public event Action<Guid>? FrameChanged;

    /// <summary>Último quadro da instância, o mesmo que vai (ou iria) para o aparelho; null antes do primeiro.</summary>
    public WidgetFrame? FrameOf(Guid instanceId) => _runtimes.GetValueOrDefault(instanceId)?.LastFrame;

    public bool IsActive => _active;

    /// <summary>Nome do widget (o tipo) de uma instância do layout, ou null se o plugin não está instalado.</summary>
    public string? NameOf(Guid instanceId) => _runtimes.GetValueOrDefault(instanceId)?.Name;

    /// <summary>Nome que a pessoa vê: o da instância, ou o do widget se ela não deu um.</summary>
    public string DisplayNameOf(Guid instanceId)
    {
        var placement = _layout.AllPlacements.FirstOrDefault(p => p.InstanceId == instanceId);
        return placement?.Name is { Length: > 0 } name ? name
            : NameOf(instanceId) ?? _catalog.Find(placement?.WidgetId ?? Guid.Empty)?.Manifest.Name ?? "Widget não instalado";
    }

    /// <summary>Muda o nome da instância (vazio volta ao nome do widget).</summary>
    public async Task RenameWidgetAsync(Guid instanceId, string? name, CancellationToken cancellationToken = default)
    {
        await _state.WaitAsync(cancellationToken);
        try
        {
            if (_layout.AllPlacements.FirstOrDefault(p => p.InstanceId == instanceId) is not { } placement)
                return;
            placement.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            _store.Save(_layout);
            LayoutChanged?.Invoke();
        }
        finally
        {
            _state.Release();
        }
    }

    // "Relógio", e se já houver um, "Relógio 2", "Relógio 3"...
    private string SuggestName(string widgetName)
    {
        var used = _layout.AllPlacements.Select(p => DisplayNameOf(p.InstanceId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(widgetName))
            return widgetName;
        for (var n = 2; ; n++)
            if (!used.Contains($"{widgetName} {n}"))
                return $"{widgetName} {n}";
    }

    /// <summary>Tela de configurações da instância (componente Razor do plugin), se houver.</summary>
    public (Type Component, IWidgetInstance Instance, IWidgetContext Context)? GetSettings(Guid instanceId) =>
        _runtimes.GetValueOrDefault(instanceId) is { Instance: { SettingsComponent: { } type } instance } runtime
            ? (type, instance, runtime.Context)
            : null;

    /// <summary>
    /// Põe um widget na página, em <paramref name="at"/> ou, sem posição, na primeira livre. Devolve o id
    /// da instância, ou null se não coube.
    /// </summary>
    public async Task<Guid?> AddWidgetAsync(Guid widgetId, int page, WidgetSize? size = null, CancellationToken cancellationToken = default,
        (int Column, int Row)? at = null)
    {
        if (_catalog.Find(widgetId) is not { } widget || page < 0 || page >= _layout.Pages.Count)
            return null;
        var chosen = size is { } s && widget.Manifest.SupportedSizes.Contains(s) ? s : widget.Manifest.SupportedSizes[0];

        await _state.WaitAsync(cancellationToken);
        try
        {
            var target = _layout.Pages[page];
            (int Column, int Row)? cell = at is { } p
                ? p.Column >= 0 && p.Row >= 0 && p.Column + chosen.Columns <= WidgetGrid.Columns && p.Row + chosen.Rows <= WidgetGrid.Rows
                  && !target.Widgets.Any(w => w.Overlaps(p.Column, p.Row, chosen)) ? p : null
                : target.FindFreeCell(chosen);
            if (cell is not { } free)
                return null;
            var placement = new WidgetPlacement
            {
                WidgetId = widgetId,
                Name = SuggestName(widget.Manifest.Name),
                Column = free.Column,
                Row = free.Row,
                Size = chosen,
            };
            _layout.Pages[page].Widgets.Add(placement);
            await CreateRuntimeAsync(placement, cancellationToken);
            if (_runtimes.TryGetValue(placement.InstanceId, out var runtime))
            {
                var visible = _active && page == CurrentPage && _fullscreen is null;
                runtime.Context.IsAsleep = !visible;
                runtime.LastFrame = await runtime.RenderAsync(RenderReason.Initial, cancellationToken);
                if (visible)
                    await runtime.CallAsync("OnWakeAsync", (i, ct) => i.OnWakeAsync(ct), cancellationToken);
                else
                    await runtime.CallAsync("OnSleepAsync", (i, ct) => i.OnSleepAsync(ct), cancellationToken);
            }
            await CommitLayoutAsync(page, cancellationToken);
            return placement.InstanceId;
        }
        finally
        {
            _state.Release();
        }
    }

    /// <summary>Tira o widget do layout: o plugin recebe OnRemovedAsync e os dados da instância são apagados.</summary>
    public async Task RemoveWidgetAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        await _state.WaitAsync(cancellationToken);
        try
        {
            var page = _layout.Pages.FindIndex(p => p.Widgets.Any(w => w.InstanceId == instanceId));
            if (page < 0)
                return;
            _layout.Pages[page].Widgets.RemoveAll(w => w.InstanceId == instanceId);
            if (_runtimes.Remove(instanceId, out var runtime))
            {
                if (ReferenceEquals(_fullscreen, runtime))
                    _fullscreen = null;
                if (ReferenceEquals(_pressTarget, runtime))
                    _pressTarget = null;
                await runtime.CallAsync("OnRemovedAsync", (i, ct) => i.OnRemovedAsync(ct), cancellationToken);
                await runtime.DisposeAsync();
                runtime.Context.Storage.DeleteAll();
            }
            await CommitLayoutAsync(page, cancellationToken);
        }
        finally
        {
            _state.Release();
        }
    }

    /// <summary>Move o widget para outra célula da mesma página. False se não couber ali.</summary>
    public async Task<bool> MoveWidgetAsync(Guid instanceId, int column, int row, CancellationToken cancellationToken = default)
    {
        await _state.WaitAsync(cancellationToken);
        try
        {
            var page = _layout.Pages.FindIndex(p => p.Widgets.Any(w => w.InstanceId == instanceId));
            if (page < 0)
                return false;
            var placement = _layout.Pages[page].Widgets.First(w => w.InstanceId == instanceId);
            if (column < 0 || row < 0 || column + placement.Size.Columns > WidgetGrid.Columns || row + placement.Size.Rows > WidgetGrid.Rows)
                return false;
            if (_layout.Pages[page].Widgets.Any(w => w != placement && w.Overlaps(column, row, placement.Size)))
                return false;
            placement.Column = column;
            placement.Row = row;
            await CommitLayoutAsync(page, cancellationToken);
            return true;
        }
        finally
        {
            _state.Release();
        }
    }

    /// <summary>
    /// Muda o tamanho de um widget no lugar onde ele está. A instância é recriada no tamanho novo
    /// (o contrato fixa o tamanho por instância), mas mantém o id, as configurações e os arquivos.
    /// False se o widget não suporta o tamanho ou se ele não cabe ali.
    /// </summary>
    public async Task<bool> ResizeWidgetAsync(Guid instanceId, WidgetSize size, CancellationToken cancellationToken = default)
    {
        await _state.WaitAsync(cancellationToken);
        try
        {
            var page = _layout.Pages.FindIndex(p => p.Widgets.Any(w => w.InstanceId == instanceId));
            if (page < 0)
                return false;
            var placement = _layout.Pages[page].Widgets.First(w => w.InstanceId == instanceId);
            if (placement.Size == size)
                return true;
            if (_catalog.Find(placement.WidgetId) is not { } widget || !widget.Manifest.SupportedSizes.Contains(size))
                return false;
            if (placement.Column + size.Columns > WidgetGrid.Columns || placement.Row + size.Rows > WidgetGrid.Rows)
                return false;
            if (_layout.Pages[page].Widgets.Any(w => w != placement && w.Overlaps(placement.Column, placement.Row, size)))
                return false;

            if (_runtimes.Remove(instanceId, out var old))
            {
                if (ReferenceEquals(_fullscreen, old))
                    _fullscreen = null;
                if (ReferenceEquals(_pressTarget, old))
                    _pressTarget = null;
                await old.DisposeAsync(); // sem OnRemovedAsync: os dados da instância ficam
            }

            placement.Size = size;
            await CreateRuntimeAsync(placement, cancellationToken);
            if (_runtimes.TryGetValue(instanceId, out var runtime))
            {
                var visible = _active && page == CurrentPage && _fullscreen is null;
                runtime.Context.IsAsleep = !visible;
                runtime.LastFrame = await runtime.RenderAsync(RenderReason.Initial, cancellationToken);
                if (visible)
                    await runtime.CallAsync("OnWakeAsync", (i, ct) => i.OnWakeAsync(ct), cancellationToken);
                else
                    await runtime.CallAsync("OnSleepAsync", (i, ct) => i.OnSleepAsync(ct), cancellationToken);
            }
            await CommitLayoutAsync(page, cancellationToken);
            return true;
        }
        finally
        {
            _state.Release();
        }
    }

    /// <summary>Nova página. Sem nome (ou com um já usado), ganha o primeiro "Página N" livre.</summary>
    public async Task<int> AddPageAsync(string? name = null, CancellationToken cancellationToken = default)
    {
        await _state.WaitAsync(cancellationToken);
        try
        {
            name = name?.Trim();
            _layout.Pages.Add(new LayoutPage { Name = string.IsNullOrEmpty(name) ? NextPageName() : UniquePageName(name) });
            await CommitLayoutAsync(-1, cancellationToken);
            return _layout.Pages.Count - 1;
        }
        finally
        {
            _state.Release();
        }
    }

    /// <summary>Remove uma página vazia (precisa sobrar pelo menos uma).</summary>
    public async Task<bool> RemovePageAsync(int page, CancellationToken cancellationToken = default)
    {
        await _state.WaitAsync(cancellationToken);
        try
        {
            if (_layout.Pages.Count <= 1 || page < 0 || page >= _layout.Pages.Count || _layout.Pages[page].Widgets.Count > 0)
                return false;
            _layout.Pages.RemoveAt(page);
            if (CurrentPage >= _layout.Pages.Count)
                CurrentPage = _layout.Pages.Count - 1;
            await CommitLayoutAsync(CurrentPage, cancellationToken);
            return true;
        }
        finally
        {
            _state.Release();
        }
    }

    /// <summary>Renomeia. Recusa (false) nome vazio ou já usado por outra página: não existem duas com o mesmo nome.</summary>
    public async Task<bool> RenamePageAsync(int page, string name, CancellationToken cancellationToken = default)
    {
        await _state.WaitAsync(cancellationToken);
        try
        {
            name = name.Trim();
            if (page < 0 || page >= _layout.Pages.Count || name.Length == 0 || IsPageNameTaken(name, page))
                return false;
            _layout.Pages[page].Name = name;
            await CommitLayoutAsync(-1, cancellationToken);
            return true;
        }
        finally
        {
            _state.Release();
        }
    }

    public bool IsPageNameTaken(string name, int exceptPage = -1) =>
        _layout.Pages.Where((_, i) => i != exceptPage).Any(p => string.Equals(p.Name.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase));

    private string NextPageName()
    {
        for (var n = 1; ; n++)
            if (!IsPageNameTaken($"Página {n}"))
                return $"Página {n}";
    }

    private string UniquePageName(string name, int exceptPage = -1)
    {
        if (!IsPageNameTaken(name, exceptPage))
            return name;
        for (var n = 2; ; n++)
            if (!IsPageNameTaken($"{name} {n}", exceptPage))
                return $"{name} {n}";
    }

    // Layouts antigos podem ter nomes repetidos (o "Nova página" de antes gerava): a segunda em diante ganha número.
    private bool FixDuplicatePageNames()
    {
        var changed = false;
        for (var i = 1; i < _layout.Pages.Count; i++)
        {
            var page = _layout.Pages[i];
            if (_layout.Pages.Take(i).Any(p => string.Equals(p.Name.Trim(), page.Name.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            {
                var unique = UniquePageName(page.Name.Trim(), i);
                _logger.LogInformation("Página repetida \"{Name}\" renomeada para \"{Unique}\"", page.Name, unique);
                page.Name = unique;
                changed = true;
            }
        }
        return changed;
    }

    // Grava o layout e avisa a UI na hora. O redesenho do aparelho (página inteira pelo USB) vai em
    // segundo plano: quem editou não espera por ele, e edições seguidas viram um redesenho só.
    private Task CommitLayoutAsync(int changedPage, CancellationToken cancellationToken)
    {
        _store.Save(_layout);
        LayoutChanged?.Invoke();
        if (_active && changedPage == CurrentPage && Interlocked.Exchange(ref _redrawPending, 1) == 0)
            _ = Task.Run(() => RedrawAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private int _redrawPending;

    private async Task RedrawAsync(CancellationToken cancellationToken)
    {
        await _state.WaitAsync(cancellationToken);
        try
        {
            Interlocked.Exchange(ref _redrawPending, 0);
            if (_active)
                await ShowCurrentAsync(Transition.None, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            _logger.LogDebug("Redesenho depois de editar o layout falhou: {Message}", ex.Message);
        }
        finally
        {
            _state.Release();
        }
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
        var runtime = new WidgetRuntime(widget, placement, context, logger) { FrameChanged = id => FrameChanged?.Invoke(id) };
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
