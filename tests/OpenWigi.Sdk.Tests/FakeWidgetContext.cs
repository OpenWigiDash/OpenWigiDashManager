using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace OpenWigi.Sdk.Tests;

/// <summary>Host de mentira, só o bastante para exercitar o contrato.</summary>
internal sealed class FakeWidgetContext(WidgetSize size) : IWidgetContext, IWidgetHost
{
    public List<WidgetFrame> Presented { get; } = [];
    public int RenderRequests { get; private set; }
    public FakeSettings FakeSettings { get; } = new();

    public Guid InstanceId { get; } = Guid.NewGuid();
    public IWidgetHost Host => this;
    public WidgetSize Size { get; } = size;
    public PixelSize PixelSize => IsFullscreen ? WidgetGrid.Screen : Frame.ContentSize(Size.CellSpan);
    public TileFrame Frame { get; set; } = TileFrame.Default;
    public bool IsFullscreen { get; set; }
    public bool IsAsleep { get; set; }
    public WidgetTheme Theme { get; } = new();
    public IWidgetSettings Settings => FakeSettings;
    public IWidgetFiles Files => throw new NotSupportedException();
    public IWidgetActions Actions => throw new NotSupportedException();
    public ILogger Logger => NullLogger.Instance;
    public CancellationToken Stopping => CancellationToken.None;

    public string ResourceDirectory => AppContext.BaseDirectory;
    public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;
    public TimeProvider Time { get; } = new FakeTimeProvider();

    public ValueTask PresentAsync(WidgetFrame frame, CancellationToken cancellationToken = default)
    {
        if (frame.Size != PixelSize)
            throw new ArgumentException($"Quadro {frame.Size}, esperado {PixelSize}.", nameof(frame));
        if (!IsAsleep)
            Presented.Add(frame);
        return ValueTask.CompletedTask;
    }

    public void RequestRender() => RenderRequests++;

    public ValueTask<bool> RequestFullscreenAsync(bool fullscreen, CancellationToken cancellationToken = default)
    {
        IsFullscreen = fullscreen;
        return ValueTask.FromResult(true);
    }
}

internal sealed class FakeSettings : IWidgetSettings
{
    public Dictionary<string, (string Value, bool Cloneable)> Values { get; } = [];

    public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Values.TryGetValue(key, out var v) ? v.Value : null);

    public ValueTask SetAsync(string key, string value, bool cloneable = true, CancellationToken cancellationToken = default)
    {
        Values[key] = (value, cloneable);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Values.Remove(key));

    public ValueTask<IReadOnlyCollection<string>> GetKeysAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyCollection<string>>(Values.Keys.ToArray());
}
