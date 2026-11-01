using System.Text.Json.Nodes;

namespace OpenWigi.Plugins.Tests;

/// <summary>O que a loja do manager usa da biblioteca: metadados da busca, ícones e checagem de atualização.</summary>
public sealed class PluginStoreTests : IDisposable
{
    private const string Id = "Autor.OpenWigiDash.Teste";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "owd-store-" + Guid.NewGuid().ToString("N"));
    private readonly FakeFeed _feed = new();
    private string Widgets => Path.Combine(_root, "Widgets");
    private string IconCache => Path.Combine(_root, "icons");
    private PluginInstaller Installer() => new(_feed.CreateFeed(), Widgets, Path.Combine(_root, "cache"));
    private PluginIconCache Icons() => new(new HttpClient(_feed), _feed.CreateFeed(), IconCache);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Plugin_publicado_contra_o_id_antigo_do_sdk_continua_compativel()
    {
        _feed.Publish(Id, "1.0.0", sdkId: PluginFeed.LegacySdkPackageId);

        var versions = await _feed.CreateFeed().GetVersionsAsync(Id);

        Assert.Equal("2.0.0-preview.3", Assert.Single(versions).SdkVersion!.ToString());
    }

    [Fact]
    public async Task Busca_traz_dono_links_seguros_versoes_e_o_sdk_que_falta()
    {
        _feed.Publish(Id, "1.0.0", sdk: "2.4.0");
        _feed.Publish(Id, "1.1.0", sdk: "2.5.0");
        _feed.Publish(Id, "1.2.0", sdk: "2.6.0", listed: false);
        _feed.SearchExtras[Id] = new JsonObject
        {
            ["owners"] = new JsonArray("victorperez2911"),
            ["projectUrl"] = "https://github.com/autor/teste",
            ["licenseUrl"] = "javascript:alert(1)",
            ["iconUrl"] = "file:///c:/windows/win.ini",
        };
        _feed.RefreshSearch();

        var hit = Assert.Single(await _feed.CreateFeed().SearchAsync(null));

        Assert.Equal("victorperez2911", hit.Owners);
        Assert.True(hit.Official);
        Assert.Equal("https://github.com/autor/teste", hit.ProjectUrl);
        Assert.Null(hit.LicenseUrl);
        Assert.Null(hit.IconUrl);
        Assert.Null(hit.CompatibleVersion);
        Assert.Equal("2.5.0", hit.RequiredSdk!.ToString()); // a mais nova listada
        Assert.Equal(new[] { "1.1.0", "1.0.0" }, hit.Versions!.Select(v => v.ToString()));
        Assert.Equal($"https://www.nuget.org/packages/{Id}", hit.GalleryUrl);
    }

    [Fact]
    public async Task So_dono_confiavel_no_nuget_org_conta_como_verificado_nao_o_id_nem_o_autor()
    {
        _feed.Publish("OpenWigiDash.Falso", "1.0.0");
        _feed.SearchExtras["OpenWigiDash.Falso"] = new JsonObject
        {
            ["owners"] = new JsonArray("outra-conta"),
            ["authors"] = new JsonArray("victorperez2911"),
        };
        _feed.RefreshSearch();

        var hit = Assert.Single(await _feed.CreateFeed().SearchAsync(null));

        Assert.False(hit.Official);
    }

    [Fact]
    public async Task Icone_vem_do_iconUrl_e_fica_em_cache_para_usar_offline()
    {
        _feed.Publish(Id, "1.0.0");
        _feed.SearchExtras[Id] = new JsonObject { ["iconUrl"] = "https://img.test/teste.png" };
        _feed.RefreshSearch();
        _feed.Route("https://img.test/teste.png", Png);
        var package = Assert.Single(await _feed.CreateFeed().SearchAsync(null));

        var icon = await Icons().GetAsync(package);

        Assert.Equal(Path.Combine(IconCache, Id.ToLowerInvariant(), "1.0.0.png"), icon);
        Assert.Equal(Png, File.ReadAllBytes(icon!));
        _feed.Offline = true;
        Assert.Equal(icon, await Icons().GetAsync(package));
    }

    [Fact]
    public async Task Sem_iconUrl_usa_o_icone_embutido_no_pacote_e_recusa_o_que_nao_e_imagem()
    {
        _feed.Publish(Id, "1.0.0");
        var package = Assert.Single(await _feed.CreateFeed().SearchAsync(null));
        _feed.Route($"https://feed.test/flat/{Id.ToLowerInvariant()}/1.0.0/icon", "<html>"u8.ToArray(), "text/html");

        Assert.Null(await Icons().GetAsync(package));

        _feed.Route($"https://feed.test/flat/{Id.ToLowerInvariant()}/1.0.0/icon", Png);
        Assert.Equal(Png, File.ReadAllBytes((await Icons().GetAsync(package))!));
    }

    [Fact]
    public async Task Offline_sem_cache_nao_tem_icone_e_nao_estoura()
    {
        _feed.Publish(Id, "1.0.0");
        var package = Assert.Single(await _feed.CreateFeed().SearchAsync(null));
        _feed.Offline = true;

        Assert.Null(await Icons().GetAsync(package));
    }

    [Fact]
    public async Task Instalado_usa_o_icone_da_propria_pasta()
    {
        _feed.Publish(Id, "1.0.0");
        await Installer().InstallAsync(Id);

        var installed = Installer().Find(Id)!;

        Assert.Equal(Path.Combine(Widgets, Id, "icon.png"), PluginIconCache.Installed(installed));
    }

    [Fact]
    public async Task Checagem_de_atualizacao_so_lista_quem_tem_compativel_mais_nova()
    {
        _feed.Publish(Id, "1.0.0");
        _feed.Publish("Autor.OpenWigiDash.Outro", "1.0.0");
        var installer = Installer();
        await installer.InstallAsync(Id);
        await installer.InstallAsync("Autor.OpenWigiDash.Outro");
        Directory.CreateDirectory(Path.Combine(Widgets, "Manual"));
        _feed.Publish(Id, "1.1.0");
        _feed.Publish("Autor.OpenWigiDash.Outro", "2.0.0", sdk: "3.0.0");

        var updates = await installer.CheckUpdatesAsync();

        var update = Assert.Single(updates);
        Assert.Equal(Id, update.Plugin.PackageId);
        Assert.Equal("1.1.0", update.Version.ToString());

        _feed.Offline = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => installer.CheckUpdatesAsync());
    }
}
