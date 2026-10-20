using SecurityManager.Scanner.Application.DTOs;
using SecurityManager.Scanner.Domain.Entities;

namespace SecurityManager.Scanner.Application.Mapping;

internal static class ScannerMappings
{
    public static TargetDto ToDto(this ScanTarget t) => new(
        t.Id, t.Name, t.Host, t.Ports, t.Description, t.IsEnabled, t.LastScannedAt, t.RiskScore, t.CreatedAt);

    public static ScheduleDto ToDto(this ScanSchedule s) => new(
        s.Id, s.TargetId, s.Target?.Name ?? string.Empty, s.Name, s.ScanTypes, s.IntervalMinutes,
        s.RunAtUtc?.ToString("HH\\:mm"), s.IsEnabled, s.LastRunAt, s.NextRunAt, s.CreatedAt);

    public static ScanJobDto ToDto(this ScanJob j) => new(
        j.Id, j.TargetId, j.TargetName, j.Host, j.ScanTypes, j.Status, j.Trigger, j.Progress,
        j.StartedAt, j.CompletedAt,
        j.StartedAt is { } s && j.CompletedAt is { } c ? Math.Round((c - s).TotalSeconds, 1) : null,
        j.OpenPorts, j.FindingsCount, j.CriticalCount, j.HighCount, j.MediumCount, j.LowCount, j.Error, j.CreatedAt);

    public static FindingDto ToDto(this ScanFinding f) => new(
        f.Id, f.JobId, f.TargetId, f.TargetName, f.Title, f.Description, f.Severity, f.Category, f.Port,
        f.Evidence, f.Recommendation, f.Status, f.CreatedAt);
}
