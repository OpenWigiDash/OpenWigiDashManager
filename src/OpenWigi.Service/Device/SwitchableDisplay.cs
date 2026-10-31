using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;
using OpenWigi.Ipc;
using OpenWigi.Service.Agents;

namespace OpenWigi.Service.Device;

/// <summary>
/// O aparelho que o broker usa: o WigiDash de verdade ou o virtual, conforme o "Aparelho ativo" do manager.
/// A troca vale na hora: avisa um <see cref="StatusChanged"/>, e o broker pede ao dono que redesenhe tudo.
/// O WigiDash de verdade continua sendo procurado, para a UI saber se ele está ligado.
/// </summary>
public sealed class SwitchableDisplay : IDisplay
{
    private readonly IDisplay _usb;
    private readonly IDisplay _virtual;
    private readonly DeviceChoiceStore _store;
    private readonly ILogger<SwitchableDisplay> _logger;
    private readonly Lock _gate = new();
    private DeviceKind _active;

    public SwitchableDisplay(IDisplay usb, IDisplay @virtual, DeviceChoiceStore store, ILogger<SwitchableDisplay> logger)
    {
        _usb = usb;
        _virtual = @virtual;
        _store = store;
        _logger = logger;
        _active = store.Load();
        _logger.LogInformation("Aparelho ativo: {Device}", _active);

        // Mesmo com o virtual ativo, ligar ou desligar o cabo muda o que a UI oferece.
        _usb.StatusChanged += _ => StatusChanged?.Invoke(Status);
        _usb.Gesture += g => { if (Active == DeviceKind.Usb) Gesture?.Invoke(g); };
        _virtual.Gesture += g => { if (Active == DeviceKind.Virtual) Gesture?.Invoke(g); };
    }

    public DeviceKind Active
    {
        get
        {
            lock (_gate)
                return _active;
        }
    }

    public DeviceStatus Status => Current.Status with { Kind = Active, UsbPresent = _usb.Status.Connected };

    public event Action<DeviceStatus>? StatusChanged;
    public event Action<GestureEvent>? Gesture;

    private IDisplay Current => Active == DeviceKind.Virtual ? _virtual : _usb;

    public bool WriteFrame(int page, byte[] pixels, int byteOffset) => Current.WriteFrame(page, pixels, byteOffset);
    public bool ShowPage(int page, Transition transition) => Current.ShowPage(page, transition);
    public bool SetBrightness(int percent) => Current.SetBrightness(percent);
    public int? GetBrightness() => Current.GetBrightness();
    public bool Wake() => Current.Wake();
    public bool Sleep() => Current.Sleep();
    public bool InjectTouch(TouchContact contact, int x, int y) => Current.InjectTouch(contact, x, y);

    public bool SelectDevice(DeviceKind kind)
    {
        if (!Enum.IsDefined(kind))
            return false;
        lock (_gate)
        {
            if (_active == kind)
                return true;
            _active = kind;
        }

        _store.Save(kind);
        _logger.LogInformation("Aparelho ativo trocado para {Device}", kind);
        StatusChanged?.Invoke(Status);
        return true;
    }
}
