namespace SecurityManager.Shared.Kernel.Networking;

/// <summary>
/// Parses port specifications such as "any", "443", "80,443" or "1000-2000,3389".
/// </summary>
public static class PortSpec
{
    public const int MinPort = 0;
    public const int MaxPort = 65535;

    public static bool IsAny(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return true;
        var s = spec.Trim();
        return s == "*" || s.Equals("any", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValid(string? spec) =>
        IsAny(spec) || Tokens(spec!).All(t => TryParseRange(t, out _, out _));

    public static bool IsMatch(string? spec, int? port)
    {
        if (IsAny(spec)) return true;
        if (port is null) return false;
        foreach (var token in Tokens(spec!))
        {
            if (TryParseRange(token, out var lo, out var hi) && port >= lo && port <= hi) return true;
        }
        return false;
    }

    /// <summary>Expands a spec into a distinct, ordered list of ports (capped at <paramref name="max"/>).</summary>
    public static IReadOnlyList<int> Expand(string? spec, int max = 5000)
    {
        if (IsAny(spec)) return [];
        var set = new SortedSet<int>();
        foreach (var token in Tokens(spec!))
        {
            if (!TryParseRange(token, out var lo, out var hi)) continue;
            for (var p = lo; p <= hi && set.Count < max; p++) set.Add(p);
            if (set.Count >= max) break;
        }
        return set.ToList();
    }

    public static bool TryParseRange(string token, out int lo, out int hi)
    {
        lo = hi = 0;
        var parts = token.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], out lo))
        {
            hi = lo;
        }
        else if (parts.Length != 2 || !int.TryParse(parts[0], out lo) || !int.TryParse(parts[1], out hi))
        {
            return false;
        }
        return lo >= MinPort && hi <= MaxPort && lo <= hi;
    }

    private static IEnumerable<string> Tokens(string spec) =>
        spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
