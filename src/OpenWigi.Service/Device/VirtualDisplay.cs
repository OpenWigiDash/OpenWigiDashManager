using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;
using OpenWigi.Ipc;
using OpenWigi.Service.Agents;

namespace OpenWigi.Service.Device;

/// <summary>
/// WigiDash de mentira, para quem não tem o aparelho ("Aparelho ativo" no manager). Aceita
/// quadros e páginas, que o broker guarda para a prévia, e transforma o toque emulado em gestos com o
/// mesmo reconhecedor e a mesma leitura a cada ~33 ms do aparelho real.
/// </summary>
public sealed class VirtualDisplay : IDisplay, IDisposable
{
    public static readonly Guid DeviceId = new("0f9d3c1e-6a52-4b7e-9c1d-5e2a7b8f4d60");
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(33);

    private readonly Lock _gate = new();
    private readonly Lock _pollGate = new();
    private readonly GestureRecognizer _gestures;
    private readonly ITimer _poll;
    private (TouchContact Contact, int X, int Y)? _injected;
    private int _brightness = 50; // o aparelho real liga em 50%

    public VirtualDisplay(TimeProvider time, GestureOptions? gestures = null)
    {
        _gestures = new GestureRecognizer(gestures, time);
        _poll = time.CreateTimer(_ => Poll(), null, PollInterval, PollInterval);
    }

    /// <summary>Está sempre ligado: não há cabo para cair.</summary>
    public DeviceStatus Status { get; } = new(true, DeviceId, "virtual", DeviceKind.Virtual);

    public event Action<DeviceStatus>? StatusChanged
    {
        add { }
        remove { }
    }

    public event Action<GestureEvent>? Gesture;

    public bool WriteFrame(int page, byte[] pixels, int byteOffset) =>
        page is >= 0 and < WigiDashProtocol.PageCount && byteOffset >= 0 && byteOffset + pixels.Length <= WigiDashProtocol.FrameBytes;

    public bool ShowPage(int page, Transition transition) => page is >= 0 and < WigiDashProtocol.PageCount;

    public bool SetBrightness(int percent)
    {
        lock (_gate)
            _brightness = Math.Clamp(percent, 0, 100);
        return true;
    }

    public int? GetBrightness()
    {
        lock (_gate)
            return _brightness;
    }

    public bool Wake() => true;
    public bool Sleep() => true;

    public bool InjectTouch(TouchContact contact, int x, int y)
    {
        lock (_gate)
            _injected = contact == TouchContact.None ? null : (contact, x, y);
        return true;
    }

    public bool SelectDevice(DeviceKind kind) => kind == DeviceKind.Virtual;

    public void Dispose() => _poll.Dispose();

    // Igual ao aparelho: o Down vale até chegar o Up, e o Up vale uma leitura só.
    private void Poll()
    {
        TouchSample sample;
        lock (_gate)
        {
            sample = _injected is { } injected
                ? new TouchSample(injected.Contact, injected.X, injected.Y, 0, false)
                : new TouchSample(TouchContact.None, 0, 0, 0, false);
            if (_injected is { Contact: TouchContact.Up })
                _injected = null;
        }

        lock (_pollGate)
        {
            foreach (var gesture in _gestures.Process(sample))
                Gesture?.Invoke(gesture);
        }
    }
}
