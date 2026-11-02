namespace OpenWigi.Service.Device;

/// <summary>
/// Decide o ritmo da reconexão do <see cref="DeviceHost"/>. Uma conexão que cai por falha de USB antes
/// de ficar estável conta como falha seguida; com falhas seguidas a espera entre tentativas dobra (até
/// <see cref="ServiceOptions.ReconnectMaxInterval"/>) e, a cada <see cref="ServiceOptions.ResetAfterFailures"/>,
/// é hora de resetar o USB do aparelho. Enquanto há falhas o aparelho está "reconectando", mesmo nos
/// segundos em que abre, para a UI não piscar entre conectado e desconectado.
/// </summary>
public sealed class ReconnectPolicy(ServiceOptions options, TimeProvider time)
{
    private DateTimeOffset? _connectedAt;
    private DateTimeOffset _lastReset = DateTimeOffset.MinValue;

    /// <summary>Falhas seguidas desde a última conexão estável (ou desde que o cabo saiu).</summary>
    public int Failures { get; private set; }

    /// <summary>O aparelho está ligado mas não está funcionando direito: o serviço está tentando de novo.</summary>
    public bool Recovering => Failures > 0;

    /// <summary>Espera até a próxima tentativa.</summary>
    public TimeSpan NextDelay
    {
        get
        {
            if (Failures <= 1)
                return options.ReconnectInterval;
            var factor = Math.Pow(2, Math.Min(Failures - 1, 16));
            var delay = options.ReconnectInterval * factor;
            return delay < options.ReconnectMaxInterval ? delay : options.ReconnectMaxInterval;
        }
    }

    public void Connected() => _connectedAt = time.GetUtcNow();

    /// <summary>A conexão caiu por falha de USB. True quando é hora de resetar o aparelho.</summary>
    public bool Dropped()
    {
        if (_connectedAt is { } at && time.GetUtcNow() - at >= options.StableAfter)
            Failures = 0; // ficou bem por um tempo: é uma falha nova, não a mesma de antes
        _connectedAt = null;
        return Fail();
    }

    /// <summary>O aparelho está na lista do Windows, mas não abriu. True quando é hora de resetar.</summary>
    public bool OpenFailed() => Fail();

    /// <summary>
    /// Nenhum WigiDash na lista do Windows: o cabo saiu. Logo depois de um reset é o próprio reset
    /// (o aparelho some por uns segundos), então ainda conta como reconectando.
    /// </summary>
    /// <returns>True se o estado de "reconectando" acabou agora.</returns>
    public bool Absent()
    {
        _connectedAt = null;
        if (Failures == 0 || time.GetUtcNow() - _lastReset < options.ResetGrace)
            return false;
        Failures = 0;
        return true;
    }

    /// <summary>Anota o reset, para a sumida do aparelho logo depois não contar como cabo fora.</summary>
    public void ResetDone() => _lastReset = time.GetUtcNow();

    /// <summary>True se a conexão atual acabou de passar do tempo de estável e as falhas foram zeradas.</summary>
    public bool CheckStable()
    {
        if (Failures == 0 || _connectedAt is not { } at || time.GetUtcNow() - at < options.StableAfter)
            return false;
        Failures = 0;
        return true;
    }

    private bool Fail()
    {
        Failures++;
        return options.ResetAfterFailures > 0 && Failures % options.ResetAfterFailures == 0;
    }
}
