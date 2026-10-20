using SecurityManager.Network.Application.DTOs;
using SecurityManager.Network.Domain.Entities;
using SecurityManager.Network.Domain.Enums;
using SecurityManager.Network.Domain.Services;

namespace SecurityManager.Network.Application.Mapping;

internal static class NetworkMappings
{
    public static InterfaceDto ToDto(this NetworkInterface i) =>
        new(i.Id, i.Name, i.Type, i.Zone, i.Mode, i.Address, i.Mtu, i.VlanId, i.Parent, i.Members, i.IsEnabled, i.Description);

    public static RouteDto ToDto(this StaticRoute r) =>
        new(r.Id, r.Destination, r.NextHop, r.Interface, r.Metric, r.Table, r.IsDefault, r.IsEnabled, r.Description);

    public static RoutingPolicyDto ToDto(this RoutingPolicy p) =>
        new(p.Id, p.Priority, p.Source, p.Destination, p.InInterface, p.Table, p.IsEnabled, p.Description);

    public static NatRuleDto ToDto(this NatRule n) =>
        new(n.Id, n.Name, n.Type, n.Protocol, n.InInterface, n.OutInterface, n.Source, n.Destination, n.DestinationPort,
            n.TranslatedAddress, n.TranslatedPort, n.Priority, n.IsEnabled, n.Description);

    public static PeerDto ToDto(this VpnPeer p) =>
        new(p.Id, p.TunnelId, p.Name, p.PublicKey, p.Endpoint, p.AllowedIps, p.PersistentKeepalive, p.IsEnabled);

    public static TunnelDto ToDto(this VpnTunnel t) =>
        new(t.Id, t.Name, t.Type, t.InterfaceName, t.ListenPort, t.TunnelAddress, t.PublicKey, t.Mtu, t.RemoteGateway,
            t.LocalSubnets, t.RemoteSubnets, t.IkeVersion, t.Proposals, t.IsEnabled, t.Description,
            t.Peers.OrderBy(p => p.Name).Select(p => p.ToDto()).ToList());

    public static VirtualIpDto ToDto(this VirtualIp v) => new(v.Id, v.ClusterId, v.Interface, v.Address, v.Description);

    public static GatewayDto ToDto(this Gateway g, HaCluster? cluster, IReadOnlyList<Gateway> members, IReadOnlyList<ConfigIssue> issues)
    {
        string? role = null;
        if (cluster is not null) role = HaCluster.ElectMaster(members)?.Id == g.Id ? "Master" : "Backup";
        return new GatewayDto(
            g.Id, g.Name, g.Hostname, g.Description, g.ManagementAddress, g.ClusterId, cluster?.Name, g.HaPriority, role,
            g.Interfaces.Count, g.Routes.Count, g.NatRules.Count, g.Tunnels.Count,
            issues.Count(i => i.Severity == IssueSeverity.Error), issues.Count(i => i.Severity == IssueSeverity.Warning),
            g.ConfigRevision, g.DeployedRevision, g.DeployedAt, g.DeployedBy, g.HasPendingChanges, g.CreatedAt, g.UpdatedAt);
    }
}
