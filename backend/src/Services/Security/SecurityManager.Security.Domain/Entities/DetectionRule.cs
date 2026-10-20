using SecurityManager.Security.Domain.Enums;
using SecurityManager.Shared.Kernel.Entities;
using SecurityManager.Shared.Kernel.Enums;

namespace SecurityManager.Security.Domain.Entities;

/// <summary>
/// Threshold-based correlation rule: raise a detection when at least <see cref="Threshold"/> events of
/// <see cref="EventType"/> sharing the same <see cref="CorrelateBy"/> value occur within <see cref="TimeWindowMinutes"/>.
/// </summary>
public class DetectionRule : BaseEntity
{
    public const string AllKey = "*";

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public SecurityEventType EventType { get; set; }
    public Severity MinEventSeverity { get; set; } = Severity.Info;
    public int Threshold { get; set; } = 1;
    public int TimeWindowMinutes { get; set; } = 5;
    public CorrelationField CorrelateBy { get; set; } = CorrelationField.SourceIp;
    public Severity Severity { get; set; } = Severity.Medium;
    public string? MitreTechnique { get; set; }
    public bool IsEnabled { get; set; } = true;
    public long TriggerCount { get; set; }
    public DateTime? LastTriggeredAt { get; set; }

    public bool AppliesTo(SecurityEvent e) =>
        IsEnabled && e.EventType == EventType && e.Severity >= MinEventSeverity;

    /// <summary>Returns the correlation key for an event, or null if the event lacks the correlated attribute.</summary>
    public string? GetCorrelationKey(SecurityEvent e) => CorrelateBy switch
    {
        CorrelationField.SourceIp => NullIfEmpty(e.SourceIp),
        CorrelationField.UserName => NullIfEmpty(e.UserName),
        CorrelationField.TargetHost => NullIfEmpty(e.TargetHost),
        _ => AllKey
    };

    public void RegisterTrigger(DateTime at)
    {
        TriggerCount++;
        LastTriggeredAt = at;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
