using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using OpenWigi.Samples.Clock;
using OpenWigi.Sdk;

[assembly: OpenWigiWidget(typeof(ClockWidget))]
[assembly: OpenWigiWidget(typeof(CounterWidget))]

namespace OpenWigi.Samples.Clock;

/// <summary>Relógio que empurra um quadro por segundo enquanto está acordado.</summary>
public sealed class ClockWidget : IWidget
{
    public WidgetManifest Manifest { get; } = new()
    {
        Id = new("4f0f9a5e-2a7b-4c11-9a51-3c1d7a9e2b10"),
        Name = "Relógio (exemplo)",
        Version = new(1, 0),
        Author = "OpenWigiDashManager",
        Description = "Hora e data; exemplo de widget que empurra quadros.",
        SupportedSizes = [WidgetSize.Wide, WidgetSize.Small, WidgetSize.Large],
    };

    private WidgetTheme _theme = new();

    public ValueTask LoadAsync(IWidgetHost host, CancellationToken cancellationToken)
    {
        _theme = host.Theme;
        return ValueTask.CompletedTask;
    }

    /// <summary>Ícone do catálogo: um relógio parado. O Contador não tem e ganha o ícone genérico.</summary>
    public ValueTask<WidgetFrame?> GetPreviewAsync(WidgetSize size, PixelSize pixelSize, CancellationToken cancellationToken) =>
        ValueTask.FromResult<WidgetFrame?>(Painter.Paint(pixelSize, _theme, (g, pixels) =>
        {
            using var font = new Font(_theme.PrimaryFontFamily, pixels.Height * 0.42f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("12:00", font, Brushes.White, new RectangleF(0, 0, pixels.Width, pixels.Height), center);
        }));

    public ValueTask<IWidgetInstance> CreateInstanceAsync(IWidgetContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IWidgetInstance>(new ClockInstance(context));
}

internal sealed class ClockInstance(IWidgetContext context) : IWidgetInstance
{
    private CancellationTokenSource? _ticking;

    public ValueTask<WidgetFrame> RenderAsync(RenderReason reason, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Draw());

    public ValueTask OnWakeAsync(CancellationToken cancellationToken)
    {
        _ticking?.Cancel();
        _ticking = CancellationTokenSource.CreateLinkedTokenSource(context.Stopping);
        var token = _ticking.Token;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), context.Host.Time);
            while (await timer.WaitForNextTickAsync(token))
                await context.PresentAsync(Draw(), token);
        }, token);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnSleepAsync(CancellationToken cancellationToken)
    {
        _ticking?.Cancel();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _ticking?.Cancel();
        return ValueTask.CompletedTask;
    }

    private WidgetFrame Draw()
    {
        var now = context.Host.Time.GetLocalNow();
        return Painter.Paint(context.PixelSize, context.Theme, (g, size) =>
        {
            var big = size.Height * 0.42f;
            using var hour = new Font(context.Theme.PrimaryFontFamily, big, FontStyle.Bold, GraphicsUnit.Pixel);
            using var date = new Font(context.Theme.PrimaryFontFamily, big * 0.32f, GraphicsUnit.Pixel);
            using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(now.ToString("HH:mm:ss"), hour, Brushes.White, new RectangleF(0, 0, size.Width, size.Height * 0.72f), center);
            g.DrawString(now.ToString("ddd, dd MMM"), date, Brushes.Gainsboro, new RectangleF(0, size.Height * 0.62f, size.Width, size.Height * 0.3f), center);
        });
    }
}

/// <summary>Contador: tap soma, long-press zera. Mostra o anel de progresso enquanto o dedo segura.</summary>
public sealed class CounterWidget : IWidget
{
    public WidgetManifest Manifest { get; } = new()
    {
        Id = new("9c3b1e47-6d20-4a8e-b5f4-2e7c0a91d6f3"),
        Name = "Contador (exemplo)",
        Version = new(1, 0),
        Author = "OpenWigiDashManager",
        Description = "Tap soma um, long-press zera. Exemplo de toques ricos.",
        SupportedSizes = [WidgetSize.Small, WidgetSize.Wide, WidgetSize.Mini],
    };

