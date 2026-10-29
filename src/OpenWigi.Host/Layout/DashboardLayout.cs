using System.Text.Json;
using System.Text.Json.Serialization;
using OpenWigi.Sdk;

namespace OpenWigi.Host.Layout;

/// <summary>Onde cada instância fica: páginas, e em cada uma, widgets presos à grade 5x4.</summary>
public sealed class DashboardLayout
{
    public List<LayoutPage> Pages { get; set; } = [new LayoutPage { Name = "Página 1" }];

    public IEnumerable<WidgetPlacement> AllPlacements => Pages.SelectMany(p => p.Widgets);
}

public sealed class LayoutPage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<WidgetPlacement> Widgets { get; set; } = [];

    /// <summary>Primeira posição livre (linha a linha) que cabe o tamanho, ou null.</summary>
    public (int Column, int Row)? FindFreeCell(WidgetSize size)
    {
        for (var row = 0; row + size.Rows <= WidgetGrid.Rows; row++)
            for (var column = 0; column + size.Columns <= WidgetGrid.Columns; column++)
                if (!Widgets.Any(w => w.Overlaps(column, row, size)))
                    return (column, row);
        return null;
    }
}

public sealed class WidgetPlacement
{
    public Guid InstanceId { get; set; } = Guid.NewGuid();
    public Guid WidgetId { get; set; }
    public int Column { get; set; }
    public int Row { get; set; }

    [JsonConverter(typeof(WidgetSizeJsonConverter))]
    public WidgetSize Size { get; set; } = WidgetSize.Small;

    /// <summary>Retângulo na tela, em pixels.</summary>
    public PixelRect Bounds
    {
        get
        {
            var (x, y) = WidgetGrid.CellOrigin(Column, Row);
            var pixels = Size.PixelSize;
            return new PixelRect(x, y, pixels.Width, pixels.Height);
        }
    }

    public bool Overlaps(int column, int row, WidgetSize size) =>
        column < Column + Size.Columns && Column < column + size.Columns &&
        row < Row + Size.Rows && Row < row + size.Rows;
}

/// <summary>Grava "2x1", o mesmo formato do manager antigo e do <see cref="WidgetSize.ToString"/>.</summary>
internal sealed class WidgetSizeJsonConverter : JsonConverter<WidgetSize>
{
    public override WidgetSize Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        WidgetSize.Parse(reader.GetString() ?? "1x1");

    public override void Write(Utf8JsonWriter writer, WidgetSize value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>Lê e grava o layout em JSON. Gravação atômica: escreve ao lado e troca.</summary>
public sealed class LayoutStore(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Path { get; } = path;

    public DashboardLayout Load()
    {
        if (!File.Exists(Path))
            return new DashboardLayout();
        var layout = JsonSerializer.Deserialize<DashboardLayout>(File.ReadAllText(Path), Json) ?? new DashboardLayout();
        if (layout.Pages.Count == 0)
            layout.Pages.Add(new LayoutPage { Name = "Página 1" });
        return layout;
    }

    public void Save(DashboardLayout layout)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(layout, Json));
        File.Move(temp, Path, overwrite: true);
    }
}
