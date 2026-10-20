using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Shared.Kernel.Entities;

namespace SecurityManager.Firewall.Domain.Entities;

public class TrafficLog : BaseEntity
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string SourceIp { get; set; } = string.Empty;
    public string DestinationIp { get; set; } = string.Empty;
    public int? SourcePort { get; set; }
    public int? DestinationPort { get; set; }
    public NetworkProtocol Protocol { get; set; }
    public TrafficDirection Direction { get; set; }
    public RuleAction Action { get; set; }
    public Guid? MatchedRuleId { get; set; }
    public string? MatchedRuleName { get; set; }
    public string? PolicyName { get; set; }
    public string Reason { get; set; } = string.Empty;
}
