using System.IO.Compression;
using System.Text.Json.Nodes;
using OpenWigi.Host.Layout;

namespace OpenWigi.Host.Tests;

public sealed class LayoutArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "openwigi-archive-tests", Guid.NewGuid().ToString("N"));

    public LayoutArchiveTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static DashboardLayout LayoutWith(params Guid[] instances) => new()
    {
        Pages = [new LayoutPage { Name = "P", Widgets = instances.Select(id => new WidgetPlacement { InstanceId = id, WidgetId = Guid.NewGuid() }).ToList() }],
    };

    [Fact]
    public void Copia_leva_so_as_instancias_do_layout_e_os_arquivos_voltam_na_pasta_nova()
    {
        var kept = Guid.NewGuid();
        var other = Guid.NewGuid();
        var source = Path.Combine(_root, "origem");
        var keptFolder = Path.Combine(source, kept.ToString("D"));
        Directory.CreateDirectory(Path.Combine(keptFolder, "files", "ABC"));
        File.WriteAllText(Path.Combine(keptFolder, "settings.json"), "{}");
        var image = Path.Combine(keptFolder, "files", "ABC", "foto.png");
        File.WriteAllText(image, "png");
        File.WriteAllText(Path.Combine(keptFolder, "files.json"), new JsonObject { ["foto"] = image, ["fora"] = "/outro/lugar.png" }.ToJsonString());
        Directory.CreateDirectory(Path.Combine(source, other.ToString("D")));
        File.WriteAllText(Path.Combine(source, other.ToString("D"), "settings.json"), "{}");

        var zip = Path.Combine(_root, "copia.zip");
        LayoutArchive.Export(zip, LayoutWith(kept), source);

        using (var archive = ZipFile.OpenRead(zip))
        {
            var names = archive.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("layout.json", names);
            Assert.Contains($"instances/{kept:D}/files/ABC/foto.png", names);
            Assert.DoesNotContain(names, n => n.Contains(other.ToString("D")));
        }

        var layout = LayoutArchive.Read(zip);
        Assert.Equal(kept, Assert.Single(layout.AllPlacements).InstanceId);

        var target = Path.Combine(_root, "destino");
        LayoutArchive.ExtractInstances(zip, target, [kept]);
        var restored = Path.Combine(target, kept.ToString("D"));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(restored, "settings.json")));
        var files = JsonNode.Parse(File.ReadAllText(Path.Combine(restored, "files.json")))!;
        Assert.Equal(Path.Combine(restored, "files", "ABC", "foto.png"), files["foto"]!.GetValue<string>());
        Assert.Equal("png", File.ReadAllText(files["foto"]!.GetValue<string>()));
        Assert.Equal("/outro/lugar.png", files["fora"]!.GetValue<string>()); // fora da pasta da instância: como estava
    }

    [Fact]
    public void Restaurar_troca_a_pasta_da_instancia_e_nao_escreve_fora_dela()
    {
        var id = Guid.NewGuid();
        var zip = Path.Combine(_root, "copia.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(archive.CreateEntry("layout.json").Open()))
                w.Write(LayoutStore.Serialize(LayoutWith(id)));
            using (var w = new StreamWriter(archive.CreateEntry($"instances/{id:D}/settings.json").Open()))
                w.Write("novo");
            using (var w = new StreamWriter(archive.CreateEntry($"instances/{id:D}/../../fugiu.txt").Open()))
                w.Write("x");
        }
        var target = Path.Combine(_root, "destino");
        var folder = Path.Combine(target, id.ToString("D"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "velho.txt"), "some");

        LayoutArchive.ExtractInstances(zip, target, [id]);

        Assert.Equal("novo", File.ReadAllText(Path.Combine(folder, "settings.json")));
        Assert.False(File.Exists(Path.Combine(folder, "velho.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "fugiu.txt")));
        Assert.False(File.Exists(Path.Combine(target, "fugiu.txt")));
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("{ \"grid\": 99, \"pages\": [] }")]
    [InlineData("[1, 2]")]
    public void Arquivo_que_nao_serve_e_recusado(string text)
    {
        var path = Path.Combine(_root, "ruim.json");
        File.WriteAllText(path, text);
        Assert.Throws<InvalidDataException>(() => LayoutArchive.Read(path));
    }

    [Fact]
    public void Zip_sem_layout_e_recusado()
    {
        var zip = Path.Combine(_root, "outro.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntry("leia-me.txt");
        Assert.Throws<InvalidDataException>(() => LayoutArchive.Read(zip));
    }
}
