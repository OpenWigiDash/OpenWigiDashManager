using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;
using OpenWigi.Device.Usb;

namespace OpenWigi.Device;

/// <summary>
/// Um WigiDash aberto. Serializa o acesso ao USB, prepara as páginas do hardware e,
/// com <see cref="StartPolling"/>, lê o toque em segundo plano e publica gestos.
/// </summary>
public sealed class WigiDashDevice : IDisposable
{
    private const int ErrorLimit = 5;

    private readonly IUsbTransport _transport;
    private readonly WigiDashProtocol _protocol;
    private readonly Lock _usb = new();
    private readonly bool[] _pagePrepared = new bool[WigiDashProtocol.PageCount];
    private readonly TimeProvider _time;
    private CancellationTokenSource? _pollCts;
    private Thread? _pollThread;
    private readonly Lock _injectLock = new();
    private (TouchContact Contact, int X, int Y)? _injected;
    private int _lastScreen = -1;
    private bool? _lastSleeping;

    public WigiDashDevice(IUsbTransport transport, GestureOptions? gestures = null, TimeProvider? time = null)
    {
        _transport = transport;
        _protocol = new WigiDashProtocol(transport);
        _time = time ?? TimeProvider.System;
        Gestures = new GestureRecognizer(gestures, _time);
        Info = Run(p => p.GetInfo());
    }

    /// <summary>Abre o primeiro WigiDash encontrado, ou o do caminho informado.</summary>
    public static WigiDashDevice Open(string? devicePath = null, GestureOptions? gestures = null)
    {
        devicePath ??= WinUsbDeviceFinder.FindDevicePaths().FirstOrDefault()
            ?? throw new IOException("Nenhum WigiDash conectado.");
        return new WigiDashDevice(WinUsbTransport.Open(devicePath), gestures);
    }

    public DeviceInfo Info { get; }

    public GestureRecognizer Gestures { get; }

    /// <summary>Página do hardware que está na tela (0 a 2), ou -1 se for outra tela do firmware.</summary>
    public int CurrentPage { get; private set; } = -1;

    public event Action<TouchSample>? RawTouch;
    public event Action<GestureEvent>? Gesture;
    public event Action<int>? ScreenChanged;
    public event Action<bool>? SleepChanged;
    public event Action<Exception>? Disconnected;

    public DeviceConfig GetConfig() => Run(p => p.GetConfig());

    /// <summary>Aplica a config; com <paramref name="persist"/> grava na flash do aparelho.</summary>
    public void SetConfig(DeviceConfig config, bool persist = false) => Run(p =>
    {
        p.SetConfig(config);
        if (persist)
            p.StoreConfig();
    });

    public int GetBrightness() => Run(p => p.GetBrightness());
    public void SetBrightness(int percent) => Run(p => p.SetBrightness(percent));
    public void Wake() => Run(p => p.Wake());
    public void Sleep() => Run(p => p.Sleep());

    public void ShowWelcome() => Run(p =>
    {
        p.ShowWelcome();
        CurrentPage = -1;
    });

    /// <summary>
    /// Escreve um quadro RGB565 numa página do hardware. Com <paramref name="byteOffset"/> e um
    /// trecho menor, atualiza só uma faixa de linhas (offset = linha × 1016 × 2).
    /// </summary>
    public void WriteFrame(int page, ReadOnlySpan<byte> rgb565, int byteOffset = 0)
    {
        if (byteOffset < 0 || byteOffset + rgb565.Length > WigiDashProtocol.FrameBytes)
            throw new ArgumentOutOfRangeException(nameof(byteOffset), "O trecho passa do fim do quadro.");

        lock (_usb)
        {
            EnsurePagePrepared(page);
            _protocol.WriteRegion(page, 0, byteOffset, rgb565);
        }
    }

    public void ShowPage(int page, Transition transition = Transition.None)
    {
        lock (_usb)
        {
            EnsurePagePrepared(page);
            _protocol.ShowPage(page, transition);
            CurrentPage = page;
        }
    }

    /// <summary>
    /// Toque emulado (mouse numa prévia, testes). Vale nas próximas leituras como se viesse do
    /// aparelho: um Down fica valendo até chegar um Up. Toque real no vidro tem prioridade.
    /// </summary>
    public void InjectTouch(TouchContact contact, int x, int y)
    {
        lock (_injectLock)
            _injected = contact == TouchContact.None ? null : (contact, x, y);
    }

    /// <summary>Começa a ler o toque a cada <paramref name="interval"/> (padrão 33 ms) numa thread própria.</summary>
    public void StartPolling(TimeSpan? interval = null)
    {
        if (_pollThread is not null)
            return;

        var period = interval ?? TimeSpan.FromMilliseconds(33);
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;
        _pollThread = new Thread(() => PollLoop(period, token)) { IsBackground = true, Name = "WigiDash touch" };
        _pollThread.Start();
    }

    public void StopPolling()
    {
        _pollCts?.Cancel();
        if (_pollThread is { } thread && thread != Thread.CurrentThread)
            thread.Join(TimeSpan.FromSeconds(2));
        _pollThread = null;
        _pollCts?.Dispose();
        _pollCts = null;
    }

    public void Dispose()
    {
        StopPolling();
        lock (_usb)
            _transport.Dispose();
    }

    private void PollLoop(TimeSpan period, CancellationToken token)
    {
        var errors = 0;
        using var timer = new PeriodicTimer(period, _time);
        while (!token.IsCancellationRequested)
        {
            try
            {
                var sample = Run(p => p.ReadTouch());
                errors = 0;
                Publish(sample);
            }
            catch (IOException) when (++errors < ErrorLimit)
            {
                // Falha isolada de USB acontece; só desistimos depois de várias seguidas.
            }
            catch (IOException ex)
            {
                Disconnected?.Invoke(ex);
                return;
            }

            try
            {
                if (!timer.WaitForNextTickAsync(token).AsTask().GetAwaiter().GetResult())
                    return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Publish(TouchSample sample)
    {
        if (sample.Contact == TouchContact.None)
        {
            lock (_injectLock)
            {
                if (_injected is { } injected)
                {
                    sample = sample with { Contact = injected.Contact, X = injected.X, Y = injected.Y };
                    if (injected.Contact == TouchContact.Up)
                        _injected = null; // o Up vale uma leitura só
                }
            }
        }

        if (sample.Contact != TouchContact.None)
            RawTouch?.Invoke(sample);

        foreach (var gesture in Gestures.Process(sample))
            Gesture?.Invoke(gesture);

        if (sample.Screen != _lastScreen)
        {
            _lastScreen = sample.Screen;
            ScreenChanged?.Invoke(sample.Screen);
        }

        if (sample.Sleeping != _lastSleeping)
        {
            _lastSleeping = sample.Sleeping;
            SleepChanged?.Invoke(sample.Sleeping);
        }
    }

    // A página precisa de uma região cobrindo a tela toda antes do primeiro quadro.
    private void EnsurePagePrepared(int page)
    {
        if (_pagePrepared[page])
            return;
        _protocol.AddRegion(page, 0, 0, 0, WigiDashProtocol.Width, WigiDashProtocol.Height);
        _pagePrepared[page] = true;
    }

    private T Run<T>(Func<WigiDashProtocol, T> action)
    {
        lock (_usb)
            return action(_protocol);
    }

    private void Run(Action<WigiDashProtocol> action)
    {
        lock (_usb)
            action(_protocol);
    }
}
