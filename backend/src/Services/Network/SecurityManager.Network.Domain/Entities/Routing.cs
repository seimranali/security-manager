using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.Network.Domain.Entities;

/// <summary>A static route, optionally in a custom routing table (used with <see cref="RoutingPolicy"/>).</summary>
public class StaticRoute : GatewayChild
{
    /// <summary>Destination network, e.g. "0.0.0.0/0" (default) or "10.50.0.0/16".</summary>
    public string Destination { get; set; } = "0.0.0.0/0";
    public string? NextHop { get; set; }
    public string? Interface { get; set; }
    public int Metric { get; set; } = 100;
    /// <summary>Routing table id: 254 = main, 1–252 = custom (dedicated gateway / multi-WAN).</summary>
    public int Table { get; set; } = NetworkNames.MainTable;

    public bool IsDefault => Cidr.TryParse(Destination, out var c) && c.Prefix == 0;

    public override IEnumerable<string> Validate()
    {
        if (!Cidr.TryParse(Destination, out var dest)) yield return "Destination must be a network in CIDR form, e.g. 10.50.0.0/16 or 0.0.0.0/0.";
        if (string.IsNullOrWhiteSpace(NextHop) && string.IsNullOrWhiteSpace(Interface)) yield return "Specify a next hop, an interface, or both.";
        if (!string.IsNullOrWhiteSpace(NextHop))
        {
            if (!IpText.TryParse(NextHop, out var hop)) yield return "Next hop must be an IP address.";
            else if (Cidr.TryParse(Destination, out dest) && hop.AddressFamily != dest.Family) yield return "Next hop and destination must be the same IP version.";
        }
        if (!string.IsNullOrWhiteSpace(Interface) && !NetworkNames.IsInterfaceName(Interface)) yield return $"'{Interface}' is not a valid interface name.";
        if (Metric is < 0 or > 65535) yield return "Metric must be between 0 and 65535.";
        if (!NetworkNames.IsRoutingTable(Table)) yield return "Table must be 254 (main) or a custom table between 1 and 252.";
    }
}

/// <summary>
/// Policy-based routing rule ("ip rule"): traffic matching source/destination/input interface is routed using a
/// specific table — e.g. send the guest network out through a dedicated second WAN gateway.
/// </summary>
public class RoutingPolicy : GatewayChild
{
    /// <summary>Rule priority (lower first). 0, 32766 and 32767 are reserved by Linux.</summary>
    public int Priority { get; set; } = 1000;
    public string? Source { get; set; }
    public string? Destination { get; set; }
    public string? InInterface { get; set; }
    public int Table { get; set; } = 100;

    public override IEnumerable<string> Validate()
    {
        if (Priority is < 1 or > 32765) yield return "Priority must be between 1 and 32765.";
        if (string.IsNullOrWhiteSpace(Source) && string.IsNullOrWhiteSpace(Destination) && string.IsNullOrWhiteSpace(InInterface))
            yield return "Match on at least one of source, destination or input interface.";
        if (!string.IsNullOrWhiteSpace(Source) && !Cidr.TryParseHostOrNetwork(Source, out _)) yield return "Source must be an IP or CIDR.";
        if (!string.IsNullOrWhiteSpace(Destination) && !Cidr.TryParseHostOrNetwork(Destination, out _)) yield return "Destination must be an IP or CIDR.";
        if (!string.IsNullOrWhiteSpace(InInterface) && !NetworkNames.IsInterfaceName(InInterface)) yield return $"'{InInterface}' is not a valid interface name.";
        if (!NetworkNames.IsRoutingTable(Table)) yield return "Table must be 254 (main) or a custom table between 1 and 252.";
    }
}
