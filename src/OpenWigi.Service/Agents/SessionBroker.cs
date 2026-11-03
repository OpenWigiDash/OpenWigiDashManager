using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Options;
using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;
using OpenWigi.Ipc;
using OpenWigi.Service.Device;
using OpenWigi.Service.Sessions;

namespace OpenWigi.Service.Agents;

/// <summary>
/// Decide qual agente desenha no WigiDash e encaminha quadros e toques. Reavalia a posse quando
/// um agente entra ou sai, quando o Windows avisa de troca de sessão, quando o aparelho reconecta
/// e, por garantia, a cada <see cref="ServiceOptions.SessionPollInterval"/>.
/// </summary>
public sealed class SessionBroker : BackgroundService
{
    private readonly IDisplay _display;
    private readonly ISessionMonitor _sessions;
    private readonly ServiceOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<SessionBroker> _logger;
    private readonly ConcurrentDictionary<Guid, AgentSession> _agents = new();
    private readonly SemaphoreSlim _evaluate = new(1, 1);
    private readonly SemaphoreSlim _wakeUp = new(0, int.MaxValue);
    private readonly FrameMirror _mirror = new();

    private Guid? _owner;
    private string _reason = string.Empty;
    private bool _regrant = true;
    private string? _screenDrawn;
    private readonly ServiceScreenSettingsStore? _screenStore;
    private ServiceScreenSettings _screenSettings;

    public SessionBroker(IDisplay display, ISessionMonitor sessions, IOptions<ServiceOptions> options, TimeProvider time,
        ILogger<SessionBroker> logger, ServiceScreenSettingsStore? screenSettings = null)
    {
        _screenStore = screenSettings;
        _screenSettings = screenSettings?.Load() ?? ServiceScreenSettingsStore.Default;
        _display = display;
        _sessions = sessions;
        _options = options.Value;
        _time = time;
        _logger = logger;

        _sessions.Changed += () => _wakeUp.Release();
        _display.Gesture += OnGesture;
        _display.StatusChanged += OnDeviceStatus;
    }

    public Guid? Owner => _owner;

    // O WigiDash virtual não tem tela física para proteger de quem está longe dela: sessão remota pode usá-lo.
    private bool AllowRemoteSessions => _options.AllowRemoteSessions || _display.Status.Kind == DeviceKind.Virtual;
    public string Reason => _reason;

    public ServiceScreenSettings ScreenSettings => _screenSettings;

    /// <summary>Versão mostrada na tela do serviço: a do instalador, que é a mesma do manager (sem o "+commit").</summary>
    private static readonly string Version = typeof(SessionBroker).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";

    public async Task RegisterAsync(AgentSession agent, CancellationToken cancellationToken = default)
    {
        _agents[agent.Id] = agent;
        _logger.LogInformation("Agente conectado: {Agent}", agent);
        await agent.Channel.SendAsync(new Welcome(agent.SessionId, _display.Status), cancellationToken);
        await agent.Channel.SendAsync(_screenSettings, cancellationToken);
        await EvaluateAsync(cancellationToken);
    }

    public async Task UnregisterAsync(AgentSession agent, CancellationToken cancellationToken = default)
    {
        if (_agents.TryRemove(agent.Id, out _))
            _logger.LogInformation("Agente saiu: {Agent}", agent);
        await EvaluateAsync(cancellationToken);
    }

