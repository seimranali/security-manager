using System.Net;
using System.Net.Sockets;

namespace SecurityManager.Shared.Kernel.Networking;

/// <summary>Strict IP parsing (rejects shorthand such as "10.1" that IPAddress.TryParse accepts).</summary>
public static class IpText
{
    public static bool TryParse(string? text, out IPAddress address)
    {
        address = IPAddress.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (!IPAddress.TryParse(s, out var parsed)) return false;
        if (parsed.AddressFamily == AddressFamily.InterNetwork && s.Count(c => c == '.') != 3) return false;
        address = parsed.IsIPv4MappedToIPv6 ? parsed.MapToIPv4() : parsed;
        return true;
    }

    public static bool IsValid(string? text) => TryParse(text, out _);

    /// <summary>"1.2.3.4" or "1.2.3.0/24".</summary>
    public static bool IsIpOrCidr(string? text) => IsValid(text) || Cidr.TryParse(text, out _);
}

/// <summary>An address with prefix length, e.g. "10.0.0.1/24" (interface address) or "10.0.0.0/24" (network).</summary>
public readonly record struct Cidr
{
    public IPAddress Address { get; }
    public int Prefix { get; }

    private Cidr(IPAddress address, int prefix)
    {
        Address = address;
        Prefix = prefix;
    }

    public AddressFamily Family => Address.AddressFamily;
    public bool IsIPv4 => Family == AddressFamily.InterNetwork;
    public int MaxPrefix => IsIPv4 ? 32 : 128;

    public static bool TryParse(string? text, out Cidr cidr)
    {
        cidr = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split('/');
        if (parts.Length != 2 || !IpText.TryParse(parts[0], out var ip) || !int.TryParse(parts[1], out var prefix)) return false;
        var max = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (prefix < 0 || prefix > max) return false;
        cidr = new Cidr(ip, prefix);
        return true;
    }

    /// <summary>Parses a CIDR, treating a bare IP as a host route (/32 or /128).</summary>
    public static bool TryParseHostOrNetwork(string? text, out Cidr cidr)
    {
        if (TryParse(text, out cidr)) return true;
        if (!IpText.TryParse(text, out var ip)) return false;
        cidr = new Cidr(ip, ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
        return true;
    }

    public static Cidr Parse(string text) =>
        TryParse(text, out var c) ? c : throw new FormatException($"'{text}' is not a valid CIDR.");

    /// <summary>Parses a comma-separated list; returns false if any item is invalid.</summary>
    public static bool TryParseList(string? text, out List<Cidr> list)
    {
        list = [];
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var item in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseHostOrNetwork(item, out var c)) return false;
            list.Add(c);
        }
        return list.Count > 0;
    }

    public IPAddress Network => new(Mask(Address.GetAddressBytes(), Prefix));
    public string NetworkText => $"{Network}/{Prefix}";

    /// <summary>True when the address is the network address itself (allowed for /31, /32 and IPv6 /127+).</summary>
    public bool IsNetworkAddress => Address.Equals(Network) && Prefix < MaxPrefix - 1;

    public bool Contains(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return ip.AddressFamily == Family
               && Mask(ip.GetAddressBytes(), Prefix).AsSpan().SequenceEqual(Mask(Address.GetAddressBytes(), Prefix));
    }

    public bool Overlaps(Cidr other)
    {
        if (other.Family != Family) return false;
        var p = Math.Min(Prefix, other.Prefix);
        return Mask(Address.GetAddressBytes(), p).AsSpan().SequenceEqual(Mask(other.Address.GetAddressBytes(), p));
    }

    public override string ToString() => $"{Address}/{Prefix}";

    private static byte[] Mask(byte[] bytes, int prefix)
    {
        var result = (byte[])bytes.Clone();
        for (var i = 0; i < result.Length; i++)
        {
            var bits = Math.Clamp(prefix - i * 8, 0, 8);
            result[i] &= bits == 0 ? (byte)0 : (byte)(0xFF << (8 - bits));
        }
        return result;
    }
}
