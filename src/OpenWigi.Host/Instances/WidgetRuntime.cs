using Microsoft.Extensions.Logging;
using OpenWigi.Host.Layout;
using OpenWigi.Host.Plugins;
using OpenWigi.Sdk;

namespace OpenWigi.Host.Instances;

/// <summary>
/// Uma instância viva: o objeto do plugin, o contexto dela e o último quadro. Garante a regra do
/// contrato de uma chamada por vez e isola falhas do plugin (logadas, não derrubam o host).
/// </summary>
internal sealed class WidgetRuntime : IAsyncDisposable
{
    private readonly SemaphoreSlim _calls = new(1, 1);
    private readonly ILogger _logger;
    private int _renderRequested;

    public WidgetRuntime(LoadedWidget widget, WidgetPlacement placement, WidgetContext context, ILogger logger)
    {
        Widget = widget;
        Placement = placement;
        Context = context;
        _logger = logger;
    }

    public LoadedWidget Widget { get; }
    public WidgetPlacement Placement { get; }
    public WidgetContext Context { get; }
    public IWidgetInstance? Instance { get; private set; }
    public WidgetFrame? LastFrame { get; set; }
    public string Name => Widget.Manifest.Name;

    public async Task<bool> CreateAsync(CancellationToken cancellationToken)
    {
        try
        {
            Instance = await Widget.Widget.CreateInstanceAsync(Context, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "{Widget}: falha ao criar a instância {Instance}", Name, Placement.InstanceId);
            return false;
        }
    }

    /// <summary>Roda uma chamada no plugin, uma por vez. Devolve false se o plugin falhou.</summary>
    public async Task<bool> CallAsync(string what, Func<IWidgetInstance, CancellationToken, ValueTask> call, CancellationToken cancellationToken)
    {
        if (Instance is not { } instance)
            return false;
        await _calls.WaitAsync(cancellationToken);
        try
        {
            await call(instance, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "{Widget}: falha em {Call}", Name, what);
            return false;
        }
        finally
        {
            _calls.Release();
        }
    }

    public async Task<WidgetFrame?> RenderAsync(RenderReason reason, CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _renderRequested, 0);
        WidgetFrame? frame = null;
        await CallAsync($"RenderAsync({reason})", async (i, ct) => frame = await i.RenderAsync(reason, ct), cancellationToken);
        if (frame is not null && frame.Size != Context.PixelSize)
        {
            _logger.LogWarning("{Widget}: quadro {Got} descartado, esperado {Expected}", Name, frame.Size, Context.PixelSize);
            return null;
        }
        return frame;
    }

    /// <summary>Marca um pedido de render; devolve true só para o primeiro de uma rajada.</summary>
    public bool TryRequestRender() => Interlocked.Exchange(ref _renderRequested, 1) == 0;

    public async ValueTask DisposeAsync()
    {
        Context.Stop();
        if (Instance is { } instance)
        {
            await _calls.WaitAsync();
            try
            {
                await instance.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Widget}: falha ao descartar", Name);
            }
            finally
            {
                _calls.Release();
            }
        }
        Instance = null;
    }
}
