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
}
