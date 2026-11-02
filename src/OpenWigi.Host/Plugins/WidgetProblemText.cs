using OpenWigi.Host.Layout;

namespace OpenWigi.Host.Plugins;

/// <summary>
/// Os textos de um widget que não está funcionando. Os mesmos no Canvas, no assistente de adicionar e no
/// tile que vai para o aparelho (e para a prévia), para a pessoa ver o mesmo aviso em todo lugar.
/// </summary>
public static class WidgetProblemText
{
    /// <summary>O que aconteceu, em poucas palavras ("Plugin removido").</summary>
    public static string Title(WidgetProblem problem) => problem.Kind switch
    {
        WidgetProblemKind.NotInstalled => "Plugin removido",
        WidgetProblemKind.PluginFailed when problem.Reason?.Contains("SDK", StringComparison.Ordinal) == true => "Não funciona nesta versão",
        WidgetProblemKind.PluginFailed => "Plugin não carregou",
        _ => "Widget com erro",
    };

    /// <summary>O que aconteceu e o que fazer, numa frase.</summary>
    public static string Detail(WidgetProblem problem) => problem.Kind switch
    {
        WidgetProblemKind.NotInstalled when problem.PluginFolder is { } folder =>
            $"O plugin {folder} não está mais instalado. Reinstale pela Loja ou tire este widget da página; as configurações dele ficam guardadas até lá.",
        WidgetProblemKind.NotInstalled => "O plugin deste widget não está instalado. Instale pela Loja ou tire este widget da página.",
        WidgetProblemKind.PluginFailed => $"O plugin {problem.PluginFolder} está instalado, mas não carregou nesta versão do OpenWigiDash: {problem.Reason}",
        _ => problem.Reason ?? "O widget deu erro ao abrir.",
    };

    /// <summary>Nome que o widget tinha: o da instância, senão o do tipo (lembrado de quando ele carregou).</summary>
    public static string Widget(WidgetPlacement placement, WidgetProblem problem) =>
        placement.Name is { Length: > 0 } name ? name : problem.WidgetName ?? "Widget desconhecido";

    /// <summary>De qual plugin ele era ("plugin X").</summary>
    public static string Plugin(WidgetProblem problem) =>
        problem.PluginFolder is { } folder ? $"plugin {folder}" : "plugin desconhecido";
}

/// <summary>
/// Desenha o tile de aviso de um widget que não está funcionando, no tamanho da área de conteúdo dele.
/// O host não tem biblioteca de desenho: quem tem (o manager) passa isto em <see cref="DashboardOptions.ProblemTile"/>.
/// </summary>
/// <param name="Title">De <see cref="WidgetProblemText.Title"/>.</param>
/// <param name="Widget">De <see cref="WidgetProblemText.Widget"/>.</param>
/// <param name="Plugin">De <see cref="WidgetProblemText.Plugin"/>.</param>
public sealed record ProblemTileRequest(string Title, string Widget, string Plugin, OpenWigi.Sdk.PixelSize Size);