    /// <summary>Trata uma mensagem do agente. Comandos de tela de quem não é o dono são ignorados.</summary>
    public void Handle(AgentSession agent, IpcMessage message)
    {
        if (message is Hello hello)
        {
            agent.UserName = hello.UserName;
            return;
        }

        // Cada agente só fala da própria sessão (que vem do pipe), então qualquer um pode mandar o perfil.
        if (message is UserProfile profile)
        {
            var name = profile.DisplayName.Trim();
            agent.DisplayName = name.Length == 0 ? null : name.Length > 64 ? name[..64] : name;
            agent.Picture = profile.Picture.Length == UserProfile.PictureBytes ? profile.Picture : null;
            _wakeUp.Release();
            return;
        }

        // Ajuste da máquina, com a mesma regra da troca de aparelho: o dono ou, sem dono, qualquer um.
        if (message is ServiceScreenSettings settings)
        {
            if (_owner is null || _owner == agent.Id)
                ApplyScreenSettings(settings);
            return;
        }

        // Sem dono, qualquer agente troca o aparelho: é assim que quem entra pelo RDP ou pelo Windows Sandbox,
        // sem o WigiDash, liga o virtual e passa a ter a tela. Com dono, só ele troca.
        if (message is SelectDevice choice && _owner is null)
        {
            _display.SelectDevice(choice.Kind);
            return;
        }

        if (_owner != agent.Id)
            return;

        switch (message)
        {
            case Frame frame when frame.Page != ServiceOptions.ServicePage:
                if (_display.WriteFrame(frame.Page, frame.Pixels, frame.ByteOffset))
                    _mirror.Write(frame.Page, frame.Pixels, frame.ByteOffset);
                break;
            case ShowPage show when show.Page != ServiceOptions.ServicePage:
                if (_display.ShowPage(show.Page, show.Transition))
                    _mirror.Show(show.Page);
                break;
            case SubscribePreview subscribe:
                agent.WantsPreview = subscribe.Enabled;
                _mirror.MarkDirty();
                break;
            case EmulatedTouch touch:
                _display.InjectTouch(touch.Contact, touch.X, touch.Y);
                break;
            case SetBrightness brightness:
                _display.SetBrightness(brightness.Percent);
                break;
            case WakeDisplay:
                _display.Wake();
                break;
            case SleepDisplay:
                _display.Sleep();
                break;
            case SelectDevice select:
                _display.SelectDevice(select.Kind);
                break;
        }
    }

    /// <summary>Recalcula a posse e avisa quem ganhou e quem perdeu.</summary>
    public async Task EvaluateAsync(CancellationToken cancellationToken = default)
    {
        await _evaluate.WaitAsync(cancellationToken);
        try
        {
            var agents = _agents.Values.ToList();
            var snapshot = _sessions.Capture(agents.Select(a => a.SessionId).Distinct());
            var decision = OwnershipPolicy.Decide(
                agents.Select(a => new AgentCandidate(a.Id, a.SessionId, a.ConnectedAt)).ToList(),
                snapshot, AllowRemoteSessions);

            var previous = _owner;
            var changed = previous != decision.Owner;
            var regrant = _regrant && _display.Status.Connected;
            _owner = decision.Owner;
            _reason = decision.Reason;

            if (changed && previous is { } lost && _agents.TryGetValue(lost, out var loser))
                await TrySendAsync(loser, new Ownership(false, decision.Reason), cancellationToken);

            if (decision.Owner is { } won && _agents.TryGetValue(won, out var winner))
            {
                if (changed || regrant)
                {
                    _logger.LogInformation("Posse do WigiDash: {Agent} ({Reason})", winner, decision.Reason);
                    await TrySendAsync(winner, new Ownership(true, decision.Reason), cancellationToken);
                    // O aparelho liga no brilho padrão dele; o manager mostra o que está valendo, não o que supõe.
                    if (_display.GetBrightness() is { } brightness)
                        await TrySendAsync(winner, new BrightnessReport(brightness), cancellationToken);
                    _mirror.MarkDirty();
                }
            }
            else if (_display.Status.Connected)
            {
                DrawServiceScreen(changed || regrant);
            }

            if (_display.Status.Connected)
                _regrant = false;
        }
        finally
        {
            _evaluate.Release();
        }
    }

