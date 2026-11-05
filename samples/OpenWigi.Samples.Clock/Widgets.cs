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

/// <summary>
/// Relógio que empurra um quadro por segundo enquanto está acordado. No 1x1 mostra só a hora (HH:mm);
/// nos outros tamanhos, hora com segundos e a data. Cor de fundo e de fonte escolhidas nas configurações.
/// </summary>
public sealed class ClockWidget : IWidget
{
    public WidgetManifest Manifest { get; } = new()
    {
        Id = new("4f0f9a5e-2a7b-4c11-9a51-3c1d7a9e2b10"),
        Name = "Relógio (exemplo)",
        Version = new(1, 1),
        Author = "OpenWigiDashManager",
        Description = "Hora e data, ou só a hora no 1x1, com cor de fundo e de fonte.",
        // O primeiro é o tamanho de quem adiciona sem escolher; o 1x1 vai no fim para não mudar isso.
        SupportedSizes = [WidgetSize.Wide, WidgetSize.Small, WidgetSize.Large, WidgetSize.Mini],
    };

    private WidgetTheme _theme = new();

    public ValueTask LoadAsync(IWidgetHost host, CancellationToken cancellationToken)
    {
        _theme = host.Theme;
        return ValueTask.CompletedTask;
    }

    /// <summary>Ícone do catálogo: um relógio parado. O Contador não tem e ganha o ícone genérico.</summary>
    public ValueTask<WidgetFrame?> GetPreviewAsync(WidgetSize size, PixelSize pixelSize, CancellationToken cancellationToken) =>
        ValueTask.FromResult<WidgetFrame?>(Painter.Paint(pixelSize, _theme.PrimaryBackground, (g, pixels) =>
            ClockFace.DrawTime(g, "12:00", _theme.PrimaryFontFamily, _theme.PrimaryForeground,
                new RectangleF(0, 0, pixels.Width, pixels.Height))));

    public ValueTask<IWidgetInstance> CreateInstanceAsync(IWidgetContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IWidgetInstance>(new ClockInstance(context));
}

public sealed class ClockInstance(IWidgetContext context) : IWidgetInstance
{
    internal const string BackgroundKey = "background";
    internal const string ForegroundKey = "foreground";

    private CancellationTokenSource? _ticking;
    private bool _loaded;
    private WidgetColor? _background;
    private WidgetColor? _foreground;
    private string? _shown;

    /// <summary>Cor de fundo escolhida, ou <c>null</c> para seguir o tema do manager.</summary>
    public WidgetColor? CustomBackground => _background;

    /// <summary>Cor da fonte escolhida, ou <c>null</c> para seguir o tema do manager.</summary>
    public WidgetColor? CustomForeground => _foreground;

    public WidgetColor Background => _background ?? context.Theme.PrimaryBackground;
    public WidgetColor Foreground => _foreground ?? context.Theme.PrimaryForeground;

    /// <summary>1x1: só a hora, sem segundos e sem data.</summary>
    private bool TimeOnly => context.Size == WidgetSize.Mini;

    /// <summary>Tela de configurações em Razor (ClockSettings.razor).</summary>
    public Type? SettingsComponent => typeof(ClockSettings);

    public async ValueTask<WidgetFrame> RenderAsync(RenderReason reason, CancellationToken cancellationToken)
    {
        if (!_loaded)
        {
            _background = ClockColors.Parse(await context.Settings.GetAsync(BackgroundKey, cancellationToken));
            _foreground = ClockColors.Parse(await context.Settings.GetAsync(ForegroundKey, cancellationToken));
            _loaded = true;
        }
        return Draw(context.Host.Time.GetLocalNow());
    }

    /// <summary>Troca a cor de fundo; <c>null</c> volta para a do tema.</summary>
    public Task SetBackgroundAsync(WidgetColor? color) => SaveAsync(BackgroundKey, _background = color);

    /// <summary>Troca a cor da fonte; <c>null</c> volta para a do tema.</summary>
    public Task SetForegroundAsync(WidgetColor? color) => SaveAsync(ForegroundKey, _foreground = color);

    private async Task SaveAsync(string key, WidgetColor? color)
    {
        if (color is { } c)
            await context.Settings.SetAsync(key, c.ToString());
        else
            await context.Settings.RemoveAsync(key);
        context.RequestRender();
    }

    public ValueTask OnWakeAsync(CancellationToken cancellationToken)
    {
        _ticking?.Cancel();
        _ticking = CancellationTokenSource.CreateLinkedTokenSource(context.Stopping);
        var token = _ticking.Token;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), context.Host.Time);
            while (await timer.WaitForNextTickAsync(token))
            {
                // No 1x1 o texto só muda uma vez por minuto: não manda quadro igual pelo USB.
                var now = context.Host.Time.GetLocalNow();
                if (TimeOnly && Text(now) == _shown)
                    continue;
                await context.PresentAsync(Draw(now), token);
            }
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

