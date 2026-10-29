using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OpenWigi.Agent;
using OpenWigi.Device.Display;
using OpenWigi.Device.Protocol;
using OpenWigi.Host;
using OpenWigi.Host.Display;
using OpenWigi.Ipc;

// Agente de cada usuário: fala com o serviço e, quando tem a tela, desenha o painel com os
// widgets instalados. Sem widgets, mostra a tela de teste da fase 3 (cor por usuário, nome, hora
// e último gesto). A UI de configuração (WPF + Blazor) chega na fase 5.
//   --preview   janela com o que está no aparelho; o mouse nela vira toque
//   --plugins   pasta de plugins (padrão: %LOCALAPPDATA%\OpenWigiDash\Widgets)

const int DemoPage = 0;
var user = Environment.UserName;
var background = ColorFor(user);
var lastGesture = "toque na tela";
var isOwner = false;
IpcConnection? current = null;
var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

using var loggerFactory = LoggerFactory.Create(b => b
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss.fff "; })
    .SetMinimumLevel(LogLevel.Information));
var log = loggerFactory.CreateLogger("Agente");

var pluginsDirectory = Option("--plugins") ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenWigiDash", "Widgets");
var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenWigiDash");

await using var dashboard = new DashboardHost(
    new DashboardOptions { PluginsDirectory = pluginsDirectory, DataDirectory = dataDirectory },
    new IpcSink(() => current), loggerFactory);
await dashboard.StartAsync(stop.Token);
var useDashboard = dashboard.AvailableWidgets.Count > 0;
log.LogInformation(useDashboard
    ? $"{dashboard.AvailableWidgets.Count} widgets em {pluginsDirectory}"
    : $"nenhum widget em {pluginsDirectory}; usando a tela de teste");

var preview = args.Contains("--preview") ? OpenPreview($"OpenWigiDash — prévia ({user})", stop) : null;
if (preview is not null)
{
    preview.Touch += (contact, x, y) =>
    {
        if (isOwner && current is { } connection)
            _ = connection.SendAsync(new EmulatedTouch(contact, x, y));
    };
}

while (!stop.IsCancellationRequested)
{
    try
    {
        await RunAsync(stop.Token);
    }
    catch (Exception ex) when (ex is IOException or TimeoutException or EndOfStreamException)
    {
        log.LogWarning("sem conexão com o serviço ({Message}); tentando de novo", ex.Message);
    }
    catch (OperationCanceledException)
    {
        break;
    }

    current = null;
    if (isOwner)
        await dashboard.ActivateAsync(false);
    isOwner = false;
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stop.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }
}

