using SecurityManager.Security.Domain.Enums;
using SecurityManager.Shared.Kernel.Entities;
using SecurityManager.Shared.Kernel.Enums;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Security.Domain.Entities;

public class Detection : BaseEntity
{
    public Guid RuleId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public DetectionStatus Status { get; set; } = DetectionStatus.New;
    public SecurityEventType EventType { get; set; }
    public CorrelationField CorrelateBy { get; set; }
    public string CorrelationKey { get; set; } = DetectionRule.AllKey;
    public int EventCount { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public string? MitreTechnique { get; set; }
    public string? Notes { get; set; }
    public string? AssignedTo { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public bool IsOpen => Status is DetectionStatus.New or DetectionStatus.Investigating;

    public static Detection Raise(DetectionRule rule, string key, int eventCount, DateTime firstSeen, DateTime lastSeen) => new()
    {
        RuleId = rule.Id,
        RuleName = rule.Name,
        Title = key == DetectionRule.AllKey ? rule.Name : $"{rule.Name} — {key}",
        Description = rule.CorrelateBy == CorrelationField.None
            ? $"{eventCount} {rule.EventType} event(s) within {rule.TimeWindowMinutes} min (threshold {rule.Threshold})."
            : $"{eventCount} {rule.EventType} event(s) from {rule.CorrelateBy} '{key}' within {rule.TimeWindowMinutes} min (threshold {rule.Threshold}).",
        Severity = rule.Severity,
        EventType = rule.EventType,
        CorrelateBy = rule.CorrelateBy,
        CorrelationKey = key,
        EventCount = eventCount,
        FirstSeen = firstSeen,
        LastSeen = lastSeen,
        MitreTechnique = rule.MitreTechnique
    };

    public void RegisterOccurrence(int eventCount, DateTime lastSeen)
    {
        EventCount = Math.Max(EventCount + 1, eventCount);
        if (lastSeen > LastSeen) LastSeen = lastSeen;
    }

    public void ChangeStatus(DetectionStatus status, string? notes, string? assignedTo)
    {
        if (!IsOpen && status is DetectionStatus.New)
            throw new BusinessRuleException("A closed detection cannot be moved back to New; use Investigating to reopen.");

        Status = status;
        if (!string.IsNullOrWhiteSpace(notes)) Notes = notes.Trim();
        if (!string.IsNullOrWhiteSpace(assignedTo)) AssignedTo = assignedTo.Trim();
        ResolvedAt = IsOpen ? null : DateTime.UtcNow;
    }
}
