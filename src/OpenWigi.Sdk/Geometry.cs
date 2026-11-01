namespace OpenWigi.Sdk;

/// <summary>Tamanho em pixels.</summary>
public readonly record struct PixelSize(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>Retângulo em pixels, relativo ao canto superior esquerdo do widget.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromSize(PixelSize size) => new(0, 0, size.Width, size.Height);

    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;

    public bool Contains(PixelRect other) =>
        other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;

    /// <summary>Menor retângulo que cobre os dois (vazio não conta).</summary>
    public PixelRect Union(PixelRect other)
    {
        if (IsEmpty)
            return other;
        if (other.IsEmpty)
            return this;
        var x = Math.Min(X, other.X);
        var y = Math.Min(Y, other.Y);
        return new PixelRect(x, y, Math.Max(Right, other.Right) - x, Math.Max(Bottom, other.Bottom) - y);
    }
}

/// <summary>
/// A grade da tela: 10 colunas por 8 linhas de meias células, com 4 px entre elas, fechando os 1016x592
/// do WigiDash. As colunas têm 98 px. As linhas alternam 70 px (pares) e 71 px (ímpares), então um widget
/// com número par de linhas tem sempre a mesma altura em qualquer posição, e 2x2 meias células dão os
/// mesmos 200x145 da grade 5x4 antiga.
/// </summary>
/// <remarks>
/// Os valores são <c>static readonly</c> de propósito: <c>const</c> seria copiado para dentro do plugin
/// na compilação e ficaria velho se a grade mudar.
/// </remarks>
public static class WidgetGrid
{
    public static readonly int Columns = 10;
    public static readonly int Rows = 8;

    /// <summary>Espaço fixo entre duas células vizinhas.</summary>
    public static readonly int Gap = 4;

    public static readonly int ColumnWidth = 98;

    /// <summary>Altura de um par de linhas mais o espaço depois dele (70 + 4 + 71 + 4).</summary>
    private const int RowPairPitch = 149;

    public static PixelSize Screen { get; } = new(1016, 592);

    /// <summary>X do começo da coluna, contando de 0.</summary>
    public static int ColumnX(int column) => column * (ColumnWidth + Gap);

    /// <summary>Y do começo da linha, contando de 0.</summary>
    public static int RowY(int row) => row / 2 * RowPairPitch + row % 2 * 74;

    /// <summary>Altura da linha: 70 nas pares, 71 nas ímpares.</summary>
    public static int RowHeight(int row) => row % 2 == 0 ? 70 : 71;

    /// <summary>Retângulo na tela ocupado por um widget de <paramref name="size"/> com canto em (coluna, linha).</summary>
    public static PixelRect CellBounds(int column, int row, WidgetSize size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(column + size.Columns, Columns, nameof(column));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(row + size.Rows, Rows, nameof(row));
        var x = ColumnX(column);
        var y = RowY(row);
        var last = row + size.Rows - 1;
        return new PixelRect(x, y,
            ColumnX(column + size.Columns - 1) + ColumnWidth - x,
            RowY(last) + RowHeight(last) - y);
    }
}

/// <summary>Tamanho de um widget em meias células da <see cref="WidgetGrid"/> (1x1 até 10x8).</summary>
public readonly record struct WidgetSize
{
    public WidgetSize(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(columns, WidgetGrid.Columns);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rows, WidgetGrid.Rows);
        Columns = columns;
        Rows = rows;
    }

    public int Columns { get; }
    public int Rows { get; }

    /// <summary>
    /// Pixels que as células cobrem começando numa linha par, contando o espaço entre elas. Com número
    /// ímpar de linhas, começar numa linha ímpar dá 1 px a mais de altura. Não é o tamanho do quadro:
    /// desenhe em <see cref="IWidgetContext.PixelSize"/>, que já desconta a moldura da página.
    /// </summary>
    public PixelSize CellSpan
    {
        get
        {
            var bounds = WidgetGrid.CellBounds(0, 0, this);
            return new PixelSize(bounds.Width, bounds.Height);
        }
    }

    /// <summary>Botão pequeno: uma meia célula (98x70).</summary>
    public static WidgetSize Mini { get; } = new(1, 1);
    /// <summary>O tile 1x1 da grade antiga (200x145).</summary>
    public static WidgetSize Small { get; } = new(2, 2);
    public static WidgetSize Wide { get; } = new(4, 2);
    public static WidgetSize Large { get; } = new(4, 4);
    public static WidgetSize FullScreen { get; } = new(WidgetGrid.Columns, WidgetGrid.Rows);

    public override string ToString() => $"{Columns}x{Rows}";

    /// <summary>Lê "4x2" (o mesmo formato do <see cref="ToString"/>).</summary>
    public static WidgetSize Parse(string text)
    {
        var parts = text.Split('x', 'X');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var columns) || !int.TryParse(parts[1], out var rows))
            throw new FormatException($"Tamanho de widget inválido: '{text}'. Use colunas x linhas, ex.: 4x2.");
        return new WidgetSize(columns, rows);
    }
}

