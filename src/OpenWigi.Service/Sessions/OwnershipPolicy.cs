namespace OpenWigi.Service.Sessions;

/// <summary>Um agente conectado, do ponto de vista da decisão de posse.</summary>
public sealed record AgentCandidate(Guid AgentId, int SessionId, DateTimeOffset ConnectedAt);

/// <summary>Estado das sessões do Windows num instante.</summary>
public sealed record SessionSnapshot(int? ConsoleSessionId, bool ConsoleLocked, IReadOnlySet<int> ActiveSessions);

public sealed record OwnershipDecision(Guid? Owner, string Reason);

/// <summary>
/// Quem desenha no WigiDash. Regra: o agente da sessão que está no console físico, desbloqueada.
/// Sessões remotas só entram se a opção permitir e o console não tiver dono.
/// </summary>
public static class OwnershipPolicy
{
    public static OwnershipDecision Decide(IReadOnlyCollection<AgentCandidate> agents, SessionSnapshot sessions, bool allowRemoteSessions)
    {
        if (sessions.ConsoleSessionId is { } console)
        {
            var local = agents.Where(a => a.SessionId == console).OrderByDescending(a => a.ConnectedAt).FirstOrDefault();
            if (local is not null)
            {
                return sessions.ConsoleLocked
                    ? new OwnershipDecision(null, "Sessão bloqueada")
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
                return new OwnershipDecision(remote.AgentId, "Sessão remota ativa (opção AllowRemoteSessions)");
        }

        return new OwnershipDecision(null, sessions.ConsoleSessionId is null
            ? "Nenhum usuário no console"
            : "OpenWigiDash não está aberto para o usuário do console");
    }
}
