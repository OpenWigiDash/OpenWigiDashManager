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
/// A grade da tela: células de 200x145 com 4 px entre elas. 5x4 células fecham os 1016x592 do WigiDash.
/// </summary>
public static class WidgetGrid
{
    public const int CellWidth = 200;
    public const int CellHeight = 145;
    public const int Spacing = 4;
    public const int Columns = 5;
    public const int Rows = 4;

    public static PixelSize Screen { get; } = new WidgetSize(Columns, Rows).PixelSize;

    /// <summary>Canto superior esquerdo da célula (coluna, linha), contando de 0.</summary>
    public static (int X, int Y) CellOrigin(int column, int row) =>
        (column * (CellWidth + Spacing), row * (CellHeight + Spacing));
}

/// <summary>Tamanho de um widget em células da <see cref="WidgetGrid"/> (1x1 até 5x4).</summary>
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

    /// <summary>Pixels ocupados, contando o espaçamento entre as células cobertas.</summary>
    public PixelSize PixelSize => new(
        Columns * WidgetGrid.CellWidth + (Columns - 1) * WidgetGrid.Spacing,
        Rows * WidgetGrid.CellHeight + (Rows - 1) * WidgetGrid.Spacing);

    public static WidgetSize Small { get; } = new(1, 1);
    public static WidgetSize Wide { get; } = new(2, 1);
    public static WidgetSize Large { get; } = new(2, 2);
    public static WidgetSize FullScreen { get; } = new(WidgetGrid.Columns, WidgetGrid.Rows);

    public override string ToString() => $"{Columns}x{Rows}";

    /// <summary>Lê "2x1" (o mesmo formato do <see cref="ToString"/> e do manager antigo).</summary>
    public static WidgetSize Parse(string text)
    {
        var parts = text.Split('x', 'X');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var columns) || !int.TryParse(parts[1], out var rows))
            throw new FormatException($"Tamanho de widget inválido: '{text}'. Use colunas x linhas, ex.: 2x1.");
        return new WidgetSize(columns, rows);
    }
}