/// <summary>
/// Moldura dos tiles de uma página, escolhida pela pessoa no manager entre valores fixos. O host pinta
/// a moldura; o plugin só desenha a área de conteúdo (<see cref="IWidgetContext.PixelSize"/>), que é a
/// célula menos <see cref="Inset"/> de cada lado. Tela cheia não tem moldura.
/// </summary>
public sealed record TileFrame
{
    /// <summary>Respiro em volta de cada tile, além dos 4 px fixos da grade.</summary>
    public static IReadOnlyList<int> SpacingOptions { get; } = [0, 1, 2, 3];

    /// <summary>Larguras de borda possíveis.</summary>
    public static IReadOnlyList<int> BorderOptions { get; } = [0, 1, 2];

    /// <summary>Raio do canto de fora quando <see cref="Rounded"/>; o host reduz em tiles pequenos.</summary>
    public static readonly int CornerRadius = 12;

    /// <summary>Todas as folgas (<see cref="Inset"/>) que um widget pode receber, para testar o desenho.</summary>
    public static IReadOnlyList<int> InsetOptions { get; } =
        SpacingOptions.SelectMany(s => BorderOptions, (s, b) => s + b).Distinct().Order().ToArray();

    private readonly int _spacing;
    private readonly int _border;

    public int Spacing
    {
        get => _spacing;
        init => _spacing = SpacingOptions.Contains(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Spacing), value, "Use um valor de TileFrame.SpacingOptions.");
    }

    public int Border
    {
        get => _border;
        init => _border = BorderOptions.Contains(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Border), value, "Use um valor de TileFrame.BorderOptions.");
    }

    public bool Rounded { get; init; }
    public WidgetColor BorderColor { get; init; } = new(74, 74, 85);

    /// <summary>Cor do fundo da página: o que aparece entre os tiles e nos cantos arredondados.</summary>
    public WidgetColor Background { get; init; } = new(40, 36, 58);

    /// <summary>Quanto a área de conteúdo encolhe de cada lado.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Inset => Spacing + Border;

    /// <summary>Área de conteúdo de uma célula de <paramref name="cell"/> pixels.</summary>
    public PixelSize ContentSize(PixelSize cell) => new(cell.Width - 2 * Inset, cell.Height - 2 * Inset);

    /// <summary>Área de conteúdo, na tela, de uma célula em <paramref name="cell"/>.</summary>
    public PixelRect ContentBounds(PixelRect cell) =>
        new(cell.X + Inset, cell.Y + Inset, cell.Width - 2 * Inset, cell.Height - 2 * Inset);

    /// <summary>Raio do canto de fora para um tile (célula menos o respiro) de <paramref name="tile"/> pixels.</summary>
    public int OuterRadius(PixelSize tile) =>
        Rounded ? Math.Min(CornerRadius, Math.Min(tile.Width, tile.Height) / 4) : 0;

    /// <summary>Padrão das páginas: 3 px de respiro, borda de 2 px, cantos arredondados.</summary>
    public static TileFrame Default { get; } = new() { Spacing = 3, Border = 2, Rounded = true };

    /// <summary>Sem moldura: tiles colados na grade, fundo preto (o visual da grade antiga sem cantos).</summary>
    public static TileFrame None { get; } = new() { Spacing = 0, Border = 0, Rounded = false, Background = new(0, 0, 0) };
}
