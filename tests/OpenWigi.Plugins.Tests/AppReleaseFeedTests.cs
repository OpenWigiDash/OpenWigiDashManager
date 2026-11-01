using System.Net;
using System.Text;

namespace OpenWigi.Plugins.Tests;

public class AppReleaseFeedTests
{
    private const string Url = "https://api.test/repos/x/y/releases";

    private const string Releases = """
        [
          { "tag_name": "v0.3.0-beta.1", "name": "", "draft": false, "prerelease": true,
            "html_url": "https://github.test/r/v0.3.0-beta.1", "body": "beta", "published_at": "2026-12-01T10:00:00Z", "assets": [] },
          { "tag_name": "v0.2.1", "name": "OpenWigiDash 0.2.1", "draft": true, "prerelease": false,
            "html_url": "https://github.test/r/v0.2.1", "body": "", "published_at": null, "assets": [] },
          { "tag_name": "v0.2.0", "name": "OpenWigiDash 0.2.0", "draft": false, "prerelease": false,
            "html_url": "https://github.test/r/v0.2.0", "body": "novidades", "published_at": "2026-11-20T12:30:00Z",
            "assets": [
              { "name": "OpenWigiDash.msi", "browser_download_url": "https://github.test/d/v0.2.0/OpenWigiDash.msi" },
              { "name": "OpenWigiDash-Setup.exe", "browser_download_url": "https://github.test/d/v0.2.0/OpenWigiDash-Setup.exe" }
            ] },
          { "tag_name": "nightly", "name": "nightly", "draft": false, "prerelease": true, "html_url": "", "assets": [] },
          { "tag_name": "v0.1.0", "name": "OpenWigiDash 0.1.0", "draft": false, "prerelease": false,
            "html_url": "https://github.test/r/v0.1.0", "body": "", "published_at": "2026-11-05T09:00:00Z", "assets": [] }
        ]
        """;

    [Fact]
    public void Le_as_releases_sem_rascunho_e_sem_tag_que_nao_e_versao()
    {
        var releases = AppReleaseFeed.Parse(Releases);

        Assert.Equal(new[] { "v0.3.0-beta.1", "v0.2.0", "v0.1.0" }, releases.Select(r => r.Tag));
        var stable = releases[1];
        Assert.Equal(PackageVersion.Parse("0.2.0"), stable.Version);
        Assert.Equal("OpenWigiDash 0.2.0", stable.Name);
        Assert.False(stable.IsPrerelease);
        Assert.Equal("https://github.test/r/v0.2.0", stable.PageUrl);
        Assert.Equal("novidades", stable.Notes);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 12, 30, 0, TimeSpan.Zero), stable.PublishedAt);
        Assert.Equal("https://github.test/d/v0.2.0/OpenWigiDash-Setup.exe", stable.SetupUrl);
        Assert.Null(releases[2].SetupUrl);
        // Sem nome na release, mostra a tag.
        Assert.Equal("v0.3.0-beta.1", releases[0].Name);
        Assert.True(releases[0].IsPrerelease);
    }

    [Theory]
    [InlineData("0.1.0", false, "v0.2.0")]
    [InlineData("0.1.0", true, "v0.3.0-beta.1")]
    [InlineData("0.2.0", false, null)]
    [InlineData("0.2.0", true, "v0.3.0-beta.1")]
    [InlineData("0.3.0-beta.1", true, null)]
    [InlineData("1.0.0", true, null)]
    public void Escolhe_a_mais_nova_acima_da_atual(string current, bool beta, string? expected)
    {
        var update = AppReleaseFeed.PickUpdate(PackageVersion.Parse(current), AppReleaseFeed.Parse(Releases), beta);

        Assert.Equal(expected, update?.Tag);
    }

    [Fact]
    public void Release_marcada_como_pre_release_no_GitHub_conta_como_beta_mesmo_sem_sufixo()
    {
        const string json = """
            [ { "tag_name": "v0.2.0", "draft": false, "prerelease": true, "html_url": "https://github.test/r/v0.2.0", "assets": [] } ]
            """;

        Assert.Null(AppReleaseFeed.PickUpdate(PackageVersion.Parse("0.1.0"), AppReleaseFeed.Parse(json), includePrerelease: false));
        Assert.Equal("v0.2.0", AppReleaseFeed.PickUpdate(PackageVersion.Parse("0.1.0"), AppReleaseFeed.Parse(json), includePrerelease: true)?.Tag);
    }

    [Fact]
    public void Resposta_que_nao_e_lista_nao_tem_releases()
    {
        Assert.Empty(AppReleaseFeed.Parse("""{ "message": "API rate limit exceeded" }"""));
    }

    [Fact]
    public async Task Pede_a_API_do_GitHub_com_o_cabecalho_de_versao()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Releases);
        var feed = new AppReleaseFeed(new HttpClient(handler), Url);

        var update = await feed.CheckAsync(PackageVersion.Parse("0.1.0"), includePrerelease: false);

        Assert.Equal("v0.2.0", update?.Tag);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(Url, request.RequestUri?.ToString());
        Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/vnd.github+json");
        Assert.True(request.Headers.Contains("X-GitHub-Api-Version"));
    }

    [Fact]
    public async Task Limite_da_API_estourado_sobe_como_erro()
    {
        var feed = new AppReleaseFeed(new HttpClient(new StubHandler(HttpStatusCode.Forbidden, "{}")), Url);

        await Assert.ThrowsAsync<HttpRequestException>(() => feed.CheckAsync(PackageVersion.Parse("0.1.0"), includePrerelease: false));
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
