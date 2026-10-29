namespace OpenWigi.Service.Sessions;

public interface ISessionMonitor
{
    SessionSnapshot Capture(IEnumerable<int> knownSessions);

    /// <summary>Disparado quando o Windows avisa de logon, logoff, lock, unlock ou troca de console.</summary>
    event Action? Changed;
}

/// <summary>
/// Lê o estado das sessões pelo WTS. Os avisos chegam pelo <see cref="SessionAwareServiceLifetime"/>
/// quando roda como serviço; em modo console o broker também consulta periodicamente.
/// </summary>
public sealed class WtsSessionMonitor : ISessionMonitor
{
    public event Action? Changed;

    public SessionSnapshot Capture(IEnumerable<int> knownSessions)
    {
        var console = SessionNative.WTSGetActiveConsoleSessionId();
        int? consoleId = console == SessionNative.NoSession ? null : (int)console;
        var locked = consoleId is { } id && SessionNative.IsLocked(id) == true;
        var active = knownSessions.Where(SessionNative.IsActive).ToHashSet();
        return new SessionSnapshot(consoleId, locked, active);
    }

    public void NotifyChanged() => Changed?.Invoke();
}
