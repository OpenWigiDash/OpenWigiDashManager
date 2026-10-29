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
}
