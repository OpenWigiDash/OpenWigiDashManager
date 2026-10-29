using System.ServiceProcess;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

namespace OpenWigi.Service.Sessions;

/// <summary>
/// Lifetime de serviço Windows que também recebe SERVICE_CONTROL_SESSIONCHANGE, para a troca
/// de usuário refletir no WigiDash na hora, sem esperar a consulta periódica.
/// </summary>
public sealed class SessionAwareServiceLifetime : WindowsServiceLifetime
{
    private readonly WtsSessionMonitor _monitor;

    public SessionAwareServiceLifetime(
        IHostEnvironment environment,
        IHostApplicationLifetime applicationLifetime,
        ILoggerFactory loggerFactory,
        IOptions<HostOptions> optionsAccessor,
        IOptions<WindowsServiceLifetimeOptions> windowsServiceOptionsAccessor,
        WtsSessionMonitor monitor)
        : base(environment, applicationLifetime, loggerFactory, optionsAccessor, windowsServiceOptionsAccessor)
    {
        _monitor = monitor;
        CanHandleSessionChangeEvent = true;
    }

    protected override void OnSessionChange(SessionChangeDescription changeDescription)
    {
        base.OnSessionChange(changeDescription);
        _monitor.NotifyChanged();
    }
}
