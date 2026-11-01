namespace OpenWigi.Plugins.Tests;

public class PackageVersionTests
{
    [Theory]
    [InlineData("1.0.0-preview.2", "1.0.0-preview.10")]
    [InlineData("1.0.0-preview.9", "1.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-1", "1.0.0-alpha")]
    [InlineData("2.0.0", "2.0.0.1")]
    [InlineData("1.9.9", "1.10.0")]
    public void Ordem_segue_o_SemVer(string lower, string higher)
    {
        Assert.True(PackageVersion.Parse(lower) < PackageVersion.Parse(higher));
        Assert.True(PackageVersion.Parse(higher) > PackageVersion.Parse(lower));
    }

    [Fact]
    public void Metadados_nao_contam_e_maiusculas_tambem_nao()
    {
        Assert.Equal(PackageVersion.Parse("1.0.0-Preview.1+abc"), PackageVersion.Parse("1.0.0-preview.1"));
    }

    [Theory]
    [InlineData("[2.0.0-preview.3, )", "2.0.0-preview.3")]
    [InlineData("2.0.0", "2.0.0")]
    [InlineData("(2.0.0,3.0.0]", "2.0.0")]
    public void Limite_inferior_da_faixa(string range, string lower)
    {
        Assert.Equal(PackageVersion.Parse(lower), PackageVersion.LowerBound(range));
    }

    [Theory]
    [InlineData("(, 3.0.0)")]
    [InlineData("")]
    [InlineData(null)]
    public void Faixa_sem_limite_inferior(string? range)
    {
        Assert.Null(PackageVersion.LowerBound(range));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.0.0-")]
    [InlineData("1.2.3.4.5")]
    public void Versao_invalida(string text)
    {
        Assert.False(PackageVersion.TryParse(text, out _));
    }
}
