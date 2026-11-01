namespace OpenWigi.Plugins;

/// <summary>
/// Versão exata (<c>1.0.0</c>) ou faixa no formato do NuGet (<c>[1.0.0, 1.2.0)</c>, <c>(, 2.0.0]</c>, <c>[1.0.0]</c>).
/// Diferente do NuGet, uma versão solta é exata, não "mínima": na lista de bloqueio é o que se espera.
/// </summary>
public sealed class VersionRange
{
    private readonly PackageVersion? _min;
    private readonly PackageVersion? _max;
    private readonly bool _minInclusive;
    private readonly bool _maxInclusive;

    private VersionRange(PackageVersion? min, bool minInclusive, PackageVersion? max, bool maxInclusive)
    {
        _min = min;
        _minInclusive = minInclusive;
        _max = max;
        _maxInclusive = maxInclusive;
    }

    public static bool TryParse(string? text, out VersionRange range)
    {
        range = null!;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var t = text.Trim();
        if (t[0] is not ('[' or '('))
        {
            if (!PackageVersion.TryParse(t, out var exact))
                return false;
            range = new VersionRange(exact, true, exact, true);
            return true;
        }
        if (t.Length < 3 || t[^1] is not (']' or ')'))
            return false;
        var minInclusive = t[0] == '[';
        var maxInclusive = t[^1] == ']';
        var inner = t[1..^1];
        var comma = inner.IndexOf(',');
        if (comma < 0)
        {
            // [1.0.0] = exata; (1.0.0) não faz sentido.
            if (!minInclusive || !maxInclusive || !PackageVersion.TryParse(inner, out var only))
                return false;
            range = new VersionRange(only, true, only, true);
            return true;
        }
        var minText = inner[..comma].Trim();
        var maxText = inner[(comma + 1)..].Trim();
        PackageVersion? min = null, max = null;
        if (minText.Length > 0 && !PackageVersion.TryParse(minText, out min))
            return false;
        if (maxText.Length > 0 && !PackageVersion.TryParse(maxText, out max))
            return false;
        if (min is null && max is null)
            return false;
        range = new VersionRange(min, minInclusive && min is not null, max, maxInclusive && max is not null);
        return true;
    }

    public bool Contains(PackageVersion version)
    {
        if (_min is not null)
        {
            var c = version.CompareTo(_min);
            if (c < 0 || (c == 0 && !_minInclusive))
                return false;
        }
        if (_max is not null)
        {
            var c = version.CompareTo(_max);
            if (c > 0 || (c == 0 && !_maxInclusive))
                return false;
        }
        return true;
    }
}
