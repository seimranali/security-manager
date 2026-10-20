using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Firewall.Domain.ValueObjects;

namespace SecurityManager.Firewall.Domain.Services;

public sealed record FirewallDecision(
    RuleAction Action, FirewallPolicy? Policy, FirewallRule? Rule, string Reason, IpListEntry? ListEntry = null);

/// <summary>
/// Pure decision logic. Order of precedence:
/// 1. IP allowlist (trusted, always allowed)  2. IP blocklist  3. first matching rule by policy/rule priority
/// 4. default action of the highest-priority applicable policy  5. implicit deny.
/// </summary>
public static class FirewallEngine
{
    public static FirewallDecision Evaluate(IEnumerable<FirewallPolicy> policies, TrafficPacket packet) =>
        Evaluate(policies, packet, [], DateTime.UtcNow);

    public static FirewallDecision Evaluate(
        IEnumerable<FirewallPolicy> policies, TrafficPacket packet, IEnumerable<IpListEntry> ipList, DateTime now)
    {
        var listed = IpListEvaluator.Match(ipList, packet, now);
        if (listed is not null)
        {
            var allowed = listed.ListType == IpListType.Allow;
            var why = string.IsNullOrWhiteSpace(listed.Description) ? string.Empty : $" ({listed.Description})";
            return new FirewallDecision(
                allowed ? RuleAction.Allow : RuleAction.Deny, null, null,
                $"{(allowed ? "Allowed by IP allowlist" : "Blocked by IP blocklist")} entry {listed.Address}{why}",
                listed);
        }

        var applicable = policies
            .Where(p => p.AppliesTo(packet))
            .OrderBy(p => p.Priority)
            .ToList();

        foreach (var policy in applicable)
        {
            var rule = policy.FindMatchingRule(packet);
            if (rule is not null)
            {
                return new FirewallDecision(rule.Action, policy, rule,
                    $"Matched rule '{rule.Name}' in policy '{policy.Name}'");
            }
        }

        if (applicable.Count > 0)
        {
            var top = applicable[0];
            return new FirewallDecision(top.DefaultAction, top, null,
                $"No rule matched; default action of policy '{top.Name}'");
        }

        return new FirewallDecision(RuleAction.Deny, null, null, "No applicable policy; implicit deny");
    }
}

/// <summary>Matches traffic against IP list entries using the remote party of the connection.</summary>
public static class IpListEvaluator
{
    /// <summary>Inbound → source, outbound → destination, unknown direction → both.</summary>
    public static IReadOnlyList<string> RemoteAddresses(TrafficPacket p) => p.Direction switch
    {
        TrafficDirection.Inbound => [p.SourceIp],
        TrafficDirection.Outbound => [p.DestinationIp],
        _ => [p.SourceIp, p.DestinationIp]
    };

    /// <summary>The deciding entry: an active allowlist match wins over an active blocklist match.</summary>
    public static IpListEntry? Match(IEnumerable<IpListEntry> entries, TrafficPacket packet, DateTime now)
    {
        var remotes = RemoteAddresses(packet);
        var active = entries.Where(e => e.IsActive(now) && remotes.Any(e.Matches)).ToList();
        return active.FirstOrDefault(e => e.ListType == IpListType.Allow)
               ?? active.FirstOrDefault(e => e.ListType == IpListType.Block);
    }

    /// <summary>All entries (active or not) that contain <paramref name="ip"/>, for the "test IP" tool.</summary>
    public static IReadOnlyList<IpListEntry> EntriesContaining(IEnumerable<IpListEntry> entries, string ip) =>
        entries.Where(e => e.Matches(ip)).ToList();
}
