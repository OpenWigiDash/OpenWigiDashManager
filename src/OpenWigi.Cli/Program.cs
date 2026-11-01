using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpenWigi.Device;
using OpenWigi.Device.Display;
using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;
using OpenWigi.Device.Usb;

// owd: ferramenta de diagnóstico do WigiDash. Os comandos do aparelho precisam do manager fechado,
// porque só um processo consegue abrir o dispositivo WinUSB de cada vez. `owd plugin` não abre o
// aparelho e funciona com o manager aberto.

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
var rest = args.Skip(1).ToArray();

try
{
    return command switch
    {
        "list" => List(),
        "info" => Info(),
        "brightness" => Brightness(rest),
        "draw" => Draw(rest),
        "touch" => Touch(rest),
        "page" => Page(rest),
        "peek" => Peek(rest),
        "sleep" => WithDevice(d => d.Sleep()),
        "wake" => WithDevice(d => d.Wake()),
        "welcome" => WithDevice(d => d.ShowWelcome()),
        "plugin" => OpenWigi.Cli.PluginCommands.Run(rest),
        _ => Help(),
    };
}
catch (IOException ex)
{
    Console.Error.WriteLine($"erro: {ex.Message}");
    return 2;
}

static int Help()
{
    Console.WriteLine("""
        owd — diagnóstico do WigiDash

          owd list                         lista os aparelhos conectados
          owd info                         identificação, firmware e config
          owd brightness [0-100]           lê ou muda o brilho atual
          owd sleep | wake | welcome       dorme, acorda ou volta à tela de boas-vindas
          owd draw [arquivo|pattern] [--page N] [--band]
                                           desenha uma imagem (ou um padrão de teste)
                                           --band manda só uma faixa no meio, para testar escrita parcial
          owd page N [--transition east|west|north|south|none]
                                           mostra uma das 3 páginas do hardware
          owd touch [--raw] [--long MS]    mostra gestos ao vivo (Ctrl+C para sair)
          owd peek 0xNN TAMANHO [--repeat N]
                                           lê um comando de controle cru (investigação)

        """ + OpenWigi.Cli.PluginCommands.HelpText);
    return 1;
}

static int WithDevice(Action<WigiDashDevice> action)
{
    using var device = WigiDashDevice.Open();
    action(device);
    return 0;
}

static int List()
{
    var paths = WinUsbDeviceFinder.FindDevicePaths();
    if (paths.Count == 0)
    {
        Console.WriteLine("Nenhum WigiDash encontrado.");
        return 1;
    }
    foreach (var path in paths)
        Console.WriteLine(path);
    return 0;
}

static int Info()
{
    using var device = WigiDashDevice.Open();
    var info = device.Info;
    var config = device.GetConfig();
    Console.WriteLine($"""
        Id:           {info.Id}
        Firmware:     {info.Firmware}
        Modo:         {info.Mode}
        Apelido:      {config.Nickname}
        Brilho:       {device.GetBrightness()}% (padrão {config.DefaultBrightness}%)
        Timeout tela: {config.ScreenTimeoutSeconds}
        Offset:       {config.DisplayOffsetX}, {config.DisplayOffsetY}
        Config v:     {config.Version}
        """);
    return 0;
}

static int Brightness(string[] args)
{
    using var device = WigiDashDevice.Open();
    if (args.Length > 0 && int.TryParse(args[0], out var value))
        device.SetBrightness(value);
    Console.WriteLine($"Brilho: {device.GetBrightness()}%");
    return 0;
}

static int Page(string[] args)
{
    if (args.Length == 0 || !int.TryParse(args[0], out var page))
        return Help();
    using var device = WigiDashDevice.Open();
    device.ShowPage(page, ParseTransition(Option(args, "--transition")));
    return 0;
}

// Lê um comando de controle cru e mostra os bytes. Só leitura: serve para investigar o firmware.
static int Peek(string[] args)
{
    if (args.Length < 2 || !TryParseByte(args[0], out var request) || !int.TryParse(args[1], out var length))
        return Help();
    var path = WinUsbDeviceFinder.FindDevicePaths().FirstOrDefault() ?? throw new IOException("Nenhum WigiDash conectado.");
    using var transport = WinUsbTransport.Open(path);
    var buffer = new byte[length];
    var repeat = int.TryParse(Option(args, "--repeat"), out var r) ? r : 1;
    for (var i = 0; i < repeat; i++)
    {
        var read = transport.ControlIn(request, 0, buffer);
        Console.WriteLine($"0x{request:X2} [{read}]: {Convert.ToHexString(buffer, 0, read)}");
        if (i + 1 < repeat)
            Thread.Sleep(100);
    }
    return 0;
}

