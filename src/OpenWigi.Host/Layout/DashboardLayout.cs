using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OpenWigi.Sdk;

namespace OpenWigi.Host.Layout;

/// <summary>Onde cada instância fica: páginas, e em cada uma, widgets presos à grade 10x8.</summary>
public sealed class DashboardLayout
{
    /// <summary>Versão da grade: 2 = 10x8 meias células. Layout sem este campo é da grade 5x4 e é convertido ao abrir.</summary>
    public const int CurrentGrid = 2;

    public int Grid { get; set; } = CurrentGrid;

    public List<LayoutPage> Pages { get; set; } = [new LayoutPage { Name = "Página 1" }];

    /// <summary>Página que estava no aparelho: o manager volta nela ao abrir. Pelo Id, para sobreviver a mover páginas.</summary>
    public Guid? CurrentPageId { get; set; }

    public IEnumerable<WidgetPlacement> AllPlacements => Pages.SelectMany(p => p.Widgets);
}

public sealed class LayoutPage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<WidgetPlacement> Widgets { get; set; } = [];

    /// <summary>Moldura dos tiles desta página (respiro, borda, cantos e cores).</summary>
    public TileFrame Frame { get; set; } = TileFrame.Default;

    /// <summary>Primeira posição livre (linha a linha) que cabe o tamanho, ou null.</summary>
    public (int Column, int Row)? FindFreeCell(WidgetSize size)
    {
        for (var row = 0; row + size.Rows <= WidgetGrid.Rows; row++)
            for (var column = 0; column + size.Columns <= WidgetGrid.Columns; column++)
                if (!Widgets.Any(w => w.Overlaps(column, row, size)))
                    return (column, row);
        return null;
    }

    /// <summary>
    /// O tamanho cabe em (<paramref name="column"/>, <paramref name="row"/>) sem sair da grade nem cobrir outro widget?
    /// <paramref name="except"/> não conta (o próprio widget que está sendo movido).
    /// </summary>
    public bool CanPlace(int column, int row, WidgetSize size, Guid? except = null) =>
        column >= 0 && row >= 0 && column + size.Columns <= WidgetGrid.Columns && row + size.Rows <= WidgetGrid.Rows &&
        !Widgets.Any(w => w.InstanceId != except && w.Overlaps(column, row, size));

    /// <summary>
    /// Onde pôr um widget deste tamanho: em <paramref name="preferred"/> se couber ali, senão na primeira posição livre.
    /// null = não cabe nesta página.
    /// </summary>
    public (int Column, int Row)? FindPlace(WidgetSize size, (int Column, int Row)? preferred = null, Guid? except = null)
    {
        if (preferred is { } p && CanPlace(p.Column, p.Row, size, except))
            return p;
        for (var row = 0; row + size.Rows <= WidgetGrid.Rows; row++)
            for (var column = 0; column + size.Columns <= WidgetGrid.Columns; column++)
                if (CanPlace(column, row, size, except))
                    return (column, row);
        return null;
    }
}

public sealed class WidgetPlacement
{
    public Guid InstanceId { get; set; } = Guid.NewGuid();
    public Guid WidgetId { get; set; }

    /// <summary>Nome desta instância, para distinguir dois do mesmo widget. Vazio = o nome do widget.</summary>
    public string? Name { get; set; }

    public int Column { get; set; }
    public int Row { get; set; }

    [JsonConverter(typeof(WidgetSizeJsonConverter))]
    public WidgetSize Size { get; set; } = WidgetSize.Small;

    /// <summary>Retângulo das células na tela, em pixels, com a moldura. Calculado: não vai para o layout.json.</summary>
    [JsonIgnore]
    public PixelRect Bounds => WidgetGrid.CellBounds(Column, Row, Size);

    /// <summary>Cabe inteiro na grade? Layout editado à mão ou com defeito pode ter widget saindo da tela.</summary>
    [JsonIgnore]
    public bool FitsGrid =>
        Column >= 0 && Row >= 0 && Column + Size.Columns <= WidgetGrid.Columns && Row + Size.Rows <= WidgetGrid.Rows;

