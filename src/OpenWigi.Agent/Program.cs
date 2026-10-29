using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using OpenWigi.Agent;
using OpenWigi.Device.Display;
using OpenWigi.Device.Protocol;
using OpenWigi.Ipc;

// Agente mínimo da fase 3: prova a troca de dono entre sessões. Pinta a tela com uma cor por
// usuário, mostra o nome, a hora e o último gesto. A UI de verdade (WPF + Blazor) chega na fase 5.
// Com --preview abre uma janela com o que está no aparelho; o mouse nela vira toque.

const int Page = 0;
var user = Environment.UserName;
var background = ColorFor(user);
var lastGesture = "toque na tela";
var isOwner = false;
IpcConnection? current = null;
var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

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
        Log($"sem conexão com o serviço ({ex.Message}); tentando de novo");
    }
    catch (OperationCanceledException)
    {
        break;
    }

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
    _ = Task.Run(async () =>
    {
        while (await refresh.WaitForNextTickAsync(cancellationToken))
            if (isOwner)
                await DrawAsync(connection, cancellationToken);
    }, cancellationToken);

    while (await connection.ReceiveAsync(cancellationToken) is { } message)
    {
        switch (message)
        {
            case Welcome welcome:
                Log($"conectado ao serviço como sessão {welcome.SessionId}; aparelho {(welcome.Device.Connected ? welcome.Device.Firmware : "desconectado")}");
                break;
            case Ownership ownership:
                isOwner = ownership.IsOwner;
                Log(ownership.IsOwner ? $"ganhei a tela ({ownership.Reason})" : $"perdi a tela ({ownership.Reason})");
                preview?.SetStatus(ownership.IsOwner
                    ? $"OpenWigiDash — prévia ({user}): com a tela"
                    : $"OpenWigiDash — prévia ({user}): sem a tela — {ownership.Reason}");
                if (isOwner)
                    await DrawAsync(connection, cancellationToken, show: true);
                break;
            case Gesture gesture:
                lastGesture = $"{gesture.Event.Kind} em {gesture.Event.X},{gesture.Event.Y} ({gesture.Event.Duration.TotalMilliseconds:F0} ms)";
                Log(lastGesture);
                await DrawAsync(connection, cancellationToken);
                break;
            case PreviewFrame frame:
                preview?.ShowFrame(frame.Pixels);
                break;
            case DeviceStatus status:
                Log(status.Connected ? $"aparelho conectado ({status.Firmware})" : "aparelho desconectado");
                break;
            case IpcError error:
                Log($"erro do serviço: {error.Message}");
                return;
        }
    }
}

async Task DrawAsync(IpcConnection connection, CancellationToken cancellationToken, bool show = false)
{
    await connection.SendAsync(new Frame(Page, 0, Render()), cancellationToken);
    if (show)
        await connection.SendAsync(new ShowPage(Page, Transition.None), cancellationToken);
}

byte[] Render()
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

static void Log(string text) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {text}");
