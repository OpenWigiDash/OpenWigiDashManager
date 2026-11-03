using OpenWigi.Service.Sessions;

namespace OpenWigi.Service.Tests;

public class OwnershipPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Victor = Guid.NewGuid(), Esposa = Guid.NewGuid(), Filha = Guid.NewGuid();

    private static SessionSnapshot Console(int? session, bool locked = false, params int[] active) =>
        new(session, locked, active.Concat(session is { } s ? [s] : []).ToHashSet());

    [Fact]
    public void Console_na_tela_de_logon_conta_como_sem_usuario()
    {
        // Victor usando a própria sessão pelo RDP: o console ficou na tela de logon, sem usuário.
        var agents = new[] { new AgentCandidate(Victor, 1, T0) };
        var decision = OwnershipPolicy.Decide(agents, Console(2, false, 1) with { ConsoleHasUser = false }, false);

        Assert.Null(decision.Owner);
        Assert.Equal(OwnershipPolicy.NoConsoleUserReason, decision.Reason);
        Assert.True(OwnershipPolicy.IsLocked(decision.Reason));
    }

    [Fact]
    public void Dono_e_o_agente_da_sessao_do_console()
    {
        var agents = new[] { new AgentCandidate(Victor, 1, T0), new AgentCandidate(Esposa, 3, T0) };
        Assert.Equal(Esposa, OwnershipPolicy.Decide(agents, Console(3), allowRemoteSessions: false).Owner);
    }

    [Fact]
    public void Troca_de_usuario_passa_a_posse_sem_o_antigo_sair()
    {
        // O Victor continua logado (sessão 1, desconectada); a filha entra na sessão 4.
        var agents = new[] { new AgentCandidate(Victor, 1, T0), new AgentCandidate(Filha, 4, T0.AddMinutes(5)) };
        Assert.Equal(Victor, OwnershipPolicy.Decide(agents, Console(1), false).Owner);
        Assert.Equal(Filha, OwnershipPolicy.Decide(agents, Console(4), false).Owner);
        Assert.Equal(Victor, OwnershipPolicy.Decide(agents, Console(1), false).Owner); // volta sem replugar nada
    }

    [Fact]
    public void Sessao_bloqueada_fica_sem_dono()
    {
        var decision = OwnershipPolicy.Decide([new AgentCandidate(Victor, 1, T0)], Console(1, locked: true), false);
        Assert.Null(decision.Owner);
        Assert.Equal("Sessão bloqueada", decision.Reason);
    }

    [Fact]
    public void Usuario_do_console_sem_agente_deixa_a_tela_com_o_servico()
    {
        var decision = OwnershipPolicy.Decide([new AgentCandidate(Victor, 1, T0)], Console(3), false);
        Assert.Null(decision.Owner);
    }

    [Fact]
    public void Sessao_remota_so_entra_com_a_opcao_ligada()
    {
        var agents = new[] { new AgentCandidate(Victor, 2, T0) };
        var snapshot = Console(1, locked: false, active: 2);
        Assert.Null(OwnershipPolicy.Decide(agents, snapshot, allowRemoteSessions: false).Owner);
        Assert.Equal(Victor, OwnershipPolicy.Decide(agents, snapshot, allowRemoteSessions: true).Owner);
    }

    [Fact]
    public void Sessao_remota_desconectada_nao_ganha_posse()
    {
        var agents = new[] { new AgentCandidate(Victor, 2, T0) };
        Assert.Null(OwnershipPolicy.Decide(agents, Console(1), allowRemoteSessions: true).Owner);
    }

    [Fact]
    public void Dois_agentes_na_mesma_sessao_o_mais_recente_vence()
    {
        var antigo = Guid.NewGuid();
        var novo = Guid.NewGuid();
        var agents = new[] { new AgentCandidate(antigo, 1, T0), new AgentCandidate(novo, 1, T0.AddSeconds(1)) };
        Assert.Equal(novo, OwnershipPolicy.Decide(agents, Console(1), false).Owner);
    }
}
