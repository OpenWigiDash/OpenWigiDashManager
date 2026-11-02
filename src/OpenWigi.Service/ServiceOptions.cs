using OpenWigi.Ipc;

namespace OpenWigi.Service;

public sealed class ServiceOptions
{
    public const string Section = "OpenWigi";

    /// <summary>Página do hardware reservada para a tela do próprio serviço (sem dono, sessão bloqueada).</summary>
    public const int ServicePage = 2;

    /// <summary>
    /// Deixa uma sessão remota (RDP) ficar com o aparelho quando o console não tem dono.
    /// Desligado por padrão: quem está na frente do PC é quem usa a tela.
    /// </summary>
    public bool AllowRemoteSessions { get; set; }

    /// <summary>Intervalo da consulta de sessões, além dos avisos do Windows.</summary>
    public TimeSpan SessionPollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Espera entre tentativas de abrir o aparelho, e a primeira espera depois de uma queda.</summary>
    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Teto da espera quando a conexão cai seguidas vezes (ela dobra a cada queda).</summary>
    public TimeSpan ReconnectMaxInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Tempo conectado sem falha para a conexão deixar de contar como "reconectando".</summary>
    public TimeSpan StableAfter { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>A cada tantas quedas seguidas o serviço reseta o USB do aparelho (0 desliga o reset).</summary>
    public int ResetAfterFailures { get; set; } = 3;

    /// <summary>Depois de um reset o aparelho some da lista por uns segundos; nesse tempo não é cabo fora.</summary>
    public TimeSpan ResetGrace { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Aparelho ativo enquanto ninguém escolheu um no manager (a escolha de lá fica em
    /// %ProgramData%\OpenWigiDash\Service\device.txt e vale mais). <see cref="DeviceKind.Virtual"/> é um
    /// WigiDash de mentira: o manager desenha na prévia, onde o mouse vira toque.
    /// </summary>
    public DeviceKind Device { get; set; } = DeviceKind.Usb;
}
