using System.Net;
using System.Net.Sockets;

namespace SecurityManager.Shared.Kernel.Networking;

public static class IpClassifier
{
    private static readonly (byte[] Network, int Prefix)[] ReservedV4 =
    [
        ([0, 0, 0, 0], 8),        // "this" network
        ([10, 0, 0, 0], 8),       // private
        ([100, 64, 0, 0], 10),    // carrier-grade NAT
        ([127, 0, 0, 0], 8),      // loopback
        ([169, 254, 0, 0], 16),   // link-local
        ([172, 16, 0, 0], 12),    // private
        ([192, 168, 0, 0], 16),   // private
        ([224, 0, 0, 0], 4),      // multicast
        ([240, 0, 0, 0], 4),      // reserved / broadcast
    ];

    /// <summary>True for private, loopback, link-local, CGNAT, multicast and other non-public addresses.</summary>
    public static bool IsPrivateOrReserved(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address)) return true; // unknown → treat as unsafe to block
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return ReservedV4.Any(r => InPrefix(bytes, r.Network, r.Prefix));
        }

        return IPAddress.IsLoopback(address)
               || address.IsIPv6LinkLocal
               || address.IsIPv6SiteLocal
               || address.IsIPv6Multicast
               || address.IsIPv6UniqueLocal
               || address.Equals(IPAddress.IPv6None);
    }

    private static bool InPrefix(byte[] address, byte[] network, int prefix)
    {
        for (var i = 0; i < prefix / 8; i++)
            if (address[i] != network[i]) return false;
        var rem = prefix % 8;
        if (rem == 0) return true;
        var mask = (byte)(0xFF << (8 - rem));
        return (address[prefix / 8] & mask) == (network[prefix / 8] & mask);
    }
}
