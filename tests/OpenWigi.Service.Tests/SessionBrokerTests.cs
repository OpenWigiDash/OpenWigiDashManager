using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;
using OpenWigi.Ipc;
using OpenWigi.Service.Agents;
using OpenWigi.Service.Sessions;

namespace OpenWigi.Service.Tests;

public class SessionBrokerTests
{
    private readonly FakeDisplay _display = new();
    private readonly FakeSessions _sessions = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
    private readonly SessionBroker _broker;

    public SessionBrokerTests()
    {
        _broker = new SessionBroker(_display, _sessions, Options.Create(new ServiceOptions()), _time, NullLogger<SessionBroker>.Instance);
    }

    private async Task<(AgentSession Agent, FakeChannel Channel)> ConnectAsync(int session, string user)
    {
        var channel = new FakeChannel();
        var agent = new AgentSession(session, channel, _time.GetUtcNow()) { UserName = user };
        _time.Advance(TimeSpan.FromSeconds(1));
        await _broker.RegisterAsync(agent);
        return (agent, channel);
    }

    [Fact]
    public async Task Agente_do_console_ganha_a_posse_ao_conectar()
    {
        _sessions.Console = 1;
        var (_, channel) = await ConnectAsync(1, "victor");

        Assert.IsType<Welcome>(channel.Sent[0]);
        Assert.Equal(new Ownership(true, "Sessão ativa no console"), channel.Sent[1]);
    }

    [Fact]
    public async Task Troca_de_usuario_avisa_os_dois_lados()
    {
        _sessions.Console = 1;
        var (_, victor) = await ConnectAsync(1, "victor");
        var (_, filha) = await ConnectAsync(4, "filha");

        _sessions.Console = 4;
        await _broker.EvaluateAsync();

        Assert.Contains(victor.Sent, m => m is Ownership { IsOwner: false });
        Assert.Contains(filha.Sent, m => m is Ownership { IsOwner: true });
    }

    [Fact]
    public async Task Quem_nao_e_dono_nao_desenha()
    {
        _sessions.Console = 1;
        await ConnectAsync(1, "victor");
        var (intruso, _) = await ConnectAsync(4, "filha");
        _display.Frames.Clear();

        _broker.Handle(intruso, new Frame(0, 0, new byte[10]));
        Assert.Empty(_display.Frames);
    }

    [Fact]
    public async Task Dono_nao_pode_escrever_na_pagina_do_servico()
    {
        _sessions.Console = 1;
        var (victor, _) = await ConnectAsync(1, "victor");
        _display.Frames.Clear();

        _broker.Handle(victor, new Frame(ServiceOptions.ServicePage, 0, new byte[10]));
        _broker.Handle(victor, new Frame(0, 0, new byte[10]));
        Assert.Equal([0], _display.Frames);
    }

    [Fact]
    public async Task Sem_dono_o_servico_mostra_a_propria_tela()
    {
        _sessions.Console = 1;
        _sessions.Locked = true;
        await ConnectAsync(1, "victor");

        Assert.Contains(ServiceOptions.ServicePage, _display.Frames);
        Assert.Equal(ServiceOptions.ServicePage, _display.Shown[^1]);
    }

    [Fact]
    public async Task Gestos_vao_so_para_o_dono()
    {
        _sessions.Console = 1;
        var (_, victor) = await ConnectAsync(1, "victor");
        var (_, filha) = await ConnectAsync(4, "filha");

        _display.RaiseGesture(new GestureEvent(GestureKind.LongPress, 10, 20, TimeSpan.FromMilliseconds(600), 1));
        await Task.Delay(50);

        Assert.Contains(victor.Sent, m => m is Gesture { Event.Kind: GestureKind.LongPress });
        Assert.DoesNotContain(filha.Sent, m => m is Gesture);
    }

    [Fact]
    public async Task Aparelho_reconectado_pede_redesenho_ao_dono()
    {
        _sessions.Console = 1;
        var (_, victor) = await ConnectAsync(1, "victor");

        _display.Reconnect();
        await _broker.EvaluateAsync();

        Assert.Equal(2, victor.Sent.Count(m => m is Ownership { IsOwner: true }));
    }