    private string Text(DateTimeOffset now) => now.ToString(TimeOnly ? "HH:mm" : "HH:mm:ss");

    private WidgetFrame Draw(DateTimeOffset now)
    {
        var text = Text(now);
        var font = context.Theme.PrimaryFontFamily;
        var foreground = Foreground;
        var frame = Painter.Paint(context.PixelSize, Background, (g, size) =>
        {
            if (TimeOnly)
            {
                ClockFace.DrawTime(g, text, font, foreground, new RectangleF(0, 0, size.Width, size.Height));
                return;
            }

            var big = size.Height * 0.42f;
            using var hour = new Font(font, big, FontStyle.Bold, GraphicsUnit.Pixel);
            using var date = new Font(font, big * 0.32f, GraphicsUnit.Pixel);
            using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var main = new SolidBrush(ClockColors.ToDrawing(foreground));
            // A data vem na mesma cor, um pouco apagada, como era o cinza claro sobre o branco.
            using var dim = new SolidBrush(Color.FromArgb(200, ClockColors.ToDrawing(foreground)));
            g.DrawString(text, hour, main, new RectangleF(0, 0, size.Width, size.Height * 0.72f), center);
            g.DrawString(now.ToString("ddd, dd MMM"), date, dim, new RectangleF(0, size.Height * 0.62f, size.Width, size.Height * 0.3f), center);
        });
        _shown = text;
        return frame;
    }
}

/// <summary>Desenho e cores do relógio, divididos entre o catálogo, o widget e a tela de configurações.</summary>
internal static class ClockFace
{
    /// <summary>A hora em negrito, centralizada e do maior tamanho que cabe em <paramref name="area"/>.</summary>
    public static void DrawTime(Graphics g, string text, string fontFamily, WidgetColor color, RectangleF area)
    {
        // Mede num tamanho de referência e escala: 90% da largura e 80% da altura, o que for menor.
        const float reference = 100f;
        SizeF measured;
        using (var probe = new Font(fontFamily, reference, FontStyle.Bold, GraphicsUnit.Pixel))
            measured = g.MeasureString(text, probe, PointF.Empty, StringFormat.GenericTypographic);
        var scale = Math.Min(area.Width * 0.9f / Math.Max(measured.Width, 1), area.Height * 0.8f / Math.Max(measured.Height, 1));
        using var font = new Font(fontFamily, Math.Max(reference * scale, 6f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var center = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        using var brush = new SolidBrush(ClockColors.ToDrawing(color));
        g.DrawString(text, font, brush, area, center);
    }
}

internal static class ClockColors
{
    /// <summary>Lê "#RRGGBB"; <c>null</c> se não houver ou não servir.</summary>
    public static WidgetColor? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length != 7 || text[0] != '#'
            || !int.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return null;
        return new WidgetColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    public static Color ToDrawing(WidgetColor color) => Color.FromArgb(color.R, color.G, color.B);
}

/// <summary>Contador: tap soma, long-press zera, swipe para cima entra e sai da tela cheia. Mostra o anel de progresso enquanto o dedo segura.</summary>
public sealed class CounterWidget : IWidget
{
    public WidgetManifest Manifest { get; } = new()
    {
        Id = new("9c3b1e47-6d20-4a8e-b5f4-2e7c0a91d6f3"),
        Name = "Contador (exemplo)",
        Version = new(1, 0),
        Author = "OpenWigiDashManager",
        Description = "Tap soma um, long-press zera, swipe para cima abre em tela cheia. Exemplo de toques ricos.",
        SupportedSizes = [WidgetSize.Small, WidgetSize.Wide],
        SupportsFullscreen = true,
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
            case TouchKind.SwipeUp:
                // Pedido de dentro do toque: o host troca a tela quando este OnTouchAsync voltar.
                await context.RequestFullscreenAsync(!context.IsFullscreen, cancellationToken);
                return;
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
    public static WidgetFrame Paint(PixelSize size, WidgetTheme theme, Action<Graphics, PixelSize> draw) =>
        Paint(size, theme.PrimaryBackground, draw);

    public static WidgetFrame Paint(PixelSize size, WidgetColor background, Action<Graphics, PixelSize> draw)
    {
        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            // O quadro inteiro é conteúdo: a moldura e o canto arredondado são do host.
            g.Clear(Color.FromArgb(background.R, background.G, background.B));
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
