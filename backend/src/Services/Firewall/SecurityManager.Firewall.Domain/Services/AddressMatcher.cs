using System.Net;
using System.Net.Sockets;

namespace SecurityManager.Firewall.Domain.Services;

/// <summary>
/// Matches IP addresses against address specifications:
/// "any", single IPs, CIDR blocks ("10.0.0.0/8", "fd00::/8"), ranges ("10.0.0.1-10.0.0.50")
/// and comma-separated combinations of these. Works for IPv4 and IPv6.
/// </summary>
public static class AddressMatcher
{
    public static bool IsAny(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return true;
        var s = spec.Trim();
        return s == "*" || s.Equals("any", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValid(string? spec) =>
        IsAny(spec) || Tokens(spec!).All(t => Evaluate(t, null) is not null);

    public static bool IsMatch(string? spec, string ip)
    {
        if (IsAny(spec)) return true;
        if (!IPAddress.TryParse(ip, out var address)) return false;
        address = Normalize(address);
        return Tokens(spec!).Any(t => Evaluate(t, address) == true);
    }

    /// <summary>
    /// Returns null when the token is invalid; otherwise whether <paramref name="address"/> matches
    /// (a null address only validates the token and returns false).
    /// </summary>
    private static bool? Evaluate(string token, IPAddress? address)
    {
        if (token.Contains('/'))
        {
            var parts = token.Split('/', 2, StringSplitOptions.TrimEntries);
            if (!IPAddress.TryParse(parts[0], out var network) || !int.TryParse(parts[1], out var prefix))
                return null;
            network = Normalize(network);
            var maxPrefix = network.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            if (prefix < 0 || prefix > maxPrefix) return null;
            return address is not null && InCidr(address, network, prefix);
        }

        if (token.Contains('-'))
        {
            var parts = token.Split('-', 2, StringSplitOptions.TrimEntries);
            if (!IPAddress.TryParse(parts[0], out var start) || !IPAddress.TryParse(parts[1], out var end))
                return null;
            start = Normalize(start);
            end = Normalize(end);
            if (start.AddressFamily != end.AddressFamily || Compare(start, end) > 0) return null;
            return address is not null
                   && address.AddressFamily == start.AddressFamily
                   && Compare(address, start) >= 0
                   && Compare(address, end) <= 0;
        }

        if (!IPAddress.TryParse(token, out var single)) return null;
        return address is not null && Normalize(single).Equals(address);
    }

    private static bool InCidr(IPAddress address, IPAddress network, int prefix)
    {
        var a = address.GetAddressBytes();
        var n = network.GetAddressBytes();
        if (a.Length != n.Length) return false;

        var fullBytes = prefix / 8;
        var remainingBits = prefix % 8;
        for (var i = 0; i < fullBytes; i++)
        {
            if (a[i] != n[i]) return false;
        }
        if (remainingBits == 0) return true;

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (a[fullBytes] & mask) == (n[fullBytes] & mask);
    }

    private static int Compare(IPAddress x, IPAddress y)
    {
        var a = x.GetAddressBytes();
        var b = y.GetAddressBytes();
        for (var i = 0; i < a.Length; i++)
        {
            var diff = a[i].CompareTo(b[i]);
            if (diff != 0) return diff;
        }
        return 0;
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static IEnumerable<string> Tokens(string spec) =>
        spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
