using System.Globalization;

namespace OpenWigi.Plugins;

/// <summary>
/// Versão SemVer 2.0 como o nuget.org usa (até 4 números, pré-release e metadados). Compara pela regra do
/// SemVer: pré-release vem antes da final; identificadores numéricos comparam como número.
/// </summary>
public sealed class PackageVersion : IComparable<PackageVersion>, IEquatable<PackageVersion>
{
    private readonly int[] _numbers;
    private readonly string[] _release;

    private PackageVersion(int[] numbers, string[] release, string original)
    {
        _numbers = numbers;
        _release = release;
        Original = original;
    }

    public int Major => _numbers[0];
    public int Minor => _numbers[1];
    public bool IsPrerelease => _release.Length > 0;
    public string Original { get; }

    public static PackageVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"Versão inválida: {text}");

    public static bool TryParse(string? text, out PackageVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var trimmed = text.Trim();
        var core = trimmed;
        var plus = core.IndexOf('+');
        if (plus >= 0)
            core = core[..plus];
        string[] release = [];
        var dash = core.IndexOf('-');
        if (dash >= 0)
        {
            release = core[(dash + 1)..].Split('.');
            core = core[..dash];
            if (release.Any(string.IsNullOrEmpty))
                return false;
        }
        var parts = core.Split('.');
        if (parts.Length is < 1 or > 4)
            return false;
        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        version = new PackageVersion(numbers, release, trimmed);
        return true;
    }

    /// <summary>Limite inferior de uma faixa do NuGet (<c>[2.0.0-preview.3, )</c>, <c>2.0.0</c>); null se não houver.</summary>
    public static PackageVersion? LowerBound(string? range)
    {
        if (string.IsNullOrWhiteSpace(range))
            return null;
        var text = range.Trim().TrimStart('[', '(').TrimEnd(']', ')');
        var lower = text.Split(',')[0].Trim();
        return TryParse(lower, out var version) ? version : null;
    }

    public int CompareTo(PackageVersion? other)
    {
        if (other is null)
            return 1;
        for (var i = 0; i < 4; i++)
        {
            var c = _numbers[i].CompareTo(other._numbers[i]);
            if (c != 0)
                return c;
        }
        if (_release.Length == 0 || other._release.Length == 0)
            return other._release.Length.CompareTo(_release.Length);
        for (var i = 0; i < Math.Min(_release.Length, other._release.Length); i++)
        {
            var c = CompareIdentifier(_release[i], other._release[i]);
            if (c != 0)
                return c;
        }
        return _release.Length.CompareTo(other._release.Length);
    }

    private static int CompareIdentifier(string a, string b)
    {
        var aNumber = int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var x);
        var bNumber = int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var y);
        return (aNumber, bNumber) switch
        {
            (true, true) => x.CompareTo(y),
            (true, false) => -1,
            (false, true) => 1,
            _ => string.Compare(a, b, StringComparison.OrdinalIgnoreCase),
        };
    }

    public bool Equals(PackageVersion? other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is PackageVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_numbers[0], _numbers[1], _numbers[2], _numbers[3], string.Join('.', _release).ToLowerInvariant());
    public override string ToString() => Original;

    public static bool operator >(PackageVersion a, PackageVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(PackageVersion a, PackageVersion b) => a.CompareTo(b) < 0;
}
