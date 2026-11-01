using System.Text.Json.Nodes;

namespace OpenWigi.Plugins.Tests;

public sealed class PluginInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "owd-plugins-" + Guid.NewGuid().ToString("N"));
    private readonly FakeFeed _feed = new();
    private string Widgets => Path.Combine(_root, "Widgets");
    private PluginInstaller Installer() => new(_feed.CreateFeed(), Widgets, Path.Combine(_root, "cache"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Instala_a_mais_nova_compativel_so_com_a_pasta_plugin()
    {
        _feed.Publish("Autor.OpenWigiDash.Teste", "1.0.0-preview.1");
        _feed.Publish("Autor.OpenWigiDash.Teste", "1.0.0-preview.2");
        _feed.Publish("Autor.OpenWigiDash.Teste", "2.0.0-preview.1", sdk: "2.1.0"); // pede um host mais novo

        var result = await Installer().InstallAsync("Autor.OpenWigiDash.Teste");

        Assert.Equal("1.0.0-preview.2", result.Version.ToString());
        Assert.True(result.Applied);
        var folder = Path.Combine(Widgets, "Autor.OpenWigiDash.Teste");
        Assert.True(File.Exists(Path.Combine(folder, "Teste.dll")));
        Assert.True(File.Exists(Path.Combine(folder, "wwwroot", "app.css")));
        Assert.Equal("png", File.ReadAllText(Path.Combine(folder, "icon.png")));
        Assert.False(File.Exists(Path.Combine(folder, "Ignorado.dll")));
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "plugin.json")))!;
        Assert.Equal("Teste.dll", (string?)manifest["entry"]);
        Assert.Equal("Autor.OpenWigiDash.Teste", (string?)manifest["package"]!["id"]);
        Assert.Equal("1.0.0-preview.2", (string?)manifest["package"]!["version"]);
        Assert.False(Directory.Exists(PendingChanges.PendingDirectory(Widgets)));
    }

    [Fact]
    public async Task So_versoes_incompativeis_da_erro_com_o_requisito()
    {
        _feed.Publish("Autor.OpenWigiDash.Novo", "1.0.0", sdk: "3.0.0");

        var error = await Assert.ThrowsAsync<PluginInstallException>(() => Installer().InstallAsync("Autor.OpenWigiDash.Novo"));

        Assert.Contains("requer OpenWigiDash com SDK 3.0", error.Message);
        Assert.False(Directory.Exists(Path.Combine(Widgets, "Autor.OpenWigiDash.Novo")));
    }

    [Fact]
    public async Task Versao_unlisted_fica_fora_da_escolha_mas_instala_se_pedida()
    {
        _feed.Publish("Autor.OpenWigiDash.Teste", "1.0.0");
        _feed.Publish("Autor.OpenWigiDash.Teste", "1.1.0", listed: false);

        Assert.Equal("1.0.0", (await Installer().InstallAsync("Autor.OpenWigiDash.Teste")).Version.ToString());
        Assert.Equal("1.1.0", (await Installer().InstallAsync("Autor.OpenWigiDash.Teste", "1.1.0")).Version.ToString());
    }

    [Fact]
    public async Task Hash_errado_nao_instala_nada()
    {
        _feed.Publish("Autor.OpenWigiDash.Teste", "1.0.0", wrongHash: Convert.ToBase64String(new byte[64]));

        var error = await Assert.ThrowsAsync<PluginInstallException>(() => Installer().InstallAsync("Autor.OpenWigiDash.Teste"));

        Assert.Contains("hash", error.Message);
        Assert.False(Directory.Exists(Path.Combine(Widgets, "Autor.OpenWigiDash.Teste")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "cache")));
    }

    [Fact]
    public async Task Zip_com_caminho_para_fora_da_pasta_e_recusado()
    {
        _feed.Publish("Autor.OpenWigiDash.Mau", "1.0.0");
        _feed.ReplacePackage("Autor.OpenWigiDash.Mau", "1.0.0", FakeFeed.BuildPackage("Autor.OpenWigiDash.Mau", "1.0.0", files: new Dictionary<string, string>
        {
            ["plugin/plugin.json"] = "{ \"entry\": \"Mau.dll\" }",
            ["plugin/../../fora.txt"] = "x",
        }));

        var error = await Assert.ThrowsAsync<PluginInstallException>(() => Installer().InstallAsync("Autor.OpenWigiDash.Mau"));

        Assert.Contains("fora da pasta", error.Message);
        Assert.False(File.Exists(Path.Combine(Widgets, "fora.txt")));
        Assert.False(File.Exists(Path.Combine(Widgets, PendingChanges.FolderName, "fora.txt")));
        Assert.False(Directory.Exists(Path.Combine(Widgets, "Autor.OpenWigiDash.Mau")));
    }

    [Fact]
    public async Task Pacote_sem_packageType_de_plugin_e_recusado()
    {
        _feed.Publish("Autor.Biblioteca", "1.0.0", plugin: false);

        var error = await Assert.ThrowsAsync<PluginInstallException>(() => Installer().InstallAsync("Autor.Biblioteca"));

        Assert.Contains("não é um plugin", error.Message);
    }

    [Fact]
    public async Task Pacote_sem_dependencia_do_SDK_e_recusado()
    {
        _feed.Publish("Autor.Outro", "1.0.0", sdk: null);

        var error = await Assert.ThrowsAsync<PluginInstallException>(() => Installer().InstallAsync("Autor.Outro"));

        Assert.Contains("não depende do SDK", error.Message);
    }

    [Fact]
    public async Task Plugin_em_uso_fica_pendente_e_entra_no_proximo_Apply()
    {
        _feed.Publish("Autor.OpenWigiDash.Teste", "1.0.0");
        var installer = Installer();
        await installer.InstallAsync("Autor.OpenWigiDash.Teste");
        _feed.Publish("Autor.OpenWigiDash.Teste", "1.1.0");
        var dll = Path.Combine(Widgets, "Autor.OpenWigiDash.Teste", "Teste.dll");

        InstallResult result;
        using (File.Open(dll, FileMode.Open, FileAccess.Read, FileShare.Read)) // como uma DLL carregada
        {
            result = (await installer.UpdateAsync("Autor.OpenWigiDash.Teste"))!;
            Assert.False(result.Applied);
            Assert.True(installer.Find("Autor.OpenWigiDash.Teste")!.PendingUpdate);
            Assert.NotEmpty(PendingChanges.Apply(Widgets));
        }

        Assert.Empty(PendingChanges.Apply(Widgets));
        Assert.Equal("1.0.0", result.Previous!.ToString());
        Assert.Equal("1.1.0", installer.Find("Autor.OpenWigiDash.Teste")!.Version!.ToString());
        Assert.False(installer.Find("Autor.OpenWigiDash.Teste")!.PendingUpdate);
    }

    [Fact]
    public async Task Atualizar_sem_versao_nova_nao_faz_nada()
    {
        _feed.Publish("Autor.OpenWigiDash.Teste", "1.0.0");
        var installer = Installer();
        await installer.InstallAsync("Autor.OpenWigiDash.Teste");

        Assert.Null(await installer.UpdateAsync("Autor.OpenWigiDash.Teste"));
    }

    [Fact]
    public async Task Remover_tira_a_pasta_e_em_uso_espera()
    {
        _feed.Publish("Autor.OpenWigiDash.Teste", "1.0.0");
        var installer = Installer();
        await installer.InstallAsync("Autor.OpenWigiDash.Teste");
        var dll = Path.Combine(Widgets, "Autor.OpenWigiDash.Teste", "Teste.dll");

        using (File.Open(dll, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(installer.Remove("Autor.OpenWigiDash.Teste"));
            Assert.True(installer.Find("Autor.OpenWigiDash.Teste")!.PendingRemoval);
        }

        Assert.Empty(PendingChanges.Apply(Widgets));
        Assert.Null(installer.Find("Autor.OpenWigiDash.Teste"));
        Assert.Empty(installer.List());
    }

    [Fact]
    public async Task Lista_mostra_origem_do_feed_e_copia_a_mao()
    {
        _feed.Publish("Autor.OpenWigiDash.Teste", "1.0.0");
        var installer = Installer();
        await installer.InstallAsync("Autor.OpenWigiDash.Teste");
        Directory.CreateDirectory(Path.Combine(Widgets, "Manual"));

        var list = installer.List();

        Assert.Equal(new[] { "Autor.OpenWigiDash.Teste", "Manual" }, list.Select(p => p.Folder));
        Assert.Equal("1.0.0", list[0].Version!.ToString());
        Assert.Null(list[1].PackageId);
    }

    [Fact]
    public async Task Busca_traz_a_versao_compativel()
    {
        _feed.Publish("Autor.OpenWigiDash.Teste", "1.0.0");
        _feed.Publish("Autor.OpenWigiDash.Teste", "1.1.0", sdk: "2.5.0");

        var results = await _feed.CreateFeed().SearchAsync("teste");

        var hit = Assert.Single(results);
        Assert.Equal("1.1.0", hit.LatestVersion.ToString());
        Assert.Equal("1.0.0", hit.CompatibleVersion!.ToString());
        Assert.Contains(_feed.Requests, r => r.Contains("packageType=OpenWigiDashPlugin") && r.Contains("prerelease=true"));
    }
}