    /// <summary>Manda ao dono, se ele pediu, a página que está no aparelho quando ela muda.</summary>
    public async Task PublishPreviewAsync(CancellationToken cancellationToken = default)
    {
        if (_owner is not { } owner || !_agents.TryGetValue(owner, out var agent) || !agent.WantsPreview)
            return;
        if (_mirror.TakeIfChanged() is { } shown)
            await TrySendAsync(agent, new PreviewFrame(shown.Page, shown.Pixels), cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = Task.Run(() => PreviewLoopAsync(stoppingToken), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateAsync(stoppingToken);
                await _wakeUp.WaitAsync(_options.SessionPollInterval, _time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao reavaliar a posse do WigiDash");
            }
        }
    }

    // No máximo 10 prévias por segundo: é para conferir, não para espelhar vídeo.
    private async Task PreviewLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100), _time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await PublishPreviewAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyScreenSettings(ServiceScreenSettings settings)
    {
        if (settings == _screenSettings)
            return;
        _screenSettings = settings;
        _screenStore?.Save(settings);
        _logger.LogInformation("Ajustes da tela do serviço: {Settings}", settings);
        foreach (var other in _agents.Values)
            _ = TrySendAsync(other, settings, CancellationToken.None);
        _wakeUp.Release();
    }

    private void DrawServiceScreen(bool force)
    {
        // Redesenha ao mudar de estado, a cada minuto (relógio) e quando a lista de sessões muda.
        var now = _time.GetLocalNow();
        var model = new ServiceScreenModel(_reason, OwnershipPolicy.IsLocked(_reason), now.DateTime, Version, ListSessions());
        var key = Describe(model);
        if (!force && key == _screenDrawn)
            return;

        if (force)
            _logger.LogInformation("Sem dono; tela do serviço: {Reason}", _reason);
        var frame = ServiceScreen.Render(model);
        if (_display.WriteFrame(ServiceOptions.ServicePage, frame, 0)
            && _display.ShowPage(ServiceOptions.ServicePage, Transition.None))
        {
            _mirror.Write(ServiceOptions.ServicePage, frame, 0);
            _mirror.Show(ServiceOptions.ServicePage);
            _screenDrawn = key;
        }
    }

    /// <summary>Sessões para a tela do serviço, com nome e foto mandados pelo agente de cada uma (quando há agente).</summary>
    internal IReadOnlyList<ServiceScreenSession> ListSessions()
    {
        if (!_screenSettings.ShowSessions)
            return [];

        var console = _sessions.Capture([]).ConsoleSessionId;
        return _sessions.ListUserSessions()
            .OrderByDescending(s => s.SessionId == console)
            .ThenBy(s => s.SessionId)
            .Select(s =>
            {
                var agent = _agents.Values.Where(a => a.SessionId == s.SessionId).MaxBy(a => a.Picture is not null);
                return new ServiceScreenSession(agent?.DisplayName ?? s.UserName, s.State, s.Remote, agent?.Picture);
            })
            .ToList();
    }

    // O que muda o desenho: o minuto, o motivo e, de cada sessão, nome, estado, RDP e se tem foto.
    private static string Describe(ServiceScreenModel model) =>
        $"{model.Now:yyyyMMddHHmm}|{model.Reason}|" + string.Join("|", model.Sessions.Select(s =>
            $"{s.Name}:{s.State}:{s.Remote}:{(s.Picture is null ? 0 : s.Picture.GetHashCode())}"));

    private void OnGesture(GestureEvent gesture)
    {
        if (_owner is { } owner && _agents.TryGetValue(owner, out var agent))
            _ = TrySendAsync(agent, new Gesture(gesture), CancellationToken.None);
        else if (gesture.Kind == GestureKind.Tap)
            _display.Wake();
    }

    private void OnDeviceStatus(DeviceStatus status)
    {
        foreach (var agent in _agents.Values)
            _ = TrySendAsync(agent, status, CancellationToken.None);

        // Aparelho novo ou reconectado: as páginas estão vazias, o dono precisa redesenhar.
        _regrant = true;
        _screenDrawn = null;
        _wakeUp.Release();
    }

    private async Task TrySendAsync(AgentSession agent, IpcMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await agent.Channel.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            _logger.LogDebug("Não deu para avisar {Agent}: {Message}", agent, ex.Message);
        }
    }
}

internal static class SemaphoreSlimExtensions
{
    /// <summary>Espera um sinal ou o tempo acabar, usando o relógio injetado (testável).</summary>
    public static async Task WaitAsync(this SemaphoreSlim semaphore, TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(timeout, time, cts.Token);
        var signal = semaphore.WaitAsync(cts.Token);
        await Task.WhenAny(delay, signal);
        cts.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
    }
}
