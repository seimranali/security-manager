using SecurityManager.Network.Domain.Entities;
using SecurityManager.Shared.Kernel.Abstractions;

namespace SecurityManager.Network.Application.Abstractions;

public interface INetworkUnitOfWork : IUnitOfWork
{
    IRepository<Gateway> Gateways { get; }
    IRepository<NetworkInterface> Interfaces { get; }
    IRepository<StaticRoute> Routes { get; }
    IRepository<RoutingPolicy> Policies { get; }
    IRepository<NatRule> NatRules { get; }
    IRepository<VpnTunnel> Tunnels { get; }
    IRepository<VpnPeer> Peers { get; }
    IRepository<HaCluster> Clusters { get; }
    IRepository<VirtualIp> VirtualIps { get; }
}
