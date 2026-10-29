using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using OpenWigi.Ipc;
using OpenWigi.Service.Sessions;

namespace OpenWigi.Service.Agents;

/// <summary>
/// Aceita agentes no pipe <see cref="IpcConnection.PipeName"/>. Só usuários autenticados desta
/// máquina conectam; acesso pela rede é negado.
/// </summary>
public sealed class AgentServer(SessionBroker broker, TimeProvider time, ILogger<AgentServer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = CreatePipe();
            }
            catch (IOException ex)
            {
                logger.LogError(ex, "Não foi possível criar o pipe {Pipe}", IpcConnection.PipeName);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                break;
            }

            _ = Task.Run(() => ServeAsync(pipe, stoppingToken), stoppingToken);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        await using var connection = new IpcConnection(pipe);
        if (!SessionNative.GetNamedPipeClientSessionId(pipe.SafePipeHandle, out var sessionId))
        {
            logger.LogWarning("Agente sem sessão identificável (erro {Error}); conexão recusada",
                System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
            return;
        }

        var agent = new AgentSession((int)sessionId, new PipeAgentChannel(connection), time.GetUtcNow());
        try
        {
            // O Hello vem antes de qualquer outra coisa, para o log já ter o nome do usuário.
            if (await connection.ReceiveAsync(stoppingToken) is not Hello hello)
                return;
            if (hello.ProtocolVersion != IpcConnection.ProtocolVersion)
            {
                await connection.SendAsync(new IpcError($"Versão de protocolo {hello.ProtocolVersion} não suportada; o serviço usa {IpcConnection.ProtocolVersion}."), stoppingToken);
                return;
            }
            broker.Handle(agent, hello);
            await broker.RegisterAsync(agent, stoppingToken);

            while (await connection.ReceiveAsync(stoppingToken) is { } message)
                broker.Handle(agent, message);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            logger.LogDebug("Conexão com {Agent} caiu: {Message}", agent, ex.Message);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await broker.UnregisterAsync(agent, CancellationToken.None);
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!,
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));

        return NamedPipeServerStreamAcl.Create(IpcConnection.PipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            inBufferSize: 1 << 20, outBufferSize: 64 * 1024, security);
    }
}
