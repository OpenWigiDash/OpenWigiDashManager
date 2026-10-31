using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;
using OpenWigi.Ipc;
using OpenWigi.Service.Device;

namespace OpenWigi.Service.Agents;

/// <summary>O que o broker precisa do aparelho. <see cref="DeviceHost"/> em produção, falso nos testes.</summary>
public interface IDisplay
{
    DeviceStatus Status { get; }
    event Action<DeviceStatus>? StatusChanged;
    event Action<GestureEvent>? Gesture;

    bool WriteFrame(int page, byte[] pixels, int byteOffset);
    bool ShowPage(int page, Transition transition);
    bool SetBrightness(int percent);

    /// <summary>Brilho atual lido do aparelho; null se ele não está ligado.</summary>
    int? GetBrightness();
    bool Wake();
    bool Sleep();
    bool InjectTouch(TouchContact contact, int x, int y);

    /// <summary>Troca o aparelho ativo; false se este display não tem esse aparelho.</summary>
    bool SelectDevice(DeviceKind kind);
}

internal sealed class DeviceHostDisplay(DeviceHost host) : IDisplay
{
    public DeviceStatus Status => host.Status;

    public event Action<DeviceStatus>? StatusChanged
    {
        add => host.StatusChanged += value;
        remove => host.StatusChanged -= value;
    }

    public event Action<GestureEvent>? Gesture
    {
        add => host.Gesture += value;
        remove => host.Gesture -= value;
    }

    public bool WriteFrame(int page, byte[] pixels, int byteOffset) =>
        host.TryUse(d => d.WriteFrame(page, pixels, byteOffset));

    public bool ShowPage(int page, Transition transition) => host.TryUse(d => d.ShowPage(page, transition));
    public bool SetBrightness(int percent) => host.TryUse(d => d.SetBrightness(percent));

    public int? GetBrightness()
    {
        int? percent = null;
        return host.TryUse(d => percent = d.GetBrightness()) ? percent : null;
    }

    public bool Wake() => host.TryUse(d => d.Wake());
    public bool Sleep() => host.TryUse(d => d.Sleep());
    public bool InjectTouch(TouchContact contact, int x, int y) => host.TryUse(d => d.InjectTouch(contact, x, y));
    public bool SelectDevice(DeviceKind kind) => kind == DeviceKind.Usb;
}
