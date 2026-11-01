using System.Xml.Linq;

namespace OpenWigi.Sdk.Tests;

public class PluginPackageTests
{
    [Fact]
    public void Targets_do_pacote_tira_os_mesmos_assemblies_que_o_host_compartilha()
    {
        var repo = AppContext.BaseDirectory[..AppContext.BaseDirectory.IndexOf($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)];
        var targets = XDocument.Load(Path.Combine(repo, "src", "OpenWigi.Sdk", "build", "victorperez2911.OpenWigi.Sdk.targets"));

        var excluded = targets.Descendants()
            .Where(e => e.Name.LocalName == "_OpenWigiSharedAssembly")
            .Select(e => (string)e.Attribute("Include")!);

        Assert.Equal(OpenWigiSdk.SharedAssemblies.Order(), excluded.Order());
    }

    [Fact]
    public void Versao_do_assembly_do_SDK_segue_o_contrato_e_nao_a_versao_da_build()
    {
        // Plugins referenciam OpenWigi.Sdk pela versão do assembly. Se ela seguisse a versão do produto
        // (o instalador compila com -p:Version=0.1.x), o host não acharia o 2.0.0.0 que os plugins pedem.
        var version = typeof(OpenWigiSdk).Assembly.GetName().Version!;

        Assert.Equal(new Version(OpenWigiSdk.Version.Major, OpenWigiSdk.Version.Minor, 0, 0), version);
    }
}
