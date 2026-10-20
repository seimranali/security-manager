using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Firewall.Domain.ValueObjects;
using SecurityManager.Shared.Kernel.Entities;

namespace SecurityManager.Firewall.Domain.Entities;

/// <summary>
/// A named, prioritised group of rules. Policies are evaluated in ascending priority;
/// if no rule in any applicable policy matches, the default action of the highest-priority
/// applicable policy is used.
/// </summary>
public class FirewallPolicy : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public TrafficDirection Direction { get; set; } = TrafficDirection.Any;
    public RuleAction DefaultAction { get; set; } = RuleAction.Deny;

    /// <summary>Opt-in: push this policy's rules to host firewalls via enforcement agents.</summary>
    public bool EnforceOnHosts { get; set; }

    public ICollection<FirewallRule> Rules { get; set; } = new List<FirewallRule>();

    public bool AppliesTo(TrafficPacket packet) =>
        IsEnabled
        && (Direction == TrafficDirection.Any
            || packet.Direction == TrafficDirection.Any
            || Direction == packet.Direction);

    public FirewallRule? FindMatchingRule(TrafficPacket packet) =>
        Rules.Where(r => r.IsEnabled)
             .OrderBy(r => r.Priority)
             .ThenBy(r => r.CreatedAt)
             .FirstOrDefault(r => r.Matches(packet));
}
