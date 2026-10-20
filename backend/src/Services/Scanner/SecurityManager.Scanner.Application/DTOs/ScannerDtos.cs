using System.ComponentModel.DataAnnotations;
using SecurityManager.Scanner.Domain.Enums;
using SecurityManager.Shared.Kernel.Enums;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Scanner.Application.DTOs;

// ---------- Targets ----------
public sealed record TargetDto(
    Guid Id, string Name, string Host, string Ports, string? Description, bool IsEnabled,
    DateTime? LastScannedAt, int? RiskScore, DateTime CreatedAt);

public sealed class UpsertTargetRequest
{
    [Required, StringLength(100, MinimumLength = 2)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(253)] public string Host { get; set; } = string.Empty;
    [Required, StringLength(1000)] public string Ports { get; set; } = string.Empty;
    [StringLength(500)] public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
}

// ---------- Schedules ----------
public sealed record ScheduleDto(
    Guid Id, Guid TargetId, string TargetName, string Name, ScanType ScanTypes, int IntervalMinutes,
    string? RunAtUtc, bool IsEnabled, DateTime? LastRunAt, DateTime? NextRunAt, DateTime CreatedAt);

public sealed class UpsertScheduleRequest
{
    [Required] public Guid TargetId { get; set; }
    [Required, StringLength(100, MinimumLength = 2)] public string Name { get; set; } = string.Empty;
    public ScanType ScanTypes { get; set; } = ScanType.All;
    [Range(5, 10080)] public int IntervalMinutes { get; set; } = 60;

    /// <summary>Time of day in UTC ("HH:mm") for whole-day intervals.</summary>
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Run time must be in HH:mm (24-hour) format.")]
    public string? RunAtUtc { get; set; }

    public bool IsEnabled { get; set; } = true;
}

// ---------- Jobs ----------
public sealed record ScanJobDto(
    Guid Id, Guid? TargetId, string TargetName, string Host, ScanType ScanTypes, ScanStatus Status,
    ScanTrigger Trigger, int Progress, DateTime? StartedAt, DateTime? CompletedAt, double? DurationSeconds,
    string? OpenPorts, int FindingsCount, int CriticalCount, int HighCount, int MediumCount, int LowCount,
    string? Error, DateTime CreatedAt);

public sealed record ScanJobDetailDto(ScanJobDto Job, IReadOnlyList<FindingDto> Findings);

public sealed class StartScanRequest
{
    [Required] public Guid TargetId { get; set; }
    public ScanType ScanTypes { get; set; } = ScanType.All;
}

public sealed class ScanJobQuery : PagedQuery
{
    public Guid? TargetId { get; set; }
    public ScanStatus? Status { get; set; }
}

// ---------- Findings ----------
public sealed record FindingDto(
    Guid Id, Guid JobId, Guid? TargetId, string TargetName, string Title, string Description, Severity Severity,
    string Category, int? Port, string? Evidence, string? Recommendation, FindingStatus Status, DateTime CreatedAt);

public sealed class FindingQuery : PagedQuery
{
    public Guid? TargetId { get; set; }
    public Severity? MinSeverity { get; set; }
    public FindingStatus? Status { get; set; }
    public string? Category { get; set; }
    public string? Search { get; set; }
}

public sealed class UpdateFindingStatusRequest
{
    [Required] public FindingStatus Status { get; set; }
}

// ---------- Stats ----------
public sealed record SeverityCount(string Severity, int Count);

public sealed record RiskTargetDto(Guid Id, string Name, string Host, int RiskScore, DateTime? LastScannedAt);

public sealed record ScannerStatsDto(
    int Targets, int ActiveSchedules, int Scans24h, int ActiveScans, int OpenFindings, int CriticalOpenFindings,
    IReadOnlyList<SeverityCount> OpenFindingsBySeverity, IReadOnlyList<RiskTargetDto> TopRiskTargets,
    IReadOnlyList<ScanJobDto> RecentJobs);