    public bool Overlaps(int column, int row, WidgetSize size) =>
        column < Column + Size.Columns && Column < column + size.Columns &&
        row < Row + Size.Rows && Row < row + size.Rows;
}

/// <summary>Grava "4x2", o mesmo formato do <see cref="WidgetSize.ToString"/>.</summary>
internal sealed class WidgetSizeJsonConverter : JsonConverter<WidgetSize>
{
    public override WidgetSize Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        WidgetSize.Parse(reader.GetString() ?? "2x2");

    public override void Write(Utf8JsonWriter writer, WidgetSize value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>Grava a cor como "#RRGGBB".</summary>
internal sealed class WidgetColorJsonConverter : JsonConverter<WidgetColor>
{
    public override WidgetColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        TryParse(reader.GetString(), out var color) ? color : throw new JsonException($"Cor inválida: '{reader.GetString()}'.");

    public override void Write(Utf8JsonWriter writer, WidgetColor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());

    public static bool TryParse(string? text, out WidgetColor color)
    {
        color = default;
        if (text is not { Length: 7 } || text[0] != '#' ||
            !uint.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return false;
        color = new WidgetColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }
}

/// <summary>Lê e grava o layout em JSON. Gravação atômica: escreve ao lado e troca.</summary>
public sealed class LayoutStore(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new WidgetColorJsonConverter() },
    };

    public string Path { get; } = path;

    public DashboardLayout Load() => File.Exists(Path) ? Parse(File.ReadAllText(Path)) : new DashboardLayout();

    /// <summary>
    /// Lê um layout em JSON (o <c>layout.json</c>, uma cópia ou um arquivo exportado). Da grade 5x4 passa pela mesma
    /// conversão do <see cref="Load"/>; de versão mais nova, ou que não é layout, dá <see cref="InvalidDataException"/>.
    /// </summary>
    public static DashboardLayout Parse(string text)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("O arquivo não é um layout do OpenWigiDash.", ex);
        }
        if (root is not JsonObject obj || obj["pages"] is not JsonArray)
            throw new InvalidDataException("O arquivo não é um layout do OpenWigiDash.");
        var grid = GridOf(obj) ?? throw new InvalidDataException("O layout está com defeito: o campo \"grid\" não é um número.");
        if (grid > DashboardLayout.CurrentGrid)
            throw new InvalidDataException($"O layout é de uma versão mais nova do OpenWigiDash (grade {grid}).");
        if (grid < DashboardLayout.CurrentGrid)
            text = MigrateFromGrid1(text);
        DashboardLayout layout;
        try
        {
            layout = JsonSerializer.Deserialize<DashboardLayout>(text, Json) ?? new DashboardLayout();
        }
        // Valor fora do esperado (tamanho maior que a grade, cor inválida...) sai como ArgumentException ou
        // FormatException do próprio tipo, sem passar pelo JsonException.
        catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException or InvalidOperationException)
        {
            throw new InvalidDataException($"O layout está com defeito: {ex.Message}", ex);
        }
        // Página nula ou widget fora da grade não derrubam o resto: saem, e o que sobra abre.
        layout.Pages.RemoveAll(p => p is null);
        foreach (var page in layout.Pages)
        {
            page.Widgets ??= [];
            page.Widgets.RemoveAll(w => w is null || !w.FitsGrid);
        }
        if (layout.Pages.Count == 0)
            layout.Pages.Add(new LayoutPage { Name = "Página 1" });
        return layout;
    }

    /// <summary>O layout em JSON, como vai para o <c>layout.json</c>.</summary>
    public static string Serialize(DashboardLayout layout) => JsonSerializer.Serialize(layout, Json);

    /// <summary>Layout gravado antes da grade 10x8? Quem abre grava de volta depois de converter.</summary>
    /// <remarks>Arquivo ilegível dá false: quem diz o que está errado é o <see cref="Load"/>.</remarks>
    public bool NeedsMigration()
    {
        if (!File.Exists(Path))
            return false;
        try
        {
            return JsonNode.Parse(File.ReadAllText(Path)) is JsonObject obj && GridOf(obj) < DashboardLayout.CurrentGrid;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Sem o campo é a grade 5x4 (1); campo que não é número inteiro dá null.
    private static int? GridOf(JsonObject root)
    {
        if (root["grid"] is not { } node)
            return 1;
        return node is JsonValue value && value.TryGetValue<int>(out var grid) ? grid : null;
    }

    /// <summary>
    /// Tira do caminho um layout que não abre (de versão mais nova ou com defeito): move para
    /// <see cref="BackupDirectory"/> como <c>ilegivel-AAAAMMDD-HHMMSS.json</c>, para não ser sobrescrito pelo layout
    /// novo. Devolve o caminho para onde foi, ou null se não havia arquivo.
    /// </summary>
    public string? SetAside(DateTimeOffset now)
    {
        if (!File.Exists(Path))
            return null;
        Directory.CreateDirectory(BackupDirectory);
        var target = System.IO.Path.Combine(BackupDirectory, $"ilegivel-{now.LocalDateTime:yyyyMMdd-HHmmss}.json");
        File.Move(Path, target, overwrite: true);
        return target;
    }

    // Grade 5x4 → 10x8: cada célula vira 2x2 meias células, então posição e tamanho dobram e nada muda de
    // lugar na tela. As páginas ficam sem o campo "frame" e ganham a moldura padrão.
    private static string MigrateFromGrid1(string text)
    {
        var root = JsonNode.Parse(text)!.AsObject();
        foreach (var page in root["pages"]?.AsArray() ?? [])
            foreach (var widget in page?["widgets"]?.AsArray() ?? [])
            {
                if (widget is not JsonObject w)
                    continue;
                w["column"] = (w["column"]?.GetValue<int>() ?? 0) * 2;
                w["row"] = (w["row"]?.GetValue<int>() ?? 0) * 2;
                var size = WidgetSize5x4(w["size"]?.GetValue<string>() ?? "1x1");
                w["size"] = $"{size.Columns * 2}x{size.Rows * 2}";
            }
        root["grid"] = DashboardLayout.CurrentGrid;
        return root.ToJsonString();
    }

    private static (int Columns, int Rows) WidgetSize5x4(string text)
    {
        var parts = text.Split('x', 'X');
        return parts.Length == 2 && int.TryParse(parts[0], out var c) && int.TryParse(parts[1], out var r)
            ? (Math.Clamp(c, 1, 5), Math.Clamp(r, 1, 4))
            : (1, 1);
    }

    public void Save(DashboardLayout layout)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, Serialize(layout));
        File.Move(temp, Path, overwrite: true);
    }

    /// <summary>Pasta das cópias de <see cref="Backup"/>: <c>backups</c> ao lado do layout.</summary>
    public string BackupDirectory => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "backups");

    /// <summary>
    /// Copia o layout atual para <see cref="BackupDirectory"/> como <c>layout-AAAAMMDD-HHMMSS.json</c> e guarda só as
    /// <paramref name="keep"/> mais novas. Para voltar a uma: Ajustes > Layout, ou <see cref="DashboardHost.RestoreLayoutAsync"/>.
    /// Sem layout, ou igual à última cópia, não faz nada. Devolve o caminho da cópia nova, ou null.
    /// </summary>
    public string? Backup(DateTimeOffset now, int keep = 10)
    {
        if (!File.Exists(Path))
            return null;
        var current = File.ReadAllBytes(Path);
        Directory.CreateDirectory(BackupDirectory);
        var existing = Backups();
        if (existing.Count > 0 && File.ReadAllBytes(existing[0]).AsSpan().SequenceEqual(current))
            return null;

        var copy = System.IO.Path.Combine(BackupDirectory, $"layout-{now.LocalDateTime:yyyyMMdd-HHmmss}.json");
        File.WriteAllBytes(copy, current);
        foreach (var old in Backups().Skip(Math.Max(1, keep)))
            File.Delete(old);
        return copy;
    }

    /// <summary>As cópias de <see cref="Backup"/>, da mais nova para a mais velha (o nome tem a data).</summary>
    public IReadOnlyList<string> Backups() => Directory.Exists(BackupDirectory)
        ? Directory.GetFiles(BackupDirectory, "layout-*.json").OrderDescending(StringComparer.Ordinal).ToList()
        : [];
}

