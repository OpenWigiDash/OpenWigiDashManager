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

    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Aparelho ativo enquanto ninguém escolheu um no manager (a escolha de lá fica em
    /// %ProgramData%\OpenWigiDash\Service\device.txt e vale mais). <see cref="DeviceKind.Virtual"/> é um
    /// WigiDash de mentira: o manager desenha na prévia, onde o mouse vira toque.
    /// </summary>
    public DeviceKind Device { get; set; } = DeviceKind.Usb;
}
