using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Shared.Kernel.Abstractions;
using SecurityManager.Shared.Kernel.Contracts;

namespace SecurityManager.Firewall.Application.Abstractions;

/// <summary>Firewall-specific unit of work exposing strongly typed repositories.</summary>
public interface IFirewallUnitOfWork : IUnitOfWork
{
    IRepository<FirewallPolicy> Policies { get; }
    IRepository<FirewallRule> Rules { get; }
    IRepository<TrafficLog> TrafficLogs { get; }
    IRepository<IpListEntry> IpList { get; }
    IRepository<EnforcementSettings> EnforcementSettings { get; }
    IRepository<EnforcementAgent> EnforcementAgents { get; }
    IRepository<ThreatFeed> ThreatFeeds { get; }
}

/// <summary>Forwards firewall activity to the Security Service.</summary>
public interface ISecurityEventPublisher
{
    Task PublishAsync(SecurityEventMessage message, CancellationToken ct = default);
}
