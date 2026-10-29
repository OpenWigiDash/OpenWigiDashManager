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
    bool Wake();
    bool Sleep();
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
    public bool Wake() => host.TryUse(d => d.Wake());
    public bool Sleep() => host.TryUse(d => d.Sleep());
}
