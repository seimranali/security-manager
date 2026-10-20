using SecurityManager.Security.Application.DTOs;
using SecurityManager.Security.Domain.Entities;

namespace SecurityManager.Security.Application.Mapping;

internal static class SecurityMappings
{
    public static SecurityEventDto ToDto(this SecurityEvent e) => new(
        e.Id, e.Timestamp, e.EventType, e.Severity, e.Source, e.Message,
        e.SourceIp, e.TargetHost, e.UserName, e.RawData, e.IsProcessed);

    public static DetectionDto ToDto(this Detection d) => new(
        d.Id, d.RuleId, d.RuleName, d.Title, d.Description, d.Severity, d.Status, d.EventType,
        d.CorrelateBy, d.CorrelationKey, d.EventCount, d.FirstSeen, d.LastSeen, d.MitreTechnique,
        d.Notes, d.AssignedTo, d.ResolvedAt, d.CreatedAt);

    public static DetectionRuleDto ToDto(this DetectionRule r) => new(
        r.Id, r.Name, r.Description, r.EventType, r.MinEventSeverity, r.Threshold, r.TimeWindowMinutes,
        r.CorrelateBy, r.Severity, r.MitreTechnique, r.IsEnabled, r.TriggerCount, r.LastTriggeredAt, r.CreatedAt);

    public static void Apply(this DetectionRule rule, UpsertDetectionRuleRequest r)
    {
        rule.Name = r.Name.Trim();
        rule.Description = r.Description?.Trim();
        rule.EventType = r.EventType;
        rule.MinEventSeverity = r.MinEventSeverity;
        rule.Threshold = r.Threshold;
        rule.TimeWindowMinutes = r.TimeWindowMinutes;
        rule.CorrelateBy = r.CorrelateBy;
        rule.Severity = r.Severity;
        rule.MitreTechnique = string.IsNullOrWhiteSpace(r.MitreTechnique) ? null : r.MitreTechnique.Trim().ToUpperInvariant();
        rule.IsEnabled = r.IsEnabled;
    }
}
