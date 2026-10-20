using System.ComponentModel.DataAnnotations;
using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Firewall.Application.DTOs;

// ---------- Policies ----------
public sealed record PolicyDto(
    Guid Id, string Name, string? Description, bool IsEnabled, int Priority,
    TrafficDirection Direction, RuleAction DefaultAction, bool EnforceOnHosts, int RuleCount, int ActiveRuleCount,
    DateTime CreatedAt, DateTime? UpdatedAt);

public sealed class UpsertPolicyRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    public bool IsEnabled { get; set; } = true;

    [Range(1, 10000)]
    public int Priority { get; set; } = 100;

    public TrafficDirection Direction { get; set; } = TrafficDirection.Any;
    public RuleAction DefaultAction { get; set; } = RuleAction.Deny;
    /// <summary>Push this policy's rules to host firewalls (enforcement agents).</summary>
    public bool EnforceOnHosts { get; set; }
}

// ---------- Rules ----------
public sealed record RuleDto(
    Guid Id, Guid PolicyId, string PolicyName, string Name, string? Description, int Priority,
    RuleAction Action, TrafficDirection Direction, NetworkProtocol Protocol,
    string SourceAddress, string DestinationAddress, string SourcePort, string DestinationPort,
    bool IsEnabled, long HitCount, DateTime? LastHitAt, DateTime CreatedAt);

public sealed class UpsertRuleRequest
{
    [Required]
    public Guid PolicyId { get; set; }

    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Range(1, 10000)]
    public int Priority { get; set; } = 100;

    public RuleAction Action { get; set; } = RuleAction.Deny;
    public TrafficDirection Direction { get; set; } = TrafficDirection.Inbound;
    public NetworkProtocol Protocol { get; set; } = NetworkProtocol.Any;

    [Required, StringLength(500)] public string SourceAddress { get; set; } = "any";
    [Required, StringLength(500)] public string DestinationAddress { get; set; } = "any";
    [Required, StringLength(200)] public string SourcePort { get; set; } = "any";
    [Required, StringLength(200)] public string DestinationPort { get; set; } = "any";

    public bool IsEnabled { get; set; } = true;
}

public sealed class RuleQuery : PagedQuery
{
    public Guid? PolicyId { get; set; }
    public string? Search { get; set; }
    public RuleAction? Action { get; set; }
    public bool? IsEnabled { get; set; }
}

// ---------- Traffic ----------
public sealed class EvaluateTrafficRequest
{
    [Required, StringLength(45)] public string SourceIp { get; set; } = string.Empty;
    [Required, StringLength(45)] public string DestinationIp { get; set; } = string.Empty;
    [Range(0, 65535)] public int? SourcePort { get; set; }
    [Range(0, 65535)] public int? DestinationPort { get; set; }
    public NetworkProtocol Protocol { get; set; } = NetworkProtocol.Tcp;
    public TrafficDirection Direction { get; set; } = TrafficDirection.Inbound;
}

public sealed record EvaluationResultDto(
    RuleAction Action, Guid? MatchedRuleId, string? MatchedRuleName, string? PolicyName,
    string Reason, DateTime EvaluatedAt);

public sealed record TrafficLogDto(
    Guid Id, DateTime Timestamp, string SourceIp, string DestinationIp, int? SourcePort, int? DestinationPort,
    NetworkProtocol Protocol, TrafficDirection Direction, RuleAction Action,
    string? MatchedRuleName, string? PolicyName, string Reason);

public sealed class TrafficLogQuery : PagedQuery
{
    public RuleAction? Action { get; set; }
    public string? Ip { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

// ---------- Stats ----------
public sealed record TimelinePoint(DateTime Hour, int Allowed, int Denied);

public sealed record TopRuleDto(Guid Id, string Name, RuleAction Action, long HitCount);

public sealed record FirewallStatsDto(
    int TotalPolicies, int ActivePolicies, int TotalRules, int ActiveRules,
    int Allowed24h, int Denied24h, IReadOnlyList<TopRuleDto> TopRules, IReadOnlyList<TimelinePoint> Timeline);
