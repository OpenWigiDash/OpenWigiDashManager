namespace OpenWigi.Service.Sessions;

/// <summary>Estado de uma sessão de usuário, como aparece na tela do serviço.</summary>
public enum WindowsSessionState
{
    Active,
    Locked,
    Disconnected,
}

/// <summary>Uma sessão com usuário logado. <paramref name="Remote"/>: aberta por RDP, e não na frente do PC.</summary>
public sealed record WindowsSession(int SessionId, string UserName, WindowsSessionState State, bool Remote);

public interface ISessionMonitor
{
    SessionSnapshot Capture(IEnumerable<int> knownSessions);

    /// <summary>Sessões com usuário logado (ativas, bloqueadas ou desconectadas), para a tela do serviço.</summary>
    IReadOnlyList<WindowsSession> ListUserSessions();

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

    public IReadOnlyList<WindowsSession> ListUserSessions()
    {
        var sessions = SessionNative.EnumerateSessions();
        if (sessions is null)
            return [];

        var list = new List<WindowsSession>();
        foreach (var (id, connectState) in sessions)
        {
            if (connectState is not (SessionNative.WTSActive or SessionNative.WTSDisconnected))
                continue;
            var user = SessionNative.UserName(id);
            if (user.Length == 0)
                continue;
            var state = connectState == SessionNative.WTSDisconnected ? WindowsSessionState.Disconnected
                : SessionNative.IsLocked(id) == true ? WindowsSessionState.Locked
                : WindowsSessionState.Active;
            list.Add(new WindowsSession(id, user, state, state != WindowsSessionState.Disconnected && SessionNative.IsRemote(id)));
        }
        return list;
    }

    public void NotifyChanged() => Changed?.Invoke();
}
