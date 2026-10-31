using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;
using OpenWigi.Ipc;
using OpenWigi.Service.Agents;
using OpenWigi.Service.Device;

namespace OpenWigi.Service.Tests;

public sealed class SwitchableDisplayTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "owd-tests", Guid.NewGuid().ToString("N"));
    private readonly SessionBrokerTests.FakeDisplay _usb = new() { Status = DeviceStatus.Disconnected };
    private readonly SessionBrokerTests.FakeDisplay _virtual = new() { Status = new(true, Guid.NewGuid(), "virtual", DeviceKind.Virtual) };

    private string ChoicePath => Path.Combine(_folder, "Service", "device.txt");

    private SwitchableDisplay Create(DeviceKind fallback = DeviceKind.Usb) =>
        new(_usb, _virtual, new DeviceChoiceStore(ChoicePath, fallback, NullLogger<DeviceChoiceStore>.Instance),
            NullLogger<SwitchableDisplay>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Sem_escolha_gravada_vale_a_configuracao()
    {
        Assert.Equal(DeviceKind.Usb, Create().Active);
        Assert.Equal(DeviceKind.Virtual, Create(DeviceKind.Virtual).Active);
    }

    [Fact]
    public void Status_diz_o_aparelho_ativo_e_se_o_usb_esta_ligado()
    {
        var display = Create();
        Assert.Equal(new DeviceStatus(false, Guid.Empty, string.Empty, DeviceKind.Usb, UsbPresent: false), display.Status);

        display.SelectDevice(DeviceKind.Virtual);
        Assert.True(display.Status.Connected);
        Assert.Equal(DeviceKind.Virtual, display.Status.Kind);
        Assert.False(display.Status.UsbPresent);
    }

    [Fact]
    public void Troca_avisa_e_fica_gravada()
    {
        var display = Create();
        var changes = new List<DeviceStatus>();
        display.StatusChanged += changes.Add;

        Assert.True(display.SelectDevice(DeviceKind.Virtual));
        Assert.True(display.SelectDevice(DeviceKind.Virtual)); // já está: não avisa de novo

        Assert.Equal(DeviceKind.Virtual, Assert.Single(changes).Kind);
        Assert.Equal("Virtual", File.ReadAllText(ChoicePath));
        Assert.Equal(DeviceKind.Virtual, Create().Active); // a escolha gravada vale mais que a configuração
    }

    [Fact]
    public void Comandos_vao_para_o_aparelho_ativo()
    {
        var display = Create();
        display.SelectDevice(DeviceKind.Virtual);

        display.WriteFrame(0, new byte[10], 0);
        display.InjectTouch(TouchContact.Down, 5, 6);

        Assert.Equal([0], _virtual.Frames);
        Assert.Empty(_usb.Frames);
        Assert.Equal([(TouchContact.Down, 5, 6)], _virtual.Injected);
    }

    [Fact]
    public void So_passa_gesto_do_aparelho_ativo()
    {
        var display = Create();
        var gestures = new List<GestureEvent>();
        display.Gesture += gestures.Add;

        _virtual.RaiseGesture(new GestureEvent(GestureKind.Tap, 1, 1, TimeSpan.Zero));
        _usb.RaiseGesture(new GestureEvent(GestureKind.Tap, 2, 2, TimeSpan.Zero));

        Assert.Equal(2, Assert.Single(gestures).X);
    }

    [Fact]
    public void Cabo_ligado_com_o_virtual_ativo_avisa_a_ui()
    {
        var display = Create(DeviceKind.Virtual);
        var changes = new List<DeviceStatus>();
        display.StatusChanged += changes.Add;

        _usb.Status = new DeviceStatus(true, Guid.NewGuid(), "1.0");
        _usb.Reconnect();

        Assert.All(changes, s => Assert.Equal(DeviceKind.Virtual, s.Kind));
        Assert.True(changes[^1].UsbPresent);
    }

    [Fact]
    public async Task Dono_troca_para_o_virtual_e_redesenha()
    {
        var display = Create();
        var time = new FakeTimeProvider();
        var sessions = new SessionBrokerTests.FakeSessions { Console = 1 };
        var broker = new SessionBroker(display, sessions, Options.Create(new ServiceOptions()), time, NullLogger<SessionBroker>.Instance);
        var channel = new SessionBrokerTests.FakeChannel();
        var agent = new AgentSession(1, channel, time.GetUtcNow()) { UserName = "victor" };
        await broker.RegisterAsync(agent);

        broker.Handle(agent, new SelectDevice(DeviceKind.Virtual));
        await broker.EvaluateAsync();

        Assert.Contains(channel.Sent, m => m is DeviceStatus { Kind: DeviceKind.Virtual, Connected: true });
        // Ganhou a posse de novo, para redesenhar no aparelho novo.
        Assert.Equal(2, channel.Sent.OfType<Ownership>().Count(o => o.IsOwner));
    }
}
