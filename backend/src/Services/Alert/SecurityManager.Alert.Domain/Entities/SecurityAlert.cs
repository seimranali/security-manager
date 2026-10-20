using SecurityManager.Alert.Domain.Enums;
using SecurityManager.Shared.Kernel.Entities;
using SecurityManager.Shared.Kernel.Enums;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Alert.Domain.Entities;

public class SecurityAlert : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? SourceReference { get; set; }
    public string? Category { get; set; }
    public AlertStatus Status { get; set; } = AlertStatus.Open;
    public int OccurrenceCount { get; set; } = 1;
    public DateTime LastOccurredAt { get; set; } = DateTime.UtcNow;
    public string? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }

    public bool IsActive => Status != AlertStatus.Resolved;

    /// <summary>Records a duplicate occurrence instead of creating a new alert.</summary>
    public void RegisterOccurrence(Severity severity, string message, DateTime at)
    {
        OccurrenceCount++;
        LastOccurredAt = at;
        Message = message;
        if (severity > Severity) Severity = severity;
    }

    public void Acknowledge(string user, DateTime at)
    {
        if (Status == AlertStatus.Resolved) throw new BusinessRuleException("Resolved alerts cannot be acknowledged.");
        if (Status == AlertStatus.Acknowledged) return;
        Status = AlertStatus.Acknowledged;
        AcknowledgedBy = user;
        AcknowledgedAt = at;
    }

    public void Resolve(string user, string? notes, DateTime at)
    {
        if (Status == AlertStatus.Resolved) return;
        if (AcknowledgedAt is null)
        {
            AcknowledgedBy = user;
            AcknowledgedAt = at;
        }
        Status = AlertStatus.Resolved;
        ResolvedBy = user;
        ResolvedAt = at;
        ResolutionNotes = notes;
    }

    public void Reopen()
    {
        if (Status != AlertStatus.Resolved) return;
        Status = AlertStatus.Open;
        ResolvedAt = null;
        ResolvedBy = null;
        AcknowledgedAt = null;
        AcknowledgedBy = null;
    }
}