async Task RunAsync(CancellationToken cancellationToken)
{
    var pipe = new NamedPipeClientStream(".", IpcConnection.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(TimeSpan.FromSeconds(5), cancellationToken);
    await using var connection = new IpcConnection(pipe);
    await connection.SendAsync(new Hello(IpcConnection.ProtocolVersion, user), cancellationToken);
    if (preview is not null)
        await connection.SendAsync(new SubscribePreview(true), cancellationToken);
    current = connection;

    using var refresh = new PeriodicTimer(TimeSpan.FromSeconds(10));
    if (!useDashboard)
    {
        _ = Task.Run(async () =>
        {
            while (await refresh.WaitForNextTickAsync(cancellationToken))
                if (isOwner)
                    await DrawDemoAsync(connection, cancellationToken);
        }, cancellationToken);
    }

    while (await connection.ReceiveAsync(cancellationToken) is { } message)
    {
        switch (message)
        {
            case Welcome welcome:
                log.LogInformation("conectado ao serviço como sessão {Session}; aparelho {Device}", welcome.SessionId,
                    welcome.Device.Connected ? welcome.Device.Firmware : "desconectado");
                break;
            case Ownership ownership:
                isOwner = ownership.IsOwner;
                log.LogInformation(ownership.IsOwner ? "ganhei a tela ({Reason})" : "perdi a tela ({Reason})", ownership.Reason);
                preview?.SetStatus(ownership.IsOwner
                    ? $"OpenWigiDash — prévia ({user}): com a tela"
                    : $"OpenWigiDash — prévia ({user}): sem a tela — {ownership.Reason}");
                if (useDashboard)
                    await dashboard.ActivateAsync(ownership.IsOwner, cancellationToken);
                else if (isOwner)
                    await DrawDemoAsync(connection, cancellationToken, show: true);
                break;
            case Gesture gesture:
                if (useDashboard)
                {
                    _ = dashboard.HandleGestureAsync(gesture.Event, cancellationToken);
                }
                else
                {
                    lastGesture = $"{gesture.Event.Kind} em {gesture.Event.X},{gesture.Event.Y} ({gesture.Event.Duration.TotalMilliseconds:F0} ms)";
                    log.LogInformation("{Gesture}", lastGesture);
                    await DrawDemoAsync(connection, cancellationToken);
                }
                break;
            case PreviewFrame frame:
                preview?.ShowFrame(frame.Pixels);
                break;
            case DeviceStatus status:
                log.LogInformation(status.Connected ? "aparelho conectado ({Firmware})" : "aparelho desconectado", status.Firmware);
                break;
            case IpcError error:
                log.LogError("erro do serviço: {Message}", error.Message);
                return;
        }
    }
}

async Task DrawDemoAsync(IpcConnection connection, CancellationToken cancellationToken, bool show = false)
{
    await connection.SendAsync(new Frame(DemoPage, 0, RenderDemo()), cancellationToken);
    if (show)
        await connection.SendAsync(new ShowPage(DemoPage, Transition.None), cancellationToken);
}

byte[] RenderDemo()
{
    const int w = WigiDashProtocol.Width, h = WigiDashProtocol.Height;
    using var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bitmap))
    {
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(background);
        using var big = new Font("Segoe UI", 72, FontStyle.Bold, GraphicsUnit.Pixel);
        using var small = new Font("Segoe UI", 30, GraphicsUnit.Pixel);
        using var center = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(user, big, Brushes.White, new RectangleF(0, 150, w, 100), center);
        g.DrawString(DateTime.Now.ToString("HH:mm:ss"), small, Brushes.White, new RectangleF(0, 270, w, 40), center);
        g.DrawString(lastGesture, small, Brushes.White, new RectangleF(0, 340, w, 40), center);
    }

    var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    try
    {
        var source = new byte[data.Stride * h];
        Marshal.Copy(data.Scan0, source, 0, source.Length);
        var frame = new byte[WigiDashProtocol.FrameBytes];
        Rgb565.FromBgra32(source, w, h, data.Stride, frame);
        return frame;
    }
    finally
    {
        bitmap.UnlockBits(data);
    }
}

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static Color ColorFor(string name)
{
    // Cor estável por usuário, escura o bastante para o texto branco.
    var hash = name.Aggregate(17, (acc, c) => acc * 31 + char.ToLowerInvariant(c));
    Color[] palette = [Color.FromArgb(30, 90, 160), Color.FromArgb(150, 50, 90), Color.FromArgb(40, 120, 70),
        Color.FromArgb(130, 80, 20), Color.FromArgb(90, 60, 150), Color.FromArgb(20, 110, 120)];
    return palette[Math.Abs(hash % palette.Length)];
}

static PreviewWindow OpenPreview(string title, CancellationTokenSource stop)
{
    PreviewWindow? window = null;
    using var ready = new ManualResetEventSlim();
    var ui = new Thread(() =>
    {
        Application.EnableVisualStyles();
        window = new PreviewWindow(title);
        window.HandleCreated += (_, _) => ready.Set();
        window.FormClosed += (_, _) => stop.Cancel();
        Application.Run(window);
    }) { IsBackground = true, Name = "Prévia" };
    ui.SetApartmentState(ApartmentState.STA);
    ui.Start();
    ready.Wait();
    return window!;
}

/// <summary>Leva os pixels do compositor ao serviço pelo canal atual; sem canal, descarta.</summary>
internal sealed class IpcSink(Func<IpcConnection?> connection) : IDisplaySink
{
    public async ValueTask WriteAsync(int page, int byteOffset, ReadOnlyMemory<byte> rgb565, CancellationToken cancellationToken)
    {
        if (connection() is { } c)
            await c.SendAsync(new Frame(page, byteOffset, rgb565.ToArray()), cancellationToken);
    }

    public async ValueTask ShowAsync(int page, Transition transition, CancellationToken cancellationToken)
    {
        if (connection() is { } c)
            await c.SendAsync(new ShowPage(page, transition), cancellationToken);
    }
}
