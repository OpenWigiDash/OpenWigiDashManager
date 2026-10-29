using Microsoft.Extensions.Options;
using OpenWigi.Device;
using OpenWigi.Device.Gestures;
using OpenWigi.Ipc;

namespace OpenWigi.Service.Device;

/// <summary>
/// Mantém o WigiDash aberto: conecta quando aparece, reconecta quando cai. É o único lugar
/// do sistema que abre o WinUSB.
/// </summary>
public sealed class DeviceHost(IOptions<ServiceOptions> options, ILogger<DeviceHost> logger) : BackgroundService
{
    private readonly Lock _gate = new();
    private WigiDashDevice? _device;

    public event Action<DeviceStatus>? StatusChanged;
    public event Action<GestureEvent>? Gesture;

    public DeviceStatus Status
    {
        get
        {
            lock (_gate)
                return _device is { } d ? new DeviceStatus(true, d.Info.Id, d.Info.Firmware) : DeviceStatus.Disconnected;
        }
    }

    /// <summary>Executa uma operação no aparelho, se estiver conectado. Falha de USB derruba a conexão.</summary>
    public bool TryUse(Action<WigiDashDevice> action)
    {
        WigiDashDevice? device;
        lock (_gate)
            device = _device;
        if (device is null)
            return false;

        try
        {
            action(device);
            return true;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Falha de USB; reconectando");
            Drop(device);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool connected;
            lock (_gate)
                connected = _device is not null;

            if (!connected)
                TryConnect();

            try
            {
                await Task.Delay(options.Value.ReconnectInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        WigiDashDevice? last;
        lock (_gate)
        {
            last = _device;
            _device = null;
        }
        last?.Dispose();
    }

    private void TryConnect()
    {
        WigiDashDevice device;
        try
        {
            device = WigiDashDevice.Open();
        }
        catch (IOException ex)
        {
            logger.LogDebug("WigiDash indisponível: {Message}", ex.Message);
            return;
        }

        device.Gesture += e => Gesture?.Invoke(e);
        device.Disconnected += _ => Drop(device);
        lock (_gate)
            _device = device;
        device.StartPolling();

        logger.LogInformation("WigiDash conectado: {Id}, firmware {Firmware}", device.Info.Id, device.Info.Firmware);
        StatusChanged?.Invoke(Status);
    }

    private void Drop(WigiDashDevice device)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_device, device))
                return;
            _device = null;
        }

        // Dispose pode ser chamado de dentro da thread de leitura; ele não espera a si mesma.
        _ = Task.Run(device.Dispose);
        logger.LogWarning("WigiDash desconectado");
        StatusChanged?.Invoke(DeviceStatus.Disconnected);
    }
}
