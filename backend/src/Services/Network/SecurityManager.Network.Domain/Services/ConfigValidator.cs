using System.Net;
using SecurityManager.Network.Domain.Entities;
using SecurityManager.Network.Domain.Enums;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.Network.Domain.Services;

public sealed record ConfigIssue(IssueSeverity Severity, string Area, string Message, Guid? ItemId = null);

/// <summary>
/// Cross-item consistency checks for a gateway's whole configuration (single items validate themselves).
/// Errors would produce a broken or dangerous config; warnings are likely mistakes.
/// </summary>
public static class ConfigValidator
{
    public static List<ConfigIssue> Validate(Gateway g, HaCluster? cluster = null, IReadOnlyList<Gateway>? clusterMembers = null)
    {
        var issues = new List<ConfigIssue>();
        void Error(string area, string message, Guid? id = null) => issues.Add(new(IssueSeverity.Error, area, message, id));
        void Warn(string area, string message, Guid? id = null) => issues.Add(new(IssueSeverity.Warning, area, message, id));
        void Info(string area, string message, Guid? id = null) => issues.Add(new(IssueSeverity.Info, area, message, id));

        var interfaces = g.Interfaces.Where(i => i.IsEnabled).ToList();
        var names = interfaces.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
        var tunnels = g.Tunnels.Where(t => t.IsEnabled).ToList();
        var wgInterfaces = tunnels.Where(t => t.Type == VpnType.WireGuard && t.InterfaceName is not null)
            .Select(t => t.InterfaceName!).ToHashSet(StringComparer.Ordinal);
        bool Exists(string? name) => name is null || names.Contains(name) || wgInterfaces.Contains(name);

        // Connected networks: static interface subnets plus WireGuard tunnel subnets.
        var connected = interfaces
            .Where(i => i.AddressCidr is not null)
            .Select(i => (Name: i.Name, Cidr: i.AddressCidr!.Value, i.Id))
            .Concat(tunnels.Where(t => Cidr.TryParse(t.TunnelAddress, out _))
                .Select(t => (Name: t.InterfaceName ?? t.Name, Cidr: Cidr.Parse(t.TunnelAddress!), t.Id)))
            .ToList();
        bool IsConnected(IPAddress ip) => connected.Any(c => c.Cidr.Contains(ip));

        // ---------- Interfaces ----------
        foreach (var dup in g.Interfaces.GroupBy(i => i.Name).Where(x => x.Count() > 1))
            Error("Interfaces", $"Interface name '{dup.Key}' is used more than once.");

        for (var a = 0; a < connected.Count; a++)
        for (var b = a + 1; b < connected.Count; b++)
        {
            if (connected[a].Cidr.Overlaps(connected[b].Cidr))
                Error("Interfaces", $"{connected[a].Name} ({connected[a].Cidr.NetworkText}) overlaps {connected[b].Name} ({connected[b].Cidr.NetworkText}).", connected[b].Id);
        }

        foreach (var i in interfaces)
        {
            if (i.Type == InterfaceType.Vlan && i.Parent is not null && !names.Contains(i.Parent))
                Error("Interfaces", $"VLAN {i.Name} references missing parent '{i.Parent}'.", i.Id);
            foreach (var member in NetworkNames.SplitList(i.Members))
            {
                var m = interfaces.FirstOrDefault(x => x.Name == member);
                if (m is null) Error("Interfaces", $"{i.Type} {i.Name} references missing member '{member}'.", i.Id);
                else if (m.Mode == AddressingMode.Static) Warn("Interfaces", $"{member} is a member of {i.Name} but also has its own address; addresses belong on {i.Name}.", m.Id);
            }
        }
        if (!interfaces.Any(i => i.Zone == InterfaceZone.Wan)) Warn("Interfaces", "No WAN interface is defined.");

        // ---------- Routing ----------
        var routes = g.Routes.Where(r => r.IsEnabled).ToList();
        foreach (var r in routes)
        {
            if (!Exists(r.Interface)) Error("Routing", $"Route to {r.Destination} uses missing interface '{r.Interface}'.", r.Id);
            if (IpText.TryParse(r.NextHop, out var hop) && !IsConnected(hop))
                Warn("Routing", $"Next hop {r.NextHop} for {r.Destination} is not inside any connected network.", r.Id);
        }
        foreach (var dup in routes.GroupBy(r => (Dest: Cidr.TryParse(r.Destination, out var c) ? c.NetworkText : r.Destination, r.Table, r.Metric)).Where(x => x.Count() > 1))
            Error("Routing", $"Duplicate route to {dup.Key.Dest} (table {dup.Key.Table}, metric {dup.Key.Metric}).");

        var hasDhcpWan = interfaces.Any(i => i.Zone == InterfaceZone.Wan && i.Mode == AddressingMode.Dhcp);
        if (!routes.Any(r => r.IsDefault && r.Table == NetworkNames.MainTable))
        {
            if (hasDhcpWan) Info("Routing", "No static default route; the DHCP WAN interface is expected to provide one.");
            else Warn("Routing", "No default route in the main table; the gateway cannot reach the internet.");
        }

        var policies = g.Policies.Where(p => p.IsEnabled).ToList();
        foreach (var dup in policies.GroupBy(p => p.Priority).Where(x => x.Count() > 1))
            Error("Routing", $"Policy priority {dup.Key} is used more than once.");
        foreach (var p in policies)
        {
            if (!Exists(p.InInterface)) Error("Routing", $"Policy {p.Priority} uses missing interface '{p.InInterface}'.", p.Id);
            if (p.Table != NetworkNames.MainTable && !routes.Any(r => r.Table == p.Table))
                Error("Routing", $"Policy {p.Priority} sends traffic to table {p.Table}, which has no routes (traffic would be dropped).", p.Id);
        }
        foreach (var table in routes.Where(r => r.Table != NetworkNames.MainTable).Select(r => r.Table).Distinct())
        {
            if (!policies.Any(p => p.Table == table))
                Warn("Routing", $"Routes in table {table} are unused: no routing policy selects that table.");
        }

        // ---------- NAT ----------
        var nat = g.NatRules.Where(n => n.IsEnabled).ToList();
        foreach (var n in nat)
        {
            if (!Exists(n.InInterface)) Error("NAT", $"'{n.Name}' uses missing input interface '{n.InInterface}'.", n.Id);
            if (!Exists(n.OutInterface)) Error("NAT", $"'{n.Name}' uses missing output interface '{n.OutInterface}'.", n.Id);
            if (n.Type is NatType.PortForward or NatType.OneToOne && IpText.TryParse(n.TranslatedAddress, out var target) && !IsConnected(target))
                Warn("NAT", $"'{n.Name}' forwards to {n.TranslatedAddress}, which is not on a connected network.", n.Id);
            if (n.Type is NatType.Masquerade or NatType.SourceNat
                && interfaces.FirstOrDefault(i => i.Name == n.OutInterface) is { Zone: not InterfaceZone.Wan } outIf)
                Info("NAT", $"'{n.Name}' translates traffic leaving {outIf.Name}, which is not a WAN interface.", n.Id);
        }

        var forwards = nat.Where(n => n.Type == NatType.PortForward && n.UsesPorts).ToList();
        for (var a = 0; a < forwards.Count; a++)
        for (var b = a + 1; b < forwards.Count; b++)
        {
            var x = forwards[a];
            var y = forwards[b];
            if (x.Protocol == y.Protocol && x.InInterface == y.InInterface && SameDestination(x, y) && PortsOverlap(x.DestinationPort!, y.DestinationPort!))
                Error("NAT", $"Port forwards '{x.Name}' and '{y.Name}' claim the same {x.Protocol.ToString().ToUpperInvariant()} port(s) on {x.InInterface}.", y.Id);
        }

        // ---------- VPN ----------
        var wg = tunnels.Where(t => t.Type == VpnType.WireGuard).ToList();
        foreach (var dup in wg.GroupBy(t => t.ListenPort).Where(x => x.Count() > 1))
            Error("VPN", $"WireGuard listen port {dup.Key} is used by more than one tunnel.");
        foreach (var t in wg)
        {
            if (t.InterfaceName is not null && names.Contains(t.InterfaceName))
                Error("VPN", $"WireGuard interface '{t.InterfaceName}' clashes with an existing interface name.", t.Id);
            if (t.ListenPort is { } port && forwards.Any(f => f.Protocol == NatProtocol.Udp && PortsOverlap(f.DestinationPort!, port.ToString())))
                Error("VPN", $"Listen port UDP {port} of '{t.Name}' is also port-forwarded to another host.", t.Id);

            var peers = t.Peers.Where(p => p.IsEnabled).ToList();
            if (peers.Count == 0) Warn("VPN", $"WireGuard tunnel '{t.Name}' has no enabled peers.", t.Id);
            var allowed = peers.SelectMany(p => Cidr.TryParseList(p.AllowedIps, out var list) ? list.Select(c => (p.Name, c)) : []).ToList();
            for (var a = 0; a < allowed.Count; a++)
            for (var b = a + 1; b < allowed.Count; b++)
            {
                if (allowed[a].Name != allowed[b].Name && allowed[a].c.Overlaps(allowed[b].c))
                    Error("VPN", $"Peers '{allowed[a].Name}' and '{allowed[b].Name}' have overlapping allowed IPs ({allowed[a].c.NetworkText} / {allowed[b].c.NetworkText}).", t.Id);
            }
        }
        foreach (var t in tunnels.Where(t => t.Type == VpnType.IpsecSiteToSite))
        {
            if (Cidr.TryParseList(t.LocalSubnets, out var locals))
                foreach (var l in locals.Where(l => !connected.Any(c => c.Cidr.Overlaps(l))))
                    Warn("VPN", $"IPsec '{t.Name}' local subnet {l.NetworkText} is not a connected network on this gateway.", t.Id);
            if (Cidr.TryParseList(t.RemoteSubnets, out var remotes))
                foreach (var r in remotes.Where(r => connected.Any(c => c.Cidr.Overlaps(r))))
                    Error("VPN", $"IPsec '{t.Name}' remote subnet {r.NetworkText} overlaps a local network.", t.Id);
        }

        // ---------- High availability ----------
        if (cluster is not null)
        {
            var members = clusterMembers ?? [g];
            if (members.Count < 2) Warn("High availability", $"Cluster '{cluster.Name}' has only one member; there is nothing to fail over to.");
            foreach (var dup in members.GroupBy(m => m.HaPriority).Where(x => x.Count() > 1))
                Warn("High availability", $"Members {string.Join(", ", dup.Select(m => m.Name))} share priority {dup.Key}; mastership is decided by tie-break.");

            var vipInterfaces = cluster.VirtualIps.Select(v => v.Interface).Distinct().ToList();
            if (cluster.VrrpRouterId + vipInterfaces.Count - 1 > 255)
                Error("High availability", "VRRP router ids would exceed 255; lower the base router id.");

            foreach (var vip in cluster.VirtualIps)
            {
                var iface = interfaces.FirstOrDefault(i => i.Name == vip.Interface);
                if (iface?.AddressCidr is not { } ifCidr)
                {
                    Error("High availability", $"Virtual IP {vip.Address} needs a static address on {vip.Interface} on this gateway.", vip.Id);
                    continue;
                }
                if (!Cidr.TryParse(vip.Address, out var v)) continue;
                if (!ifCidr.Contains(v.Address)) Error("High availability", $"Virtual IP {vip.Address} is outside {vip.Interface}'s subnet {ifCidr.NetworkText}.", vip.Id);
                if (ifCidr.Address.Equals(v.Address)) Error("High availability", $"Virtual IP {vip.Address} equals {vip.Interface}'s own address.", vip.Id);
                if (members.SelectMany(m => m.Interfaces).Any(i => i.AddressCidr is { } c && c.Address.Equals(v.Address)))
                    Error("High availability", $"Virtual IP {v.Address} is also assigned as a member interface address.", vip.Id);
            }
        }

        return issues;
    }

    private static bool SameDestination(NatRule a, NatRule b) =>
        string.IsNullOrWhiteSpace(a.Destination) || string.IsNullOrWhiteSpace(b.Destination)
        || (Cidr.TryParseHostOrNetwork(a.Destination, out var x) && Cidr.TryParseHostOrNetwork(b.Destination, out var y) && x.Overlaps(y));

    private static bool PortsOverlap(string a, string b) =>
        PortSpec.TryParseRange(a.Trim(), out var aLo, out var aHi)
        && PortSpec.TryParseRange(b.Trim(), out var bLo, out var bHi)
        && aLo <= bHi && bLo <= aHi;
}
