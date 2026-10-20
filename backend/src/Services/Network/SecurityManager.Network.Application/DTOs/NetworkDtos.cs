using System.ComponentModel.DataAnnotations;
using SecurityManager.Network.Domain.Enums;
using SecurityManager.Network.Domain.Services;

namespace SecurityManager.Network.Application.DTOs;

// ---------- Gateways ----------
public sealed record GatewayDto(
    Guid Id, string Name, string Hostname, string? Description, string? ManagementAddress,
    Guid? ClusterId, string? ClusterName, int HaPriority, string? HaRole,
    int InterfaceCount, int RouteCount, int NatRuleCount, int TunnelCount, int ErrorCount, int WarningCount,
    int ConfigRevision, int? DeployedRevision, DateTime? DeployedAt, string? DeployedBy, bool PendingChanges,
    DateTime CreatedAt, DateTime? UpdatedAt);

public sealed record GatewayDetailDto(
    GatewayDto Gateway,
    IReadOnlyList<InterfaceDto> Interfaces,
    IReadOnlyList<RouteDto> Routes,
    IReadOnlyList<RoutingPolicyDto> Policies,
    IReadOnlyList<NatRuleDto> NatRules,
    IReadOnlyList<TunnelDto> Tunnels,
    IReadOnlyList<ConfigIssue> Issues);

public sealed class UpsertGatewayRequest
{
    [Required, StringLength(100, MinimumLength = 2)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(253)] public string Hostname { get; set; } = string.Empty;
    [StringLength(500)] public string? Description { get; set; }
    [StringLength(45)] public string? ManagementAddress { get; set; }
    public Guid? ClusterId { get; set; }
    [Range(1, 254)] public int HaPriority { get; set; } = 100;
}

public sealed record GeneratedConfigDto(Guid GatewayId, string Hostname, int Revision, IReadOnlyList<GeneratedFile> Files, IReadOnlyList<ConfigIssue> Issues);

// ---------- Interfaces ----------
public sealed record InterfaceDto(
    Guid Id, string Name, InterfaceType Type, InterfaceZone Zone, AddressingMode Mode, string? Address, int? Mtu,
    int? VlanId, string? Parent, string? Members, bool IsEnabled, string? Description);

public sealed class UpsertInterfaceRequest
{
    [Required, StringLength(15)] public string Name { get; set; } = string.Empty;
    public InterfaceType Type { get; set; } = InterfaceType.Physical;
    public InterfaceZone Zone { get; set; } = InterfaceZone.Lan;
    public AddressingMode Mode { get; set; } = AddressingMode.Static;
    [StringLength(50)] public string? Address { get; set; }
    public int? Mtu { get; set; }
    public int? VlanId { get; set; }
    [StringLength(15)] public string? Parent { get; set; }
    [StringLength(200)] public string? Members { get; set; }
    public bool IsEnabled { get; set; } = true;
    [StringLength(500)] public string? Description { get; set; }
}

// ---------- Routing ----------
public sealed record RouteDto(Guid Id, string Destination, string? NextHop, string? Interface, int Metric, int Table, bool IsDefault, bool IsEnabled, string? Description);

public sealed class UpsertRouteRequest
{
    [Required, StringLength(50)] public string Destination { get; set; } = "0.0.0.0/0";
    [StringLength(45)] public string? NextHop { get; set; }
    [StringLength(15)] public string? Interface { get; set; }
    public int Metric { get; set; } = 100;
    public int Table { get; set; } = 254;
    public bool IsEnabled { get; set; } = true;
    [StringLength(500)] public string? Description { get; set; }
}

public sealed record RoutingPolicyDto(Guid Id, int Priority, string? Source, string? Destination, string? InInterface, int Table, bool IsEnabled, string? Description);

public sealed class UpsertRoutingPolicyRequest
{
    public int Priority { get; set; } = 1000;
    [StringLength(50)] public string? Source { get; set; }
    [StringLength(50)] public string? Destination { get; set; }
    [StringLength(15)] public string? InInterface { get; set; }
    public int Table { get; set; } = 100;
    public bool IsEnabled { get; set; } = true;
    [StringLength(500)] public string? Description { get; set; }
}