    [Fact]
    public async Task Dono_que_sai_devolve_a_tela_ao_servico()
    {
        _sessions.Console = 1;
        var (victor, _) = await ConnectAsync(1, "victor");
        _display.Shown.Clear();

        await _broker.UnregisterAsync(victor);

        Assert.Null(_broker.Owner);
        Assert.Equal([ServiceOptions.ServicePage], _display.Shown);
    }

    [Fact]
    public async Task Previa_vai_para_o_dono_que_pediu_com_a_pagina_na_tela()
    {
        _sessions.Console = 1;
        var (victor, channel) = await ConnectAsync(1, "victor");
        _broker.Handle(victor, new SubscribePreview(true));

        var pixels = new byte[WigiDashProtocol.FrameBytes];
        pixels[0] = 0xAB;
        _broker.Handle(victor, new Frame(0, 0, pixels));
        _broker.Handle(victor, new ShowPage(0, Transition.None));
        await _broker.PublishPreviewAsync();

        var preview = Assert.Single(channel.Sent.OfType<PreviewFrame>());
        Assert.Equal(0, preview.Page);
        Assert.Equal(0xAB, preview.Pixels[0]);

        await _broker.PublishPreviewAsync(); // nada mudou: não reenvia
        Assert.Single(channel.Sent.OfType<PreviewFrame>());
    }

    [Fact]
    public async Task Previa_nao_vaza_para_outra_sessao()
    {
        _sessions.Console = 1;
        await ConnectAsync(1, "victor");
        var (filha, channel) = await ConnectAsync(4, "filha");
        _broker.Handle(filha, new SubscribePreview(true));

        await _broker.PublishPreviewAsync();
        Assert.DoesNotContain(channel.Sent, m => m is PreviewFrame);
    }

    [Fact]
    public async Task Toque_emulado_so_vale_do_dono()
    {
        _sessions.Console = 1;
        var (victor, _) = await ConnectAsync(1, "victor");
        var (filha, _) = await ConnectAsync(4, "filha");

        _broker.Handle(filha, new EmulatedTouch(TouchContact.Down, 1, 1));
        _broker.Handle(victor, new EmulatedTouch(TouchContact.Down, 100, 200));

        Assert.Equal([(TouchContact.Down, 100, 200)], _display.Injected);
    }

    private sealed class FakeChannel : IAgentChannel
    {
        public List<IpcMessage> Sent { get; } = [];

        public Task SendAsync(IpcMessage message, CancellationToken cancellationToken = default)
        {
            lock (Sent)
                Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessions : ISessionMonitor
    {
        public int? Console { get; set; }
        public bool Locked { get; set; }
        public event Action? Changed;

        public SessionSnapshot Capture(IEnumerable<int> knownSessions) =>
            new(Console, Locked, Console is { } c ? new HashSet<int> { c } : []);

        public void Raise() => Changed?.Invoke();
    }

    private sealed class FakeDisplay : IDisplay
    {
        public List<int> Frames { get; } = [];
        public List<int> Shown { get; } = [];
        public DeviceStatus Status { get; private set; } = new(true, Guid.NewGuid(), "teste");
        public event Action<DeviceStatus>? StatusChanged;
        public event Action<GestureEvent>? Gesture;

        public bool WriteFrame(int page, byte[] pixels, int byteOffset)
        {
            Frames.Add(page);
            return true;
        }

        public bool ShowPage(int page, Transition transition)
        {
            Shown.Add(page);
            return true;
        }

        public bool SetBrightness(int percent) => true;
        public bool Wake() => true;
        public bool Sleep() => true;

        public List<(TouchContact Contact, int X, int Y)> Injected { get; } = [];

        public bool InjectTouch(TouchContact contact, int x, int y)
        {
            Injected.Add((contact, x, y));
            return true;
        }

        public void RaiseGesture(GestureEvent gesture) => Gesture?.Invoke(gesture);

        public void Reconnect()
        {
            StatusChanged?.Invoke(DeviceStatus.Disconnected);
            StatusChanged?.Invoke(Status);
        }
    }
}
