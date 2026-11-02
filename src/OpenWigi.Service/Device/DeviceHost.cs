using Microsoft.Extensions.Options;
using OpenWigi.Device;
using OpenWigi.Device.Gestures;
using OpenWigi.Device.Usb;
using OpenWigi.Ipc;

namespace OpenWigi.Service.Device;

/// <summary>
/// Mantém o WigiDash aberto: conecta quando aparece, reconecta quando cai. É o único lugar
/// do sistema que abre o WinUSB. Se a conexão cai seguidas vezes (ex.: toda escrita bulk dá timeout),
/// espera cada vez mais entre as tentativas e reseta o USB do aparelho, em vez de abrir e cair para sempre.
/// </summary>
public sealed class DeviceHost : BackgroundService
{
    private readonly IOptions<ServiceOptions> _options;
    private readonly ILogger<DeviceHost> _logger;
    private readonly TimeProvider _time;
    private readonly ReconnectPolicy _policy;
    private readonly Lock _gate = new();
    private WigiDashDevice? _device;
    private string? _devicePath;
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;
    private string? _resetPath;
    private Task _disposing = Task.CompletedTask;

    public DeviceHost(IOptions<ServiceOptions> options, ILogger<DeviceHost> logger, TimeProvider time)
    {
        _options = options;
        _logger = logger;
        _time = time;
        _policy = new ReconnectPolicy(options.Value, time);
    }

    public event Action<DeviceStatus>? StatusChanged;
    public event Action<GestureEvent>? Gesture;

    public DeviceStatus Status
    {
        get
        {
            lock (_gate)
                return _device is { } d
                    ? new DeviceStatus(true, d.Info.Id, d.Info.Firmware, Reconnecting: _policy.Recovering)
                    : DeviceStatus.Disconnected with { Reconnecting = _policy.Recovering };
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
            _logger.LogWarning(ex, "Falha de USB; reconectando");
            Drop(device);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool connected, stable = false;
            string? resetPath;
            lock (_gate)
            {
                connected = _device is not null;
                if (connected)
                    stable = _policy.CheckStable();
                resetPath = _resetPath;
                _resetPath = null;
            }

            if (stable)
            {
                _logger.LogInformation("Conexão com o WigiDash estável de novo");
                StatusChanged?.Invoke(Status);
            }

            if (resetPath is not null && !connected)
                await ResetAsync(resetPath);

            if (!connected && _time.GetUtcNow() >= _retryAt)
                TryConnect();

            try
            {
                await Task.Delay(_options.Value.ReconnectInterval, _time, stoppingToken);
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
        var path = WinUsbDeviceFinder.FindDevicePaths().FirstOrDefault();
        if (path is null)
        {
            bool recoveryEnded;
            lock (_gate)
                recoveryEnded = _policy.Absent();
            if (recoveryEnded)
            {
                _logger.LogInformation("WigiDash saiu do cabo; parando de tentar recuperar a conexão");
                StatusChanged?.Invoke(Status);
            }
            return;
        }

        WigiDashDevice device;
        try
        {
            device = WigiDashDevice.Open(path);
        }
        catch (IOException ex)
        {
            OpenFailed(path, ex);
            return;
        }

        device.Gesture += e => Gesture?.Invoke(e);
        device.Disconnected += _ => Drop(device);
        lock (_gate)
        {
            _device = device;
            _devicePath = path;
            _policy.Connected();
        }
        device.StartPolling();

        _logger.LogInformation("WigiDash conectado: {Id}, firmware {Firmware}", device.Info.Id, device.Info.Firmware);
        StatusChanged?.Invoke(Status);
    }

    // O aparelho está na lista do Windows mas não abriu. Fora de uma recuperação isso é normal (outro
    // programa, como o serviço da G.Skill, está com ele) e só tenta de novo. Durante uma recuperação
    // conta como mais uma falha, para chegar ao reset.
    private void OpenFailed(string path, IOException ex)
    {
        bool recovering, resetDue = false;
        lock (_gate)
        {
            recovering = _policy.Recovering;
            if (recovering)
            {
                resetDue = _policy.OpenFailed();
                _retryAt = _time.GetUtcNow() + _policy.NextDelay;
                if (resetDue)
                    _resetPath = path;
            }
        }

        if (recovering)
            _logger.LogWarning("WigiDash não abriu ({Failures} falhas seguidas): {Message}", _policy.Failures, ex.Message);
        else
            _logger.LogDebug("WigiDash indisponível: {Message}", ex.Message);
    }

    private void Drop(WigiDashDevice device)
    {
        bool resetDue;
        int failures;
        TimeSpan delay;
        lock (_gate)
        {
            if (!ReferenceEquals(_device, device))
                return;
            _device = null;
            resetDue = _policy.Dropped();
            failures = _policy.Failures;
            delay = _policy.NextDelay;
            _retryAt = _time.GetUtcNow() + delay;
            if (resetDue)
                _resetPath = _devicePath;

            // Dispose pode ser chamado de dentro da thread de leitura; ele não espera a si mesma.
            _disposing = Task.Run(device.Dispose);
        }

        _logger.LogWarning("WigiDash desconectado ({Failures} quedas seguidas); nova tentativa em {Delay:N0} s{Reset}",
            failures, delay.TotalSeconds, resetDue ? ", depois de resetar o USB" : string.Empty);
        StatusChanged?.Invoke(Status);
    }

    // Reseta o USB do aparelho (como tirar e pôr o cabo). O handle velho precisa estar fechado antes.
    private async Task ResetAsync(string path)
    {
        Task disposing;
        lock (_gate)
            disposing = _disposing;
        try
        {
            await disposing.WaitAsync(TimeSpan.FromSeconds(5), _time);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("O handle antigo do WigiDash não fechou a tempo; resetando assim mesmo");
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Fechar o handle antigo do WigiDash falhou: {Message}", ex.Message);
        }

        var done = UsbDeviceReset.TryReset(path, out var detail);
        lock (_gate)
        {
            _policy.ResetDone();
            // O aparelho some e volta em uns segundos depois do reset.
            _retryAt = _time.GetUtcNow() + _options.Value.ReconnectInterval * 2;
        }

        if (done)
            _logger.LogWarning("USB do WigiDash resetado depois de {Failures} quedas seguidas: {Detail}", _policy.Failures, detail);
        else
            _logger.LogError("Não deu para resetar o USB do WigiDash ({Detail}). Se a tela não voltar, tire e ponha o cabo.", detail);
    }
}
