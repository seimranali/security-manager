using SecurityManager.Scanner.Domain.Enums;
using SecurityManager.Shared.Kernel.Entities;
using SecurityManager.Shared.Kernel.Enums;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Scanner.Domain.Entities;

public class ScanJob : BaseEntity
{
    public Guid? TargetId { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public ScanType ScanTypes { get; set; } = ScanType.All;
    public ScanStatus Status { get; set; } = ScanStatus.Queued;
    public ScanTrigger Trigger { get; set; } = ScanTrigger.Manual;
    public Guid? ScheduleId { get; set; }
    public int Progress { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? OpenPorts { get; set; }
    public int FindingsCount { get; set; }
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public int MediumCount { get; set; }
    public int LowCount { get; set; }
    public string? Error { get; set; }

    public ICollection<ScanFinding> Findings { get; set; } = new List<ScanFinding>();

    public static ScanJob For(ScanTarget target, ScanType types, ScanTrigger trigger, Guid? scheduleId = null)
    {
        if (types == ScanType.None) throw new BusinessRuleException("Select at least one scan type.");
        return new ScanJob
        {
            TargetId = target.Id,
            TargetName = target.Name,
            Host = target.Host,
            ScanTypes = types,
            Trigger = trigger,
            ScheduleId = scheduleId
        };
    }

    public bool IsActive => Status is ScanStatus.Queued or ScanStatus.Running;

    public void Start(DateTime at)
    {
        if (Status != ScanStatus.Queued) throw new BusinessRuleException($"Cannot start a scan in status {Status}.");
        Status = ScanStatus.Running;
        StartedAt = at;
        Progress = 5;
    }

    public void ReportProgress(int percent) => Progress = Math.Clamp(percent, 0, 99);

    public void Complete(DateTime at, IReadOnlyCollection<ScanFinding> findings)
    {
        Status = ScanStatus.Completed;
        CompletedAt = at;
        Progress = 100;
        FindingsCount = findings.Count;
        CriticalCount = findings.Count(f => f.Severity == Severity.Critical);
        HighCount = findings.Count(f => f.Severity == Severity.High);
        MediumCount = findings.Count(f => f.Severity == Severity.Medium);
        LowCount = findings.Count(f => f.Severity == Severity.Low);
    }

    public void Fail(string error, DateTime at)
    {
        Status = ScanStatus.Failed;
        Error = error.Length > 1000 ? error[..1000] : error;
        CompletedAt = at;
    }

    public void Cancel(DateTime at)
    {
        if (Status != ScanStatus.Queued) throw new BusinessRuleException("Only queued scans can be cancelled.");
        Status = ScanStatus.Cancelled;
        CompletedAt = at;
    }
}
