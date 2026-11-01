using System.Text.Json.Nodes;

namespace OpenWigi.Plugins.Tests;

public sealed class VerifiedListTests : IDisposable
{
    private const string Id = "Autor.OpenWigiDash.Teste";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "owd-verified-" + Guid.NewGuid().ToString("N"));
    private readonly FakeFeed _feed = new();
    private string Widgets => Path.Combine(_root, "Widgets");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static VerifiedList List(string owner = "autor", params string[] blocked) => VerifiedList.Parse(new JsonObject
    {
        ["version"] = 1,
        ["plugins"] = new JsonArray(new JsonObject
        {
            ["id"] = Id,
            ["owner"] = owner,
            ["blockedVersions"] = new JsonArray(blocked.Select(b => (JsonNode)b).ToArray()),
            ["reason"] = new JsonObject { ["1.1.0"] = "Trava ao abrir as configurações" },
        }),
    }.ToJsonString());

    [Theory]
    [InlineData("1.0.0", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.1", false)]
    [InlineData("[1.0.0, 1.2.0)", "1.1.5", true)]
    [InlineData("[1.0.0, 1.2.0)", "1.2.0", false)]
    [InlineData("[1.0.0, 1.2.0)", "1.0.0-preview.1", false)]
    [InlineData("(, 2.0.0]", "2.0.0", true)]
    [InlineData("(1.0.0, )", "1.0.0", false)]
    [InlineData("[1.5.0]", "1.5.0", true)]
    public void Faixa_do_NuGet_ou_versao_exata(string range, string version, bool expected)
    {
        Assert.True(VersionRange.TryParse(range, out var parsed));
        Assert.Equal(expected, parsed.Contains(PackageVersion.Parse(version)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[,]")]
    [InlineData("(1.0.0)")]
    [InlineData("[abc, 2.0.0)")]
    public void Faixa_invalida_e_recusada(string range) => Assert.False(VersionRange.TryParse(range, out _));

    [Fact]
    public void Verificado_so_quando_o_dono_no_nuget_org_bate_com_a_entrada()
    {
        var list = List();

        Assert.True(list.IsVerified(Id, "outra, autor"));
        Assert.False(list.IsVerified(Id, "impostor"));
        Assert.False(list.IsVerified("Outro.Id", "autor"));
    }

    [Fact]
    public void Entrada_sem_dono_so_bloqueia()
    {
        var list = VerifiedList.Parse("""{ "version": 1, "plugins": [ { "id": "X", "blockedVersions": ["1.0.0"] } ] }""");

        Assert.False(list.IsVerified("X", "qualquer"));
        Assert.True(list.IsBlocked("X", PackageVersion.Parse("1.0.0")));
        Assert.Equal("Versão bloqueada pelos mantenedores do OpenWigiDash.", list.BlockReason("X", PackageVersion.Parse("1.0.0")));
    }

    [Fact]
    public void Lista_com_faixa_invalida_ou_versao_de_formato_nova_e_recusada()
    {
        Assert.Throws<InvalidDataException>(() => VerifiedList.Parse("""{ "version": 1, "plugins": [ { "id": "X", "blockedVersions": ["[,]"] } ] }"""));
        Assert.Throws<InvalidDataException>(() => VerifiedList.Parse("""{ "version": 2, "plugins": [] }"""));
    }

    [Fact]
    public async Task Versao_bloqueada_nao_instala_nem_aparece_como_atualizacao()
    {
        _feed.Publish(Id, "1.0.0");
        _feed.Publish(Id, "1.1.0");
        var feed = _feed.CreateFeed();
        var installer = new PluginInstaller(feed, Widgets, Path.Combine(_root, "cache"));
        feed.Policy = List(blocked: "1.1.0");

        var installed = await installer.InstallAsync(Id);
        Assert.Equal("1.0.0", installed.Version.ToString());
        Assert.Empty(await installer.CheckUpdatesAsync());
        Assert.Null(await installer.UpdateAsync(Id));
        var ex = await Assert.ThrowsAsync<PluginInstallException>(() => installer.InstallAsync(Id, "1.1.0"));
        Assert.Contains("Trava ao abrir as configurações", ex.Message);
        Assert.Equal("1.0.0", Assert.Single(await feed.SearchAsync(null)).CompatibleVersion!.ToString());
    }

    [Fact]
    public async Task Fonte_guarda_copia_e_usa_offline_e_ignora_lista_quebrada()
    {
        var url = "https://lista.test/verified.json";
        var cache = Path.Combine(_root, "verified.json");
        _feed.Route(url, System.Text.Encoding.UTF8.GetBytes("""{ "version": 1, "plugins": [ { "id": "X", "owner": "autor" } ] }"""), "application/json");
        var source = new VerifiedListSource(new HttpClient(_feed), cache, url);

        Assert.True((await source.LoadAsync()).IsVerified("X", "autor"));

        _feed.Offline = true;
        Assert.True((await source.LoadAsync()).IsVerified("X", "autor"));

        _feed.Offline = false;
        _feed.Route(url, "{ quebrado"u8.ToArray(), "application/json");
        Assert.True((await source.LoadAsync()).IsVerified("X", "autor")); // fica com a última boa

        Assert.Same(VerifiedList.Empty, new VerifiedListSource(new HttpClient(_feed), Path.Combine(_root, "nada.json"), "https://lista.test/404").LoadCached());
    }

    [Fact]
    public void Oficial_pela_conta_do_projeto_e_verificado_pela_lista()
    {
        var official = new FeedPackage("victorperez2911.X", "X", "", "", 0, null, PackageVersion.Parse("1.0.0"), null, Owners: "victorperez2911");
        var listed = new FeedPackage(Id, "T", "", "", 0, null, PackageVersion.Parse("1.0.0"), null, Owners: "autor");
        var other = listed with { Owners = "impostor" };
        var list = List();

        Assert.Equal(PluginTrust.Official, list.TrustOf(official));
        Assert.Equal(PluginTrust.Official, list.TrustOf(official with { Id = "OpenWigiDash.X", Owners = "OpenWigiDash" }));
        Assert.Equal(PluginTrust.Verified, list.TrustOf(listed));
        Assert.Equal(PluginTrust.Community, list.TrustOf(other));
    }

    [Fact]
    public void O_verified_json_do_repositorio_e_valido()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "plugins", "verified.json")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var list = VerifiedList.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "plugins", "verified.json")));

        Assert.True(list.IsVerified("OpenWigiDash.Discord", "OpenWigiDash"));
    }
}
