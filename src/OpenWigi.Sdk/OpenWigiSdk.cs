namespace OpenWigi.Sdk;

/// <summary>Versão do contrato e o que o host compartilha com os plugins.</summary>
public static class OpenWigiSdk
{
    /// <summary>
    /// Versão do contrato. O major muda só com quebra; o host carrega widget com o mesmo major
    /// e minor menor ou igual ao dele (<see cref="IsCompatible"/>).
    /// </summary>
    public static Version Version { get; } = new(3, 2);

    /// <summary>
    /// Assemblies que o host carrega uma vez só e entrega a todos os plugins. O plugin compila contra
    /// eles mas não deve levar cópia própria na pasta (no csproj: <c>ExcludeAssets="runtime"</c> ou
    /// <c>Private="false"</c>). Uma cópia por plugin quebraria a troca de tipos com o host.
    /// </summary>
    public static IReadOnlyList<string> SharedAssemblies { get; } =
    [
        "OpenWigi.Sdk",
        "Microsoft.AspNetCore.Components",
        "Microsoft.AspNetCore.Components.Forms",
        "Microsoft.AspNetCore.Components.Web",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "MudBlazor",
    ];

    /// <summary>Faixa do MudBlazor que o host carrega; plugin compila contra a mesma major.</summary>
    public const string MudBlazorVersion = "9.*";

    public static bool IsCompatible(Version widgetSdk) =>
        widgetSdk.Major == Version.Major && widgetSdk.Minor <= Version.Minor;
}
