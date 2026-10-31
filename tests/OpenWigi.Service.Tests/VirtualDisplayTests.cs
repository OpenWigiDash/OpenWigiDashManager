using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;
using OpenWigi.Ipc;
using OpenWigi.Service.Agents;
using OpenWigi.Service.Device;

namespace OpenWigi.Service.Tests;

public class VirtualDisplayTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
    private readonly VirtualDisplay _display;
    private readonly List<GestureEvent> _gestures = [];

    public VirtualDisplayTests()
    {
        _display = new VirtualDisplay(_time);
        _display.Gesture += g => _gestures.Add(g);
    }

    // Avança o relógio uma leitura de cada vez, como o aparelho lendo o toque.
    private void Poll(int times = 1)
    {
        for (var i = 0; i < times; i++)
            _time.Advance(VirtualDisplay.PollInterval);
    }

    [Fact]
    public void Esta_sempre_conectado()
    {
        Assert.True(_display.Status.Connected);
        Assert.Equal(VirtualDisplay.DeviceId, _display.Status.DeviceId);
    }

    [Fact]
    public void Clique_vira_tap_no_ponto_do_clique()
    {
        Poll(); // o reconhecedor espera uma leitura sem toque antes de começar
        _display.InjectTouch(TouchContact.Down, 120, 340);
        Poll(2);
        _display.InjectTouch(TouchContact.Up, 120, 340);
        Poll(2);

        Assert.Equal([GestureKind.PressStarted, GestureKind.PressEnded, GestureKind.Tap],
            _gestures.Where(g => g.Kind != GestureKind.PressProgress).Select(g => g.Kind));
        var tap = _gestures.Single(g => g.Kind == GestureKind.Tap);
        Assert.Equal((120, 340), (tap.X, tap.Y));
    }

    [Fact]
    public void Segurar_vira_long_press()
    {
        Poll();
        _display.InjectTouch(TouchContact.Down, 500, 300);
        Poll(25); // ~825 ms, além dos 600 ms do long-press

        Assert.Contains(_gestures, g => g.Kind == GestureKind.LongPress);
    }

    [Fact]
    public void Arrastar_para_a_esquerda_vira_swipe()
    {
        Poll();
        _display.InjectTouch(TouchContact.Down, 800, 300);
        Poll();
        _display.InjectTouch(TouchContact.Down, 500, 300);
        Poll();

        Assert.Contains(_gestures, g => g.Kind == GestureKind.SwipeLeft);
    }

    [Fact]
    public void Guarda_o_brilho_pedido()
    {
        Assert.Equal(50, _display.GetBrightness());
        Assert.True(_display.SetBrightness(80));
        Assert.Equal(80, _display.GetBrightness());
    }

    [Fact]
    public void Recusa_quadro_fora_da_tela()
    {
        Assert.False(_display.WriteFrame(WigiDashProtocol.PageCount, new byte[10], 0));
        Assert.False(_display.WriteFrame(0, new byte[10], WigiDashProtocol.FrameBytes - 5));
        Assert.True(_display.WriteFrame(0, new byte[10], 0));
    }

    [Fact]
    public async Task Previa_mostra_o_que_o_dono_desenhou()
    {
        var sessions = new SessionBrokerTests.FakeSessions { Console = 1 };
        var broker = new SessionBroker(_display, sessions, Options.Create(new ServiceOptions()), _time, NullLogger<SessionBroker>.Instance);
        var channel = new SessionBrokerTests.FakeChannel();
        var agent = new AgentSession(1, channel, _time.GetUtcNow()) { UserName = "victor" };
        await broker.RegisterAsync(agent);
        broker.Handle(agent, new SubscribePreview(true));

        var pixels = new byte[WigiDashProtocol.FrameBytes];
        pixels[0] = 0xAB;
        broker.Handle(agent, new Frame(0, 0, pixels));
        broker.Handle(agent, new ShowPage(0, Transition.None));
        await broker.PublishPreviewAsync();

        Assert.Contains(channel.Sent, m => m is Welcome { Device.Connected: true });
        Assert.Contains(channel.Sent, m => m is Ownership { IsOwner: true });
        Assert.Equal(0xAB, Assert.Single(channel.Sent.OfType<PreviewFrame>()).Pixels[0]);
    }

    [Fact]
    public async Task Clique_na_previa_chega_ao_dono_como_gesto()
    {
        var sessions = new SessionBrokerTests.FakeSessions { Console = 1 };
        var broker = new SessionBroker(_display, sessions, Options.Create(new ServiceOptions()), _time, NullLogger<SessionBroker>.Instance);
        var channel = new SessionBrokerTests.FakeChannel();
        var agent = new AgentSession(1, channel, _time.GetUtcNow()) { UserName = "victor" };
        await broker.RegisterAsync(agent);

        Poll();
        broker.Handle(agent, new EmulatedTouch(TouchContact.Down, 10, 20));
        Poll();
        broker.Handle(agent, new EmulatedTouch(TouchContact.Up, 10, 20));
        Poll();

        Assert.Contains(channel.Sent, m => m is Gesture { Event.Kind: GestureKind.Tap });
    }
}
