using SecurityManager.Network.Application.Abstractions;
using SecurityManager.Network.Application.DTOs;
using SecurityManager.Network.Application.Mapping;
using SecurityManager.Network.Domain.Entities;
using SecurityManager.Network.Domain.Enums;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Network.Application.Services;

// ---------------------------------------------------------------------------------------------- WireGuard peers
public interface IPeerService
{
    Task<PeerDto> CreateAsync(Guid gatewayId, Guid tunnelId, UpsertPeerRequest request, CancellationToken ct = default);
    Task<PeerDto> UpdateAsync(Guid gatewayId, Guid tunnelId, Guid id, UpsertPeerRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid gatewayId, Guid tunnelId, Guid id, CancellationToken ct = default);
}

public sealed class PeerService(INetworkUnitOfWork uow) : IPeerService
{
    public async Task<PeerDto> CreateAsync(Guid gatewayId, Guid tunnelId, UpsertPeerRequest r, CancellationToken ct = default)
    {
        var (gateway, _) = await LoadAsync(gatewayId, tunnelId, ct);
        var peer = new VpnPeer { TunnelId = tunnelId };
        await ApplyAsync(peer, r, ct);
        await uow.Peers.AddAsync(peer, ct);
        gateway.Touch();
        await uow.SaveChangesAsync(ct);
        return peer.ToDto();
    }

    public async Task<PeerDto> UpdateAsync(Guid gatewayId, Guid tunnelId, Guid id, UpsertPeerRequest r, CancellationToken ct = default)
    {
        var (gateway, _) = await LoadAsync(gatewayId, tunnelId, ct);
        var peer = await uow.Peers.FirstOrDefaultAsync(p => p.Id == id && p.TunnelId == tunnelId, ct: ct) ?? throw new NotFoundException("Peer", id);
        await ApplyAsync(peer, r, ct);
        gateway.Touch();
        await uow.SaveChangesAsync(ct);
        return peer.ToDto();
    }

    public async Task DeleteAsync(Guid gatewayId, Guid tunnelId, Guid id, CancellationToken ct = default)
    {
        var (gateway, _) = await LoadAsync(gatewayId, tunnelId, ct);
        var peer = await uow.Peers.FirstOrDefaultAsync(p => p.Id == id && p.TunnelId == tunnelId, ct: ct) ?? throw new NotFoundException("Peer", id);
        uow.Peers.Remove(peer);
        gateway.Touch();
        await uow.SaveChangesAsync(ct);
    }

    private async Task<(Gateway, VpnTunnel)> LoadAsync(Guid gatewayId, Guid tunnelId, CancellationToken ct)
    {
        var gateway = await uow.Gateways.GetByIdAsync(gatewayId, ct: ct) ?? throw new NotFoundException("Gateway", gatewayId);
        var tunnel = await uow.Tunnels.FirstOrDefaultAsync(t => t.Id == tunnelId && t.GatewayId == gatewayId, ct: ct)
                     ?? throw new NotFoundException("VPN tunnel", tunnelId);
        if (tunnel.Type != VpnType.WireGuard) throw new BusinessRuleException("Peers can only be added to WireGuard tunnels.");
        return (gateway, tunnel);
    }

    private async Task ApplyAsync(VpnPeer p, UpsertPeerRequest r, CancellationToken ct)
    {
        p.Name = r.Name.Trim();
        p.PublicKey = r.PublicKey.Trim();
        p.Endpoint = string.IsNullOrWhiteSpace(r.Endpoint) ? null : r.Endpoint.Trim();
        p.AllowedIps = string.Join(", ", r.AllowedIps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        p.PersistentKeepalive = r.PersistentKeepalive;
        p.IsEnabled = r.IsEnabled;

        var errors = p.Validate().ToList();
        if (errors.Count > 0) throw new BusinessRuleException(string.Join(" ", errors));

        var key = p.PublicKey;
        var tunnelId = p.TunnelId;
        var id = p.Id;
        if (await uow.Peers.AnyAsync(x => x.TunnelId == tunnelId && x.PublicKey == key && x.Id != id, ct))
            throw new ConflictException("Another peer on this tunnel already uses that public key.");
    }
}

// ---------------------------------------------------------------------------------------------- HA clusters
public interface IClusterService
{
    Task<IReadOnlyList<ClusterDto>> GetAllAsync(CancellationToken ct = default);
    Task<ClusterDto> CreateAsync(UpsertClusterRequest request, CancellationToken ct = default);
    Task<ClusterDto> UpdateAsync(Guid id, UpsertClusterRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<VirtualIpDto> AddVirtualIpAsync(Guid clusterId, UpsertVirtualIpRequest request, CancellationToken ct = default);
    Task<VirtualIpDto> UpdateVirtualIpAsync(Guid clusterId, Guid id, UpsertVirtualIpRequest request, CancellationToken ct = default);
    Task DeleteVirtualIpAsync(Guid clusterId, Guid id, CancellationToken ct = default);
}

public sealed class ClusterService(INetworkUnitOfWork uow) : IClusterService
{
    public async Task<IReadOnlyList<ClusterDto>> GetAllAsync(CancellationToken ct = default)
    {
        var clusters = await uow.Clusters.ListAsync(orderBy: o => o.OrderBy(c => c.Name), includes: [c => c.VirtualIps, c => c.Members], ct: ct);
        return clusters.Select(ToDto).ToList();
    }

