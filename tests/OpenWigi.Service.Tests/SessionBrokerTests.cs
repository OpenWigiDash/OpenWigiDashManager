using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;
using OpenWigi.Ipc;
using OpenWigi.Service.Agents;
using OpenWigi.Service.Device;
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
        Assert.Equal(ServiceScreenSettingsStore.Default, channel.Sent[1]);
        Assert.Equal(new Ownership(true, "Sessão ativa no console"), channel.Sent[2]);
    }

    [Fact]
    public async Task Lista_da_tela_do_servico_usa_nome_e_foto_do_agente()
    {
        _sessions.Console = 1;
        _sessions.Locked = true;
        _sessions.UserSessions.Add(new WindowsSession(3, "ana", WindowsSessionState.Disconnected, false));
        _sessions.UserSessions.Add(new WindowsSession(1, "victo", WindowsSessionState.Locked, false));
        var (victor, _) = await ConnectAsync(1, "victo");
        var picture = new byte[UserProfile.PictureBytes];

        _broker.Handle(victor, new UserProfile("Victor Perez", picture));

        var sessions = _broker.ListSessions();
        Assert.Equal(["Victor Perez", "ana"], sessions.Select(s => s.Name));
        Assert.Same(picture, sessions[0].Picture);
        Assert.Null(sessions[1].Picture);
    }

    [Fact]
    public async Task Foto_com_tamanho_errado_e_descartada()
    {
        _sessions.UserSessions.Add(new WindowsSession(1, "victo", WindowsSessionState.Locked, false));
        var (victor, _) = await ConnectAsync(1, "victo");

        _broker.Handle(victor, new UserProfile("  ", new byte[10]));

        var session = Assert.Single(_broker.ListSessions());
        Assert.Equal("victo", session.Name);
        Assert.Null(session.Picture);
    }

    [Fact]
    public async Task Sessao_remota_aparece_na_lista_da_tela_bloqueada()
    {
        // Victor no RDP: a sessão dele saiu do console, que ficou sem usuário.
        _sessions.Console = 2;
        _sessions.ConsoleHasUser = false;
        _sessions.Remote.Add(1);
        _sessions.UserSessions.Add(new WindowsSession(1, "victo", WindowsSessionState.Active, true));
        await ConnectAsync(1, "victo");

        Assert.Null(_broker.Owner);
        Assert.Equal(OwnershipPolicy.NoConsoleUserReason, _broker.Reason);
        var session = Assert.Single(_broker.ListSessions());
        Assert.True(session.Remote);
        Assert.Equal(WindowsSessionState.Active, session.State);
    }

    [Fact]
    public async Task Dono_esconde_a_lista_e_todos_os_agentes_ficam_sabendo()
    {
        _sessions.Console = 1;
        _sessions.UserSessions.Add(new WindowsSession(1, "victo", WindowsSessionState.Active, false));
        var (victor, victorChannel) = await ConnectAsync(1, "victo");
        var (_, filha) = await ConnectAsync(4, "filha");

        _broker.Handle(victor, new ServiceScreenSettings(false));

        Assert.False(_broker.ScreenSettings.ShowSessions);
        Assert.Empty(_broker.ListSessions());
        Assert.Equal(new ServiceScreenSettings(false), victorChannel.Sent.OfType<ServiceScreenSettings>().Last());
        Assert.Equal(new ServiceScreenSettings(false), filha.Sent.OfType<ServiceScreenSettings>().Last());
    }

    [Fact]
    public async Task Quem_nao_e_dono_nao_muda_a_tela_do_servico()
    {
        _sessions.Console = 1;
        await ConnectAsync(1, "victo");
        var (filha, _) = await ConnectAsync(4, "filha");

        _broker.Handle(filha, new ServiceScreenSettings(false));

        Assert.True(_broker.ScreenSettings.ShowSessions);
    }

    [Fact]
    public async Task Quem_ganha_a_posse_recebe_o_brilho_real_do_aparelho()
    {
        _sessions.Console = 1;
        _display.Brightness = 50; // padrão de fábrica ao ligar
        var (_, channel) = await ConnectAsync(1, "victor");

        Assert.Equal(new BrightnessReport(50), Assert.Single(channel.Sent.OfType<BrightnessReport>()));
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

    private static GestureEvent Tap(int x, int y) => new(GestureKind.Tap, x, y, TimeSpan.FromMilliseconds(80), 1);

    [Fact]
    public async Task Botao_desligar_monitor_pede_ao_agente_do_console()
    {
        _sessions.Console = 1;
        _sessions.Locked = true;
        var (_, victor) = await ConnectAsync(1, "victor");
        var (_, filha) = await ConnectAsync(4, "filha");

        _display.RaiseGesture(Tap(100, 550));
        await Task.Delay(50);

        Assert.Single(victor.Sent.OfType<TurnOffMonitors>());
        Assert.DoesNotContain(filha.Sent, m => m is TurnOffMonitors);
    }

    [Fact]
    public async Task Toque_fora_do_botao_nao_desliga_monitor()
    {
        _sessions.Console = 1;
        _sessions.Locked = true;
        var (_, victor) = await ConnectAsync(1, "victor");

        _display.RaiseGesture(Tap(500, 300));
        _display.RaiseGesture(new GestureEvent(GestureKind.LongPress, 100, 550, TimeSpan.FromMilliseconds(600), 1));
        await Task.Delay(50);

        Assert.DoesNotContain(victor.Sent, m => m is TurnOffMonitors);
    }

    [Fact]
    public async Task Sem_agente_no_console_nao_tem_botao_desligar_monitor()
    {
        // Victor no RDP: o console está na tela de logon, não há quem desligue os monitores dele.
        _sessions.Console = 2;
        _sessions.ConsoleHasUser = false;
        _sessions.Remote.Add(1);
        var (_, victor) = await ConnectAsync(1, "victo");

        _display.RaiseGesture(Tap(100, 550));
        await Task.Delay(50);

        Assert.DoesNotContain(victor.Sent, m => m is TurnOffMonitors);
    }

    [Fact]
    public async Task Dono_da_tela_recebe_o_toque_e_nao_desliga_monitor()
    {
        _sessions.Console = 1;
        var (_, victor) = await ConnectAsync(1, "victor");

        _display.RaiseGesture(Tap(100, 550));
        await Task.Delay(50);

        Assert.Contains(victor.Sent, m => m is Gesture);
        Assert.DoesNotContain(victor.Sent, m => m is TurnOffMonitors);
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
    public async Task Previa_pedida_antes_da_posse_chega_quando_vira_dono()
    {
        // Manager aberto numa sessão que ainda não é a do console (troca rápida de usuário): pede a prévia
        // ao conectar e só ganha a posse depois, quando a sessão vira a do console.
        _sessions.Console = 4;
        var (victor, channel) = await ConnectAsync(3, "victor");
        _broker.Handle(victor, new SubscribePreview(true));
        Assert.Null(_broker.Owner);

        _sessions.Console = 3;
        await _broker.EvaluateAsync();
        Assert.Equal(victor.Id, _broker.Owner);

        var pixels = new byte[WigiDashProtocol.FrameBytes];
        pixels[0] = 0xCD;
        _broker.Handle(victor, new Frame(1, 0, pixels));
        _broker.Handle(victor, new ShowPage(1, Transition.None));
        await _broker.PublishPreviewAsync();

        var preview = Assert.Single(channel.Sent.OfType<PreviewFrame>());
        Assert.Equal(1, preview.Page);
        Assert.Equal(0xCD, preview.Pixels[0]);
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

    [Fact]
    public async Task Sem_dono_qualquer_agente_troca_o_aparelho()
    {
        _sessions.Remote.Add(4);
        var (sandbox, _) = await ConnectAsync(4, "WDAGUtilityAccount");
        Assert.Null(_broker.Owner); // remota, sem AllowRemoteSessions

        _broker.Handle(sandbox, new SelectDevice(DeviceKind.Virtual));

        Assert.Equal([DeviceKind.Virtual], _display.Selected);
    }

    [Fact]
    public async Task Com_o_virtual_ativo_sessao_remota_fica_com_a_tela()
    {
        _sessions.Remote.Add(4);
        var (sandbox, channel) = await ConnectAsync(4, "WDAGUtilityAccount");
        Assert.Null(_broker.Owner);

        _display.Status = new DeviceStatus(true, Guid.NewGuid(), "virtual", DeviceKind.Virtual);
        await _broker.EvaluateAsync();

        Assert.Equal(sandbox.Id, _broker.Owner);
        Assert.Contains(channel.Sent, m => m is Ownership { IsOwner: true });
    }

    [Fact]
    public async Task Troca_de_aparelho_so_vale_do_dono()
    {
        _sessions.Console = 1;
        var (victor, _) = await ConnectAsync(1, "victor");
        var (filha, _) = await ConnectAsync(4, "filha");

        _broker.Handle(filha, new SelectDevice(DeviceKind.Virtual));
        _broker.Handle(victor, new SelectDevice(DeviceKind.Virtual));

        Assert.Equal([DeviceKind.Virtual], _display.Selected);
    }

    internal sealed class FakeChannel : IAgentChannel
    {
        public List<IpcMessage> Sent { get; } = [];

        public Task SendAsync(IpcMessage message, CancellationToken cancellationToken = default)
        {
            lock (Sent)
                Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeSessions : ISessionMonitor
    {
        public int? Console { get; set; }
        public bool Locked { get; set; }
        public bool ConsoleHasUser { get; set; } = true;
        public event Action? Changed;

        /// <summary>Sessões remotas ativas (RDP, Windows Sandbox).</summary>
        public HashSet<int> Remote { get; } = [];

        public SessionSnapshot Capture(IEnumerable<int> knownSessions)
        {
            var active = new HashSet<int>(Remote);
            if (Console is { } c)
                active.Add(c);
            return new SessionSnapshot(Console, Locked, active, ConsoleHasUser);
        }

        public List<WindowsSession> UserSessions { get; } = [];

        public IReadOnlyList<WindowsSession> ListUserSessions() => UserSessions;

        public void Raise() => Changed?.Invoke();
    }

    internal sealed class FakeDisplay : IDisplay
    {
        public List<int> Frames { get; } = [];
        public List<int> Shown { get; } = [];
        public DeviceStatus Status { get; set; } = new(true, Guid.NewGuid(), "teste");
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

        public int Brightness { get; set; } = 50;

        public bool SetBrightness(int percent)
        {
            Brightness = percent;
            return true;
        }

        public int? GetBrightness() => Brightness;
        public bool Wake() => true;
        public bool Sleep() => true;

        public List<(TouchContact Contact, int X, int Y)> Injected { get; } = [];

        public bool InjectTouch(TouchContact contact, int x, int y)
        {
            Injected.Add((contact, x, y));
            return true;
        }

        public List<DeviceKind> Selected { get; } = [];

        public bool SelectDevice(DeviceKind kind)
        {
            Selected.Add(kind);
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
