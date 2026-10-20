using SecurityManager.Firewall.Application.Abstractions;
using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Shared.Kernel.Abstractions;

namespace SecurityManager.Firewall.Infrastructure.Persistence;

public sealed class FirewallUnitOfWork(FirewallDbContext context)
    : UnitOfWork<FirewallDbContext>(context), IFirewallUnitOfWork
{
    public IRepository<FirewallPolicy> Policies => Repository<FirewallPolicy>();
    public IRepository<FirewallRule> Rules => Repository<FirewallRule>();
    public IRepository<TrafficLog> TrafficLogs => Repository<TrafficLog>();
    public IRepository<IpListEntry> IpList => Repository<IpListEntry>();
    public IRepository<EnforcementSettings> EnforcementSettings => Repository<EnforcementSettings>();
    public IRepository<EnforcementAgent> EnforcementAgents => Repository<EnforcementAgent>();
    public IRepository<ThreatFeed> ThreatFeeds => Repository<ThreatFeed>();
}
