using SecurityManager.Network.Application.Abstractions;
using SecurityManager.Network.Domain.Entities;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Shared.Kernel.Abstractions;

namespace SecurityManager.Network.Infrastructure.Persistence;

public sealed class NetworkUnitOfWork(NetworkDbContext context)
    : UnitOfWork<NetworkDbContext>(context), INetworkUnitOfWork
{
    public IRepository<Gateway> Gateways => Repository<Gateway>();
    public IRepository<NetworkInterface> Interfaces => Repository<NetworkInterface>();
    public IRepository<StaticRoute> Routes => Repository<StaticRoute>();
    public IRepository<RoutingPolicy> Policies => Repository<RoutingPolicy>();
    public IRepository<NatRule> NatRules => Repository<NatRule>();
    public IRepository<VpnTunnel> Tunnels => Repository<VpnTunnel>();
    public IRepository<VpnPeer> Peers => Repository<VpnPeer>();
    public IRepository<HaCluster> Clusters => Repository<HaCluster>();
    public IRepository<VirtualIp> VirtualIps => Repository<VirtualIp>();
}
