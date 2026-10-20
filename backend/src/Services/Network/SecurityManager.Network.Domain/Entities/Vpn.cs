using System.Text.RegularExpressions;
using SecurityManager.Network.Domain.Enums;
using SecurityManager.Shared.Kernel.Entities;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.Network.Domain.Entities;

/// <summary>
/// A VPN on the gateway: a WireGuard interface with peers, or an IPsec (IKEv2) site-to-site tunnel.
/// Private keys and pre-shared secrets are never stored here; generated configs read them from the device.
/// </summary>
public partial class VpnTunnel : GatewayChild
{
    public string Name { get; set; } = string.Empty;
    public VpnType Type { get; set; } = VpnType.WireGuard;

    // WireGuard
    public string? InterfaceName { get; set; } = "wg0";
    public int? ListenPort { get; set; } = 51820;
    /// <summary>Tunnel interface address, e.g. "10.200.0.1/24".</summary>
    public string? TunnelAddress { get; set; }
    /// <summary>The gateway's own public key (informational; the private key stays on the device).</summary>
    public string? PublicKey { get; set; }
    public int? Mtu { get; set; } = 1420;

    // IPsec site-to-site
    public string? RemoteGateway { get; set; }
    public string? LocalSubnets { get; set; }
    public string? RemoteSubnets { get; set; }
    public int IkeVersion { get; set; } = 2;
    public string? Proposals { get; set; } = "aes256gcm16-prfsha384-ecp384";

    public ICollection<VpnPeer> Peers { get; set; } = new List<VpnPeer>();

    public override IEnumerable<string> Validate()
    {
        if (!Regex.IsMatch(Name, "^[a-zA-Z0-9][a-zA-Z0-9_-]{1,39}$")) yield return "Name must be 2–40 letters, digits, '-' or '_'.";

        if (Type == VpnType.WireGuard)
        {
            if (!NetworkNames.IsInterfaceName(InterfaceName)) yield return "WireGuard needs an interface name such as wg0.";
            if (ListenPort is null or < 1 or > 65535) yield return "Listen port must be between 1 and 65535.";
            if (!Cidr.TryParse(TunnelAddress, out var tunnel)) yield return "Tunnel address must be in CIDR form, e.g. 10.200.0.1/24.";
            else if (tunnel.IsNetworkAddress) yield return "Tunnel address must be a host address, not the network address.";
            if (!string.IsNullOrWhiteSpace(PublicKey) && !NetworkNames.IsWireGuardKey(PublicKey)) yield return "Public key must be a 44-character base64 WireGuard key.";
            if (Mtu is not null and (< 1280 or > 9000)) yield return "WireGuard MTU must be between 1280 and 9000.";
        }
        else
        {
            if (!IpText.IsValid(RemoteGateway) && !NetworkNames.IsHostname(RemoteGateway)) yield return "Remote gateway must be an IP address or hostname.";
            if (!Cidr.TryParseList(LocalSubnets, out _)) yield return "Local subnets must be a comma-separated list of CIDRs.";
            if (!Cidr.TryParseList(RemoteSubnets, out _)) yield return "Remote subnets must be a comma-separated list of CIDRs.";
            if (IkeVersion is not (1 or 2)) yield return "IKE version must be 1 or 2.";
            if (string.IsNullOrWhiteSpace(Proposals) || !ProposalRegex().IsMatch(Proposals)) yield return "Proposals must look like aes256gcm16-prfsha384-ecp384 (comma-separated).";
        }
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*(,[a-z0-9]+(-[a-z0-9]+)*)*$")]
    private static partial Regex ProposalRegex();
}

public class VpnPeer : BaseEntity
{
    public Guid TunnelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    /// <summary>Optional "host:port" or "[ipv6]:port" for peers with a fixed address.</summary>
    public string? Endpoint { get; set; }
    /// <summary>Comma-separated CIDRs routed to this peer.</summary>
    public string AllowedIps { get; set; } = string.Empty;
    public int PersistentKeepalive { get; set; } = 25;
    public bool IsEnabled { get; set; } = true;

    public IEnumerable<string> Validate()
    {
        if (Name.Trim().Length is < 2 or > 100) yield return "Name must be 2–100 characters.";
        if (!NetworkNames.IsWireGuardKey(PublicKey)) yield return "Public key must be a 44-character base64 WireGuard key.";
        if (!Cidr.TryParseList(AllowedIps, out _)) yield return "Allowed IPs must be a comma-separated list of CIDRs.";
        if (!string.IsNullOrWhiteSpace(Endpoint) && !IsEndpoint(Endpoint)) yield return "Endpoint must be host:port (or [ipv6]:port).";
        if (PersistentKeepalive is < 0 or > 65535) yield return "Keepalive must be between 0 and 65535 seconds.";
    }

    private static bool IsEndpoint(string endpoint)
    {
        var at = endpoint.LastIndexOf(':');
        if (at <= 0 || !int.TryParse(endpoint[(at + 1)..], out var port) || port is < 1 or > 65535) return false;
        var host = endpoint[..at];
        if (host.StartsWith('[') && host.EndsWith(']')) return IpText.IsValid(host[1..^1]);
        return IpText.IsValid(host) || NetworkNames.IsHostname(host);
    }
}