    public ValueTask<IWidgetInstance> CreateInstanceAsync(IWidgetContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IWidgetInstance>(new CounterInstance(context));
}

public sealed class CounterInstance(IWidgetContext context) : IWidgetInstance
{
    private int? _count;
    private double _progress;
    private string _last = "toque ou segure";
    private string? _label;

    public int Count => _count ?? 0;

    /// <summary>Último gesto recebido, como aparece no quadro.</summary>
    public string LastGesture => _last;

    /// <summary>Tela de configurações em Razor (CounterSettings.razor).</summary>
    public Type? SettingsComponent => typeof(CounterSettings);

    public async ValueTask<WidgetFrame> RenderAsync(RenderReason reason, CancellationToken cancellationToken)
    {
        _count ??= await context.Settings.GetJsonAsync("count", 0, cancellationToken);
        _label ??= await context.Settings.GetAsync("label", cancellationToken) ?? string.Empty;
        return Draw();
    }

    public async Task SetLabelAsync(string label)
    {
        _label = label;
        await context.Settings.SetAsync("label", label);
        context.RequestRender();
    }

    public async Task ResetAsync()
    {
        _count = 0;
        _last = "zerado nas configurações";
        await context.Settings.SetJsonAsync("count", 0);
        context.RequestRender();
    }

    public async ValueTask OnTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        switch (touch.Kind)
        {
            case TouchKind.Tap:
                _count = (_count ?? 0) + 1;
                _last = "tap";
                break;
            case TouchKind.DoubleTap:
                _last = "double tap";
                break;
            case TouchKind.PressProgress:
                _progress = touch.Progress;
                break;
            case TouchKind.LongPress:
                _count = 0;
                _progress = 0;
                _last = "long-press: zerado";
                break;
            case TouchKind.PressEnded:
                _progress = 0;
                break;
            default:
                return;
        }

        if (touch.Kind is TouchKind.Tap or TouchKind.LongPress)
            await context.Settings.SetJsonAsync("count", _count, cancellationToken: cancellationToken);
        await context.PresentAsync(Draw(), cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private WidgetFrame Draw() => Painter.Paint(context.PixelSize, context.Theme, (g, size) =>
    {
        using var number = new Font(context.Theme.PrimaryFontFamily, size.Height * 0.45f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var small = new Font(context.Theme.PrimaryFontFamily, 14, GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString((_count ?? 0).ToString(), number, Brushes.White, new RectangleF(0, 0, size.Width, size.Height * 0.8f), center);
        g.DrawString(string.IsNullOrEmpty(_label) ? _last : $"{_label} · {_last}", small, Brushes.Gainsboro, new RectangleF(0, size.Height * 0.72f, size.Width, size.Height * 0.25f), center);

        if (_progress > 0)
        {
            // Anel do long-press: enche de 0 a 360 graus enquanto o dedo segura.
            var ring = Math.Min(size.Width, size.Height) - 24;
            using var pen = new Pen(Color.FromArgb(230, 60, 60), 8);
            g.DrawArc(pen, (size.Width - ring) / 2f, (size.Height - ring) / 2f, ring, ring, -90, (float)(360 * _progress));
        }
    });
}

/// <summary>Desenha com System.Drawing e entrega como quadro Bgra32 do SDK.</summary>
internal static class Painter
{
    public static WidgetFrame Paint(PixelSize size, WidgetTheme theme, Action<Graphics, PixelSize> draw)
    {
        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            // O quadro inteiro é conteúdo: a moldura e o canto arredondado são do host.
            var bg = theme.PrimaryBackground;
            g.Clear(Color.FromArgb(bg.R, bg.G, bg.B));
            draw(g, size);
        }

        var data = bitmap.LockBits(new Rectangle(0, 0, size.Width, size.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[data.Stride * size.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return new WidgetFrame(size, FramePixelFormat.Bgra32, pixels, data.Stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
