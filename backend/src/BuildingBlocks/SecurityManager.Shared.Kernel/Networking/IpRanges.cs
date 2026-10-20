using System.Net;
using System.Net.Sockets;
using System.Numerics;

namespace SecurityManager.Shared.Kernel.Networking;

/// <summary>An inclusive address range within one IP family.</summary>
public readonly record struct IpRange(BigInteger Start, BigInteger End, bool IsV6)
{
    public bool Overlaps(IpRange other) => IsV6 == other.IsV6 && Start <= other.End && other.Start <= End;
}

/// <summary>
/// Set arithmetic on IP address specifications ("any", IPs, CIDR blocks, "a-b" ranges, comma lists).
/// Used to express "block X except allowlisted Y" for firewalls where block rules always win (e.g. Windows Firewall).
/// </summary>
public static class IpRanges
{
    private static readonly BigInteger MaxV4 = (BigInteger.One << 32) - 1;
    private static readonly BigInteger MaxV6 = (BigInteger.One << 128) - 1;

    public static readonly IReadOnlyList<IpRange> Everything = [new(0, MaxV4, false), new(0, MaxV6, true)];

    /// <summary>Parses a spec into normalised (sorted, merged) ranges. "any"/"*"/empty = everything.</summary>
    public static List<IpRange> Parse(string? spec)
    {
        if (PortSpec.IsAny(spec)) return [.. Everything];
        var ranges = new List<IpRange>();
        foreach (var token in spec!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseToken(token, out var r)) ranges.Add(r);
            else throw new FormatException($"'{token}' is not a valid IP address, CIDR block or range.");
        }
        return Normalize(ranges);
    }

    public static bool TryParseToken(string token, out IpRange range)
    {
        range = default;
        if (token.Contains('/'))
        {
            if (!Cidr.TryParse(token, out var cidr)) return false;
            var start = ToNumber(cidr.Network);
            var hostBits = cidr.MaxPrefix - cidr.Prefix;
            range = new IpRange(start, start + (BigInteger.One << hostBits) - 1, !cidr.IsIPv4);
            return true;
        }
        if (token.Contains('-'))
        {
            var parts = token.Split('-', 2, StringSplitOptions.TrimEntries);
            if (!IpText.TryParse(parts[0], out var a) || !IpText.TryParse(parts[1], out var b) || a.AddressFamily != b.AddressFamily) return false;
            var (s, e) = (ToNumber(a), ToNumber(b));
            if (s > e) return false;
            range = new IpRange(s, e, a.AddressFamily == AddressFamily.InterNetworkV6);
            return true;
        }
        if (!IpText.TryParse(token, out var ip)) return false;
        var n = ToNumber(ip);
        range = new IpRange(n, n, ip.AddressFamily == AddressFamily.InterNetworkV6);
        return true;
    }

    public static List<IpRange> Normalize(IEnumerable<IpRange> ranges)
    {
        var result = new List<IpRange>();
        foreach (var r in ranges.OrderBy(r => r.IsV6).ThenBy(r => r.Start))
        {
            if (result.Count > 0 && result[^1].IsV6 == r.IsV6 && r.Start <= result[^1].End + 1)
            {
                var last = result[^1];
                result[^1] = last with { End = BigInteger.Max(last.End, r.End) };
            }
            else result.Add(r);
        }
        return result;
    }

    /// <summary>Ranges in <paramref name="from"/> that are not in <paramref name="remove"/>.</summary>
    public static List<IpRange> Subtract(IEnumerable<IpRange> from, IEnumerable<IpRange> remove)
    {
        var holes = Normalize(remove);
        var result = new List<IpRange>();
        foreach (var r in Normalize(from))
        {
            var pieces = new List<IpRange> { r };
            foreach (var h in holes.Where(h => h.IsV6 == r.IsV6))
            {
                var next = new List<IpRange>();
                foreach (var p in pieces)
                {
                    if (!p.Overlaps(h)) { next.Add(p); continue; }
                    if (h.Start > p.Start) next.Add(p with { End = h.Start - 1 });
                    if (h.End < p.End) next.Add(p with { Start = h.End + 1 });
                }
                pieces = next;
            }
            result.AddRange(pieces);
        }
        return Normalize(result);
    }

    public static bool Intersects(IEnumerable<IpRange> a, IEnumerable<IpRange> b) => a.Any(x => b.Any(x.Overlaps));

    public static bool IsEverything(IReadOnlyList<IpRange> ranges) =>
        ranges.Count == 2 && Normalize(ranges).SequenceEqual(Everything);

    /// <summary>Firewall-friendly text: "*" for everything, otherwise IPs and "a-b" ranges.</summary>
    public static IReadOnlyList<string> ToText(IReadOnlyList<IpRange> ranges)
    {
        if (IsEverything(ranges)) return ["*"];
        return ranges.Select(r => r.Start == r.End ? ToAddress(r.Start, r.IsV6).ToString() : $"{ToAddress(r.Start, r.IsV6)}-{ToAddress(r.End, r.IsV6)}").ToList();
    }

    private static BigInteger ToNumber(IPAddress ip) => new(ip.GetAddressBytes(), isUnsigned: true, isBigEndian: true);

    private static IPAddress ToAddress(BigInteger value, bool v6)
    {
        var size = v6 ? 16 : 4;
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        var buffer = new byte[size];
        Array.Copy(bytes, 0, buffer, size - bytes.Length, bytes.Length);
        return new IPAddress(buffer);
    }
}
