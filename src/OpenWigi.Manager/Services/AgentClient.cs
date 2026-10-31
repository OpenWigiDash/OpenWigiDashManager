using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenWigi.Device.Protocol;
using OpenWigi.Host;
using OpenWigi.Host.Display;
using OpenWigi.Ipc;

namespace OpenWigi.Manager.Services;

public enum AgentStatus
{
    /// <summary>Serviço OpenWigiDash fora do ar ou não instalado.</summary>
    NoService,
    /// <summary>Conectado ao serviço, mas o aparelho não está ligado.</summary>
    NoDevice,
    /// <summary>Aparelho ligado, a tela está com outra sessão (ou bloqueada).</summary>
    Waiting,
    /// <summary>Este usuário está desenhando no aparelho.</summary>
    Owner,
}

/// <summary>
/// O agente do usuário dentro do manager: mantém a conexão com o serviço, liga e desliga o painel
/// de widgets conforme a posse da tela e guarda a prévia mais recente para a UI.
/// </summary>
public sealed class AgentClient : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private IpcConnection? _connection;
    private Task? _loop;

    private readonly ManagerSettings _settings;

    public AgentClient(ILoggerFactory loggerFactory, ManagerSettings settings)
    {
        _logger = loggerFactory.CreateLogger<AgentClient>();
        _settings = settings;
    }

    /// <summary>O painel de widgets. Ligado depois de criado, porque ele usa o <see cref="Sink"/> deste cliente.</summary>
    public DashboardHost Dashboard { get; private set; } = null!;

    public void Attach(DashboardHost dashboard) => Dashboard = dashboard;
    public AgentStatus Status { get; private set; } = AgentStatus.NoService;
    public string Reason { get; private set; } = "Procurando o serviço OpenWigiDash";
    public DeviceStatus Device { get; private set; } = DeviceStatus.Disconnected;
    public int SessionId { get; private set; } = -1;

    /// <summary>Brilho do aparelho em %: o que o serviço leu ao dar a posse, depois o último que a UI mandou.</summary>
    public int? Brightness { get; private set; }

    /// <summary>Muda o brilho agora e guarda a escolha, para reaplicar quando a tela voltar para este usuário.</summary>
    public async Task SetBrightnessAsync(int percent)
    {
        Brightness = percent;
        _settings.Brightness = percent;
        await SendAsync(new SetBrightness(percent));
    }

    /// <summary>Última cópia do que está no aparelho (RGB565, 1016x592), ou null.</summary>
    public byte[]? Preview { get; private set; }

    public event Action? Changed;
    public event Action? PreviewChanged;

    /// <summary>Destino dos pixels do painel: o serviço, pelo canal atual.</summary>
    public IDisplaySink Sink => new ConnectionSink(this);

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    public async Task SendAsync(IpcMessage message)
    {
        if (_connection is { } connection)
        {
            try
            {
                await connection.SendAsync(message);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                _logger.LogDebug("Envio ao serviço falhou: {Message}", ex.Message);
            }
        }
    }

    /// <summary>Este usuário está com a tela (mesmo que o aparelho ativo esteja desligado).</summary>
    public bool IsOwner { get; private set; }

    /// <summary>Troca o aparelho ativo da máquina (USB ou virtual). O serviço só aceita do dono da tela.</summary>
    public Task SelectDeviceAsync(DeviceKind kind) => IsOwner ? SendAsync(new SelectDevice(kind)) : Task.CompletedTask;

    public Task TouchAsync(TouchContact contact, int x, int y) =>
        Status == AgentStatus.Owner ? SendAsync(new EmulatedTouch(contact, x, y)) : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndServeAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or EndOfStreamException or InvalidDataException)
            {
                _logger.LogDebug("Sem conexão com o serviço: {Message}", ex.Message);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _connection = null;
            IsOwner = false;
            if (Dashboard.IsActive)
                await Dashboard.ActivateAsync(false, CancellationToken.None);
            Set(AgentStatus.NoService, "Serviço OpenWigiDash não encontrado. Ele está instalado e rodando?");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ConnectAndServeAsync(CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", IpcConnection.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(5), cancellationToken);
        await using var connection = new IpcConnection(pipe);
        await connection.SendAsync(new Hello(IpcConnection.ProtocolVersion, Environment.UserName), cancellationToken);
        await connection.SendAsync(new SubscribePreview(true), cancellationToken);
        _connection = connection;

        while (await connection.ReceiveAsync(cancellationToken) is { } message)
        {
            switch (message)
            {
                case Welcome welcome:
                    SessionId = welcome.SessionId;
                    Device = welcome.Device;
                    Set(welcome.Device.Connected ? AgentStatus.Waiting : AgentStatus.NoDevice,
                        welcome.Device.Connected ? "Aguardando a tela" : NoDeviceReason);
                    break;
                case Ownership ownership:
                    IsOwner = ownership.IsOwner;
                    await Dashboard.ActivateAsync(ownership.IsOwner, cancellationToken);
                    Set(ownership.IsOwner ? AgentStatus.Owner : Device.Connected ? AgentStatus.Waiting : AgentStatus.NoDevice,
                        ownership.Reason);
                    break;
                case DeviceStatus status:
                    Device = status;
                    if (!status.Connected)
                        Set(AgentStatus.NoDevice, NoDeviceReason);
                    else
                        Changed?.Invoke();
                    break;
                case Gesture gesture:
                    _ = Dashboard.HandleGestureAsync(gesture.Event, cancellationToken);
                    break;
                case BrightnessReport brightness:
                    // O aparelho pode ter voltado ao padrão dele (religado, outro usuário mexeu): vale a escolha deste usuário.
                    if (_settings.Brightness is { } chosen && chosen != brightness.Percent)
                    {
                        _logger.LogInformation("Brilho do aparelho em {Device}%; reaplicando os {Chosen}% escolhidos", brightness.Percent, chosen);
                        await connection.SendAsync(new SetBrightness(chosen), cancellationToken);
                        Brightness = chosen;
                    }
                    else
                    {
                        Brightness = brightness.Percent;
                    }
                    Changed?.Invoke();
                    break;
                case PreviewFrame frame:
                    Preview = frame.Pixels;
                    PreviewChanged?.Invoke();
                    break;
                case IpcError error:
                    Set(AgentStatus.NoService, $"O serviço recusou a conexão: {error.Message}");
                    return;
            }
        }
    }

    private const string NoDeviceReason = "WigiDash desconectado. Sem o aparelho? Escolha o WigiDash virtual em Ajustes.";

    private void Set(AgentStatus status, string reason)
    {
        Status = status;
        Reason = reason;
        Changed?.Invoke();
    }

    private sealed class ConnectionSink(AgentClient client) : IDisplaySink
    {
        public async ValueTask WriteAsync(int page, int byteOffset, ReadOnlyMemory<byte> rgb565, CancellationToken cancellationToken)
        {
            if (client._connection is { } c)
                await c.SendAsync(new Frame(page, byteOffset, rgb565.ToArray()), cancellationToken);
        }

        public async ValueTask ShowAsync(int page, Transition transition, CancellationToken cancellationToken)
        {
            if (client._connection is { } c)
                await c.SendAsync(new ShowPage(page, transition), cancellationToken);
        }
    }
}
