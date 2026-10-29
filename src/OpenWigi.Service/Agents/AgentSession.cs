using OpenWigi.Ipc;

namespace OpenWigi.Service.Agents;

/// <summary>Um agente conectado. A sessão vem do processo do outro lado do pipe, não do que ele diz.</summary>
public sealed class AgentSession(int sessionId, IAgentChannel channel, DateTimeOffset connectedAt)
{
    public Guid Id { get; } = Guid.NewGuid();
    public int SessionId { get; } = sessionId;
    public string UserName { get; set; } = string.Empty;
    public DateTimeOffset ConnectedAt { get; } = connectedAt;
    public IAgentChannel Channel { get; } = channel;

    public override string ToString() => $"{UserName} (sessão {SessionId})";
}

/// <summary>Para onde o broker manda mensagens a um agente. Existe para os testes trocarem o pipe.</summary>
public interface IAgentChannel
{
    Task SendAsync(IpcMessage message, CancellationToken cancellationToken = default);
}

internal sealed class PipeAgentChannel(IpcConnection connection) : IAgentChannel
{
    public Task SendAsync(IpcMessage message, CancellationToken cancellationToken = default) =>
        connection.SendAsync(message, cancellationToken);
}