static bool TryParseByte(string text, out byte value) =>
    text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? byte.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out value)
        : byte.TryParse(text, out value);

static int Draw(string[] args)
{
    var page = int.TryParse(Option(args, "--page"), out var p) ? p : 0;
    var source = args.Where((a, i) => !a.StartsWith("--") && (i == 0 || args[i - 1] != "--page")).FirstOrDefault() ?? "pattern";

    using var device = WigiDashDevice.Open();
    device.Wake();
    var started = DateTime.UtcNow;
    if (args.Contains("--band"))
    {
        // Faixa vermelha de 100 linhas no meio da tela, por cima do que já estiver lá.
        const int firstRow = 246, rows = 100;
        var band = new byte[rows * WigiDashProtocol.Width * 2];
        Rgb565.Fill(band, 255, 0, 0);
        device.WriteFrame(page, band, firstRow * WigiDashProtocol.Width * 2);
    }
    else
    {
        device.WriteFrame(page, source == "pattern" ? TestPattern() : LoadImage(source));
    }
    device.ShowPage(page);
    Console.WriteLine($"Quadro enviado para a página {page} em {(DateTime.UtcNow - started).TotalMilliseconds:F0} ms.");

    if (int.TryParse(Option(args, "--hold"), out var seconds))
    {
        // Mantém o aparelho aberto e mostra o que o firmware diz estar na tela.
        device.ScreenChanged += s => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} tela -> {s}");
        device.StartPolling();
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
    }
    return 0;
}

static int Touch(string[] args)
{
    var raw = args.Contains("--raw");
    var options = int.TryParse(Option(args, "--long"), out var ms)
        ? new GestureOptions { LongPressDelay = TimeSpan.FromMilliseconds(ms), ReportProgress = false }
        : new GestureOptions { ReportProgress = false };

    using var device = WigiDashDevice.Open(gestures: options);
    var done = new ManualResetEventSlim();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };

    if (raw)
        device.RawTouch += s => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} raw {s.Contact,-4} x={s.X,4} y={s.Y,4} tela={s.Screen} dormindo={s.Sleeping}");
    device.Gesture += g => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {g.Kind,-12} x={g.X,4} y={g.Y,4} {g.Duration.TotalMilliseconds,5:F0} ms");
    device.ScreenChanged += s => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} tela -> {s}");
    device.SleepChanged += s => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} dormindo -> {s}");
    device.Disconnected += ex => { Console.WriteLine($"desconectado: {ex.Message}"); done.Set(); };

    device.StartPolling();
    Console.WriteLine($"Lendo toques (long-press = {options.LongPressDelay.TotalMilliseconds} ms). Ctrl+C para sair.");
    done.Wait();
    return 0;
}

static byte[] TestPattern()
{
    const int w = WigiDashProtocol.Width, h = WigiDashProtocol.Height;
    using var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bitmap))
    {
        Color[] bars = [Color.White, Color.Yellow, Color.Cyan, Color.Lime, Color.Magenta, Color.Red, Color.Blue, Color.Black];
        for (var i = 0; i < bars.Length; i++)
        {
            using var brush = new SolidBrush(bars[i]);
            g.FillRectangle(brush, i * w / bars.Length, 0, w / bars.Length + 1, h);
        }
        using var font = new Font("Segoe UI", 40, FontStyle.Bold);
        using var back = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        g.FillRectangle(back, 0, h / 2 - 50, w, 100);
        g.DrawString("OpenWigiDashManager", font, Brushes.White, 40, h / 2 - 38);
    }
    return ToRgb565(bitmap);
}

static byte[] LoadImage(string path)
{
    using var original = Image.FromFile(path);
    using var bitmap = new Bitmap(WigiDashProtocol.Width, WigiDashProtocol.Height, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bitmap))
    {
        g.Clear(Color.Black);
        g.DrawImage(original, 0, 0, WigiDashProtocol.Width, WigiDashProtocol.Height);
    }
    return ToRgb565(bitmap);
}

static byte[] ToRgb565(Bitmap bitmap)
{
    var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
    var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    try
    {
        var source = new byte[data.Stride * data.Height];
        Marshal.Copy(data.Scan0, source, 0, source.Length);
        var frame = new byte[WigiDashProtocol.FrameBytes];
        Rgb565.FromBgra32(source, bitmap.Width, bitmap.Height, data.Stride, frame);
        return frame;
    }
    finally
    {
        bitmap.UnlockBits(data);
    }
}

static string? Option(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static Transition ParseTransition(string? value) => value?.ToLowerInvariant() switch
{
    "east" => Transition.WipeEast,
    "west" => Transition.WipeWest,
    "north" => Transition.WipeNorth,
    "south" => Transition.WipeSouth,
    _ => Transition.None,
};
