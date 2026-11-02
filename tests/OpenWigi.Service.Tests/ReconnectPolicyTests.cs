using Microsoft.Extensions.Time.Testing;
using OpenWigi.Service.Device;

namespace OpenWigi.Service.Tests;

public class ReconnectPolicyTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 0, 39, 42, TimeSpan.Zero));
    private readonly ServiceOptions _options = new();
    private readonly ReconnectPolicy _policy;

    public ReconnectPolicyTests() => _policy = new ReconnectPolicy(_options, _time);

    // O que aconteceu no PC de casa: abre, a primeira escrita bulk dá timeout, cai, abre de novo...
    private bool ConnectAndFailSoon()
    {
        _policy.Connected();
        _time.Advance(TimeSpan.FromMilliseconds(500));
        return _policy.Dropped();
    }

    [Fact]
    public void Quedas_seguidas_dobram_a_espera_ate_o_teto()
    {
        var delays = new List<TimeSpan>();
        for (var i = 0; i < 7; i++)
        {
            ConnectAndFailSoon();
            delays.Add(_policy.NextDelay);
        }

        Assert.Equal([2, 4, 8, 16, 30, 30, 30], delays.Select(d => (int)d.TotalSeconds));
        Assert.True(_policy.Recovering);
    }

    [Fact]
    public void A_cada_tres_quedas_seguidas_pede_reset_do_usb()
    {
        var resets = Enumerable.Range(0, 9).Select(_ => ConnectAndFailSoon()).ToList();
        Assert.Equal([false, false, true, false, false, true, false, false, true], resets);
    }

    [Fact]
    public void Reset_desligado_nunca_e_pedido()
    {
        _options.ResetAfterFailures = 0;
        Assert.DoesNotContain(true, Enumerable.Range(0, 6).Select(_ => ConnectAndFailSoon()));
    }

    [Fact]
    public void Conexao_que_fica_estavel_zera_as_falhas()
    {
        ConnectAndFailSoon();
        ConnectAndFailSoon();
        _policy.Connected();

        _time.Advance(_options.StableAfter - TimeSpan.FromSeconds(1));
        Assert.False(_policy.CheckStable());
        Assert.True(_policy.Recovering); // ainda "reconectando", mesmo conectado: a UI não pisca

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(_policy.CheckStable());
        Assert.False(_policy.Recovering);
        Assert.Equal(_options.ReconnectInterval, _policy.NextDelay);
        Assert.False(_policy.CheckStable()); // avisa uma vez só
    }

    [Fact]
    public void Queda_depois_de_muito_tempo_bem_comeca_a_contar_de_novo()
    {
        ConnectAndFailSoon();
        ConnectAndFailSoon();
        _policy.Connected();
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.False(_policy.Dropped());
        Assert.Equal(1, _policy.Failures);
    }

    [Fact]
    public void Cabo_fora_encerra_a_recuperacao()
    {
        ConnectAndFailSoon();
        Assert.True(_policy.Absent());
        Assert.False(_policy.Recovering);
        Assert.False(_policy.Absent());
    }

    [Fact]
    public void Aparelho_sumindo_logo_depois_do_reset_nao_e_cabo_fora()
    {
        ConnectAndFailSoon();
        ConnectAndFailSoon();
        Assert.True(ConnectAndFailSoon());
        _policy.ResetDone();

        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.False(_policy.Absent());
        Assert.True(_policy.Recovering);

        _time.Advance(_options.ResetGrace);
        Assert.True(_policy.Absent());
    }

    [Fact]
    public void Falha_ao_abrir_conta_para_o_reset()
    {
        ConnectAndFailSoon();
        Assert.False(_policy.OpenFailed());
        Assert.True(_policy.OpenFailed());
    }
}
