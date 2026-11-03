namespace OpenWigi.Service.Sessions;

/// <summary>Estado de uma sessão de usuário, como aparece na tela do serviço.</summary>
public enum WindowsSessionState
{
    Active,
    Locked,
    Disconnected,
}

/// <summary>
/// Uma sessão com usuário logado. <paramref name="UserName"/>: o nome completo da conta local, ou o login.
/// <paramref name="Remote"/>: aberta por RDP, e não na frente do PC.
/// </summary>
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
        var hasUser = consoleId is { } c && SessionNative.UserName(c).Length > 0;
        return new SessionSnapshot(consoleId, locked, active, hasUser);
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
            list.Add(new WindowsSession(id, DisplayName(id, user), state,
                state != WindowsSessionState.Disconnected && SessionNative.IsRemote(id)));
        }
        return list;
    }

    // Nome completo da conta local, guardado por login: muda raramente e a consulta roda a cada segundo sem dono.
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    private string DisplayName(int sessionId, string user)
    {
        // Conta de domínio: o nome viria do controlador, que o LocalService não consulta. Fica o login.
        if (!string.Equals(SessionNative.DomainName(sessionId), Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            return user;
        lock (_names)
        {
            if (!_names.TryGetValue(user, out var name))
                _names[user] = name = SessionNative.LocalFullName(user) is { Length: > 0 } full ? full : user;
            return name;
        }
    }

    public void NotifyChanged() => Changed?.Invoke();
}
