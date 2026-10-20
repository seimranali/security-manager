using SecurityManager.Network.Application.Abstractions;
using SecurityManager.Network.Domain.Entities;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Network.Application.Services;

/// <summary>Loads gateways as complete aggregates (all configuration items, VPN peers and HA context).</summary>
public sealed class GatewayLoader(INetworkUnitOfWork uow)
{
    public async Task<Gateway> LoadAsync(Guid id, CancellationToken ct)
    {
        var gateway = await uow.Gateways.GetByIdAsync(id,
            [g => g.Interfaces, g => g.Routes, g => g.Policies, g => g.NatRules, g => g.Tunnels], ct)
            ?? throw new NotFoundException("Gateway", id);
        await LoadPeersAsync([gateway], ct);
        return gateway;
    }

    public async Task<IReadOnlyList<Gateway>> LoadManyAsync(Func<IQueryable<Gateway>, IOrderedQueryable<Gateway>>? order, Guid? clusterId, CancellationToken ct)
    {
        var gateways = await uow.Gateways.ListAsync(
            clusterId is null ? null : g => g.ClusterId == clusterId,
            order,
            includes: [g => g.Interfaces, g => g.Routes, g => g.Policies, g => g.NatRules, g => g.Tunnels],
            ct: ct);
        await LoadPeersAsync(gateways, ct);
        return gateways;
    }

    /// <summary>The gateway's cluster (with virtual IPs) and all of its members, or (null, [gateway]).</summary>
    public async Task<(HaCluster? Cluster, IReadOnlyList<Gateway> Members)> LoadHaContextAsync(Gateway gateway, CancellationToken ct)
    {
        if (gateway.ClusterId is not { } clusterId) return (null, [gateway]);
        var cluster = await uow.Clusters.GetByIdAsync(clusterId, [c => c.VirtualIps], ct);
        if (cluster is null) return (null, [gateway]);
        var members = await LoadManyAsync(null, clusterId, ct);
        return (cluster, members);
    }

    /// <summary>Peers are loaded separately; EF relationship fix-up attaches them to the tracked tunnels.</summary>
    private async Task LoadPeersAsync(IEnumerable<Gateway> gateways, CancellationToken ct)
    {
        var tunnelIds = gateways.SelectMany(g => g.Tunnels).Select(t => t.Id).ToList();
        if (tunnelIds.Count > 0) await uow.Peers.ListAsync(p => tunnelIds.Contains(p.TunnelId), ct: ct);
    }
}
