namespace OpenWigi.Service.Sessions;

/// <summary>Um agente conectado, do ponto de vista da decisão de posse.</summary>
public sealed record AgentCandidate(Guid AgentId, int SessionId, DateTimeOffset ConnectedAt);

/// <summary>
/// Estado das sessões do Windows num instante. <paramref name="ConsoleHasUser"/> é falso quando o console está na
/// tela de logon, por exemplo enquanto o usuário usa a própria sessão pelo RDP.
/// </summary>
public sealed record SessionSnapshot(int? ConsoleSessionId, bool ConsoleLocked, IReadOnlySet<int> ActiveSessions,
    bool ConsoleHasUser = true);

public sealed record OwnershipDecision(Guid? Owner, string Reason);

/// <summary>
/// Quem desenha no WigiDash. Regra: o agente da sessão que está no console físico, desbloqueada.
/// Sessões remotas só entram se for permitido (opção AllowRemoteSessions ou WigiDash virtual) e o console não tiver dono.
/// </summary>
public static class OwnershipPolicy
{
    public const string LockedReason = "Sessão bloqueada";
    public const string NoConsoleUserReason = "Nenhum usuário no console";

    /// <summary>Motivos em que o PC está fechado para quem está na frente dele: a tela do serviço mostra o cadeado.</summary>
    public static bool IsLocked(string reason) => reason is LockedReason or NoConsoleUserReason;

    public static OwnershipDecision Decide(IReadOnlyCollection<AgentCandidate> agents, SessionSnapshot sessions, bool allowRemoteSessions)
    {
        if (sessions.ConsoleSessionId is { } console)
        {
            var local = agents.Where(a => a.SessionId == console).OrderByDescending(a => a.ConnectedAt).FirstOrDefault();
            if (local is not null)
            {
                return sessions.ConsoleLocked
                    ? new OwnershipDecision(null, LockedReason)
                    : new OwnershipDecision(local.AgentId, "Sessão ativa no console");
            }
        }

        if (allowRemoteSessions)
        {
            var remote = agents
                .Where(a => a.SessionId != sessions.ConsoleSessionId && sessions.ActiveSessions.Contains(a.SessionId))
                .OrderByDescending(a => a.ConnectedAt)
                .FirstOrDefault();
            if (remote is not null)
                return new OwnershipDecision(remote.AgentId, "Sessão remota ativa");
        }

        return new OwnershipDecision(null, sessions.ConsoleSessionId is null || !sessions.ConsoleHasUser
            ? NoConsoleUserReason
            : "OpenWigiDash não está aberto para o usuário do console");
    }
}
