using SecurityManager.Firewall.Application.DTOs;
using SecurityManager.Firewall.Domain.Entities;

namespace SecurityManager.Firewall.Application.Mapping;

internal static class FirewallMappings
{
    public static PolicyDto ToDto(this FirewallPolicy p) => new(
        p.Id, p.Name, p.Description, p.IsEnabled, p.Priority, p.Direction, p.DefaultAction, p.EnforceOnHosts,
        p.Rules.Count, p.Rules.Count(r => r.IsEnabled), p.CreatedAt, p.UpdatedAt);

    public static RuleDto ToDto(this FirewallRule r) => new(
        r.Id, r.PolicyId, r.Policy?.Name ?? string.Empty, r.Name, r.Description, r.Priority,
        r.Action, r.Direction, r.Protocol, r.SourceAddress, r.DestinationAddress,
        r.SourcePort, r.DestinationPort, r.IsEnabled, r.HitCount, r.LastHitAt, r.CreatedAt);

    public static TrafficLogDto ToDto(this TrafficLog l) => new(
        l.Id, l.Timestamp, l.SourceIp, l.DestinationIp, l.SourcePort, l.DestinationPort,
        l.Protocol, l.Direction, l.Action, l.MatchedRuleName, l.PolicyName, l.Reason);

    public static void Apply(this FirewallPolicy p, UpsertPolicyRequest r)
    {
        p.Name = r.Name.Trim();
        p.Description = r.Description?.Trim();
        p.IsEnabled = r.IsEnabled;
        p.Priority = r.Priority;
        p.Direction = r.Direction;
        p.DefaultAction = r.DefaultAction;
        p.EnforceOnHosts = r.EnforceOnHosts;
    }

    public static void Apply(this FirewallRule rule, UpsertRuleRequest r)
    {
        rule.PolicyId = r.PolicyId;
        rule.Name = r.Name.Trim();
        rule.Description = r.Description?.Trim();
        rule.Priority = r.Priority;
        rule.Action = r.Action;
        rule.Direction = r.Direction;
        rule.Protocol = r.Protocol;
        rule.SourceAddress = Normalize(r.SourceAddress);
        rule.DestinationAddress = Normalize(r.DestinationAddress);
        rule.SourcePort = Normalize(r.SourcePort);
        rule.DestinationPort = Normalize(r.DestinationPort);
        rule.IsEnabled = r.IsEnabled;
    }

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim() == "*" ? "any" : value.Trim().ToLowerInvariant();
}