    public async Task<ClusterDto> CreateAsync(UpsertClusterRequest r, CancellationToken ct = default)
    {
        var cluster = new HaCluster();
        await ApplyAsync(cluster, r, ct);
        await uow.Clusters.AddAsync(cluster, ct);
        await uow.SaveChangesAsync(ct);
        return ToDto(cluster);
    }

    public async Task<ClusterDto> UpdateAsync(Guid id, UpsertClusterRequest r, CancellationToken ct = default)
    {
        var cluster = await LoadAsync(id, ct);
        await ApplyAsync(cluster, r, ct);
        TouchMembers(cluster);
        await uow.SaveChangesAsync(ct);
        return ToDto(cluster);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var cluster = await LoadAsync(id, ct);
        foreach (var member in cluster.Members)
        {
            member.ClusterId = null; // leave the gateways in place, just un-clustered
            member.Touch();
        }
        uow.VirtualIps.RemoveRange(cluster.VirtualIps);
        uow.Clusters.Remove(cluster);
        await uow.SaveChangesAsync(ct);
    }

    public async Task<VirtualIpDto> AddVirtualIpAsync(Guid clusterId, UpsertVirtualIpRequest r, CancellationToken ct = default)
    {
        var cluster = await LoadAsync(clusterId, ct);
        var vip = new VirtualIp { ClusterId = clusterId };
        Apply(vip, r, cluster);
        await uow.VirtualIps.AddAsync(vip, ct);
        TouchMembers(cluster);
        await uow.SaveChangesAsync(ct);
        return vip.ToDto();
    }

    public async Task<VirtualIpDto> UpdateVirtualIpAsync(Guid clusterId, Guid id, UpsertVirtualIpRequest r, CancellationToken ct = default)
    {
        var cluster = await LoadAsync(clusterId, ct);
        var vip = cluster.VirtualIps.FirstOrDefault(v => v.Id == id) ?? throw new NotFoundException("Virtual IP", id);
        Apply(vip, r, cluster);
        TouchMembers(cluster);
        await uow.SaveChangesAsync(ct);
        return vip.ToDto();
    }

    public async Task DeleteVirtualIpAsync(Guid clusterId, Guid id, CancellationToken ct = default)
    {
        var cluster = await LoadAsync(clusterId, ct);
        var vip = cluster.VirtualIps.FirstOrDefault(v => v.Id == id) ?? throw new NotFoundException("Virtual IP", id);
        uow.VirtualIps.Remove(vip);
        TouchMembers(cluster);
        await uow.SaveChangesAsync(ct);
    }

    private async Task<HaCluster> LoadAsync(Guid id, CancellationToken ct) =>
        await uow.Clusters.GetByIdAsync(id, [c => c.VirtualIps, c => c.Members], ct) ?? throw new NotFoundException("HA cluster", id);

    private async Task ApplyAsync(HaCluster c, UpsertClusterRequest r, CancellationToken ct)
    {
        c.Name = r.Name.Trim();
        c.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        c.VrrpRouterId = r.VrrpRouterId;
        c.AdvertIntervalSeconds = r.AdvertIntervalSeconds;
        c.Preempt = r.Preempt;
        var errors = c.Validate().ToList();
        if (errors.Count > 0) throw new BusinessRuleException(string.Join(" ", errors));

        var name = c.Name;
        var id = c.Id;
        if (await uow.Clusters.AnyAsync(x => x.Name == name && x.Id != id, ct))
            throw new ConflictException($"A cluster named '{name}' already exists.");
    }

    private static void Apply(VirtualIp v, UpsertVirtualIpRequest r, HaCluster cluster)
    {
        v.Interface = r.Interface.Trim();
        v.Address = r.Address.Trim();
        v.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        var errors = v.Validate().ToList();
        if (errors.Count > 0) throw new BusinessRuleException(string.Join(" ", errors));
        if (cluster.VirtualIps.Any(x => x.Id != v.Id && x.Address.Split('/')[0] == v.Address.Split('/')[0]))
            throw new ConflictException($"Virtual IP {v.Address} already exists in this cluster.");
    }

    /// <summary>Any cluster change alters every member's keepalived configuration.</summary>
    private static void TouchMembers(HaCluster cluster)
    {
        foreach (var member in cluster.Members) member.Touch();
    }

    private static ClusterDto ToDto(HaCluster c)
    {
        var master = HaCluster.ElectMaster(c.Members);
        return new ClusterDto(c.Id, c.Name, c.Description, c.VrrpRouterId, c.AdvertIntervalSeconds, c.Preempt,
            c.Members.OrderByDescending(m => m.HaPriority).ThenBy(m => m.Name)
                .Select(m => new ClusterMemberDto(m.Id, m.Name, m.Hostname, m.HaPriority, m.Id == master?.Id ? "Master" : "Backup", m.HasPendingChanges))
                .ToList(),
            c.VirtualIps.OrderBy(v => v.Interface).ThenBy(v => v.Address).Select(v => v.ToDto()).ToList(),
            c.CreatedAt);
    }
}