// ---------- NAT ----------
public sealed record NatRuleDto(
    Guid Id, string Name, NatType Type, NatProtocol Protocol, string? InInterface, string? OutInterface, string? Source,
    string? Destination, string? DestinationPort, string? TranslatedAddress, int? TranslatedPort, int Priority, bool IsEnabled, string? Description);

public sealed class UpsertNatRuleRequest
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    public NatType Type { get; set; } = NatType.Masquerade;
    public NatProtocol Protocol { get; set; } = NatProtocol.Any;
    [StringLength(15)] public string? InInterface { get; set; }
    [StringLength(15)] public string? OutInterface { get; set; }
    [StringLength(50)] public string? Source { get; set; }
    [StringLength(50)] public string? Destination { get; set; }
    [StringLength(20)] public string? DestinationPort { get; set; }
    [StringLength(45)] public string? TranslatedAddress { get; set; }
    public int? TranslatedPort { get; set; }
    public int Priority { get; set; } = 100;
    public bool IsEnabled { get; set; } = true;
    [StringLength(500)] public string? Description { get; set; }
}

// ---------- VPN ----------
public sealed record PeerDto(Guid Id, Guid TunnelId, string Name, string PublicKey, string? Endpoint, string AllowedIps, int PersistentKeepalive, bool IsEnabled);

public sealed record TunnelDto(
    Guid Id, string Name, VpnType Type, string? InterfaceName, int? ListenPort, string? TunnelAddress, string? PublicKey, int? Mtu,
    string? RemoteGateway, string? LocalSubnets, string? RemoteSubnets, int IkeVersion, string? Proposals,
    bool IsEnabled, string? Description, IReadOnlyList<PeerDto> Peers);

public sealed class UpsertTunnelRequest
{
    [Required, StringLength(40)] public string Name { get; set; } = string.Empty;
    public VpnType Type { get; set; } = VpnType.WireGuard;
    [StringLength(15)] public string? InterfaceName { get; set; } = "wg0";
    public int? ListenPort { get; set; } = 51820;
    [StringLength(50)] public string? TunnelAddress { get; set; }
    [StringLength(44)] public string? PublicKey { get; set; }
    public int? Mtu { get; set; } = 1420;
    [StringLength(253)] public string? RemoteGateway { get; set; }
    [StringLength(500)] public string? LocalSubnets { get; set; }
    [StringLength(500)] public string? RemoteSubnets { get; set; }
    public int IkeVersion { get; set; } = 2;
    [StringLength(200)] public string? Proposals { get; set; } = "aes256gcm16-prfsha384-ecp384";
    public bool IsEnabled { get; set; } = true;
    [StringLength(500)] public string? Description { get; set; }
}

public sealed class UpsertPeerRequest
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(44)] public string PublicKey { get; set; } = string.Empty;
    [StringLength(300)] public string? Endpoint { get; set; }
    [Required, StringLength(1000)] public string AllowedIps { get; set; } = string.Empty;
    public int PersistentKeepalive { get; set; } = 25;
    public bool IsEnabled { get; set; } = true;
}

// ---------- High availability ----------
public sealed record ClusterMemberDto(Guid GatewayId, string Name, string Hostname, int Priority, string Role, bool PendingChanges);

public sealed record VirtualIpDto(Guid Id, Guid ClusterId, string Interface, string Address, string? Description);

public sealed record ClusterDto(
    Guid Id, string Name, string? Description, int VrrpRouterId, int AdvertIntervalSeconds, bool Preempt,
    IReadOnlyList<ClusterMemberDto> Members, IReadOnlyList<VirtualIpDto> VirtualIps, DateTime CreatedAt);

public sealed class UpsertClusterRequest
{
    [Required, StringLength(100, MinimumLength = 2)] public string Name { get; set; } = string.Empty;
    [StringLength(500)] public string? Description { get; set; }
    [Range(1, 255)] public int VrrpRouterId { get; set; } = 51;
    [Range(1, 255)] public int AdvertIntervalSeconds { get; set; } = 1;
    public bool Preempt { get; set; } = true;
}

public sealed class UpsertVirtualIpRequest
{
    [Required, StringLength(15)] public string Interface { get; set; } = string.Empty;
    [Required, StringLength(50)] public string Address { get; set; } = string.Empty;
    [StringLength(500)] public string? Description { get; set; }
}
