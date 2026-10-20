using System.ComponentModel.DataAnnotations;
using SecurityManager.Security.Domain.Enums;
using SecurityManager.Shared.Kernel.Enums;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Security.Application.DTOs;

// ---------- Events ----------
public sealed record SecurityEventDto(
    Guid Id, DateTime Timestamp, SecurityEventType EventType, Severity Severity, string Source, string Message,
    string? SourceIp, string? TargetHost, string? UserName, string? RawData, bool IsProcessed);

public sealed class CreateEventRequest
{
    [Required] public SecurityEventType EventType { get; set; }
    public Severity Severity { get; set; } = Severity.Info;
    [Required, StringLength(100)] public string Source { get; set; } = string.Empty;
    [Required, StringLength(1000)] public string Message { get; set; } = string.Empty;
    [StringLength(45)] public string? SourceIp { get; set; }
    [StringLength(255)] public string? TargetHost { get; set; }
    [StringLength(100)] public string? UserName { get; set; }
    [StringLength(4000)] public string? RawData { get; set; }
    public DateTime? Timestamp { get; set; }
}

public sealed record IngestResultDto(SecurityEventDto Event, IReadOnlyList<DetectionDto> NewDetections);

public sealed class EventQuery : PagedQuery
{
    public SecurityEventType? EventType { get; set; }
    public Severity? MinSeverity { get; set; }
    public string? Search { get; set; }
    public string? SourceIp { get; set; }
    public string? UserName { get; set; }
    public string? TargetHost { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

// ---------- Detections ----------
public sealed record DetectionDto(
    Guid Id, Guid RuleId, string RuleName, string Title, string Description, Severity Severity,
    DetectionStatus Status, SecurityEventType EventType, CorrelationField CorrelateBy, string CorrelationKey,
    int EventCount, DateTime FirstSeen, DateTime LastSeen, string? MitreTechnique, string? Notes,
    string? AssignedTo, DateTime? ResolvedAt, DateTime CreatedAt);

public sealed record DetectionDetailDto(DetectionDto Detection, IReadOnlyList<SecurityEventDto> Evidence);

public sealed class UpdateDetectionStatusRequest
{
    [Required] public DetectionStatus Status { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
    [StringLength(100)] public string? AssignedTo { get; set; }
}

public sealed class DetectionQuery : PagedQuery
{
    public DetectionStatus? Status { get; set; }
    public Severity? MinSeverity { get; set; }
    public bool? OpenOnly { get; set; }
    public string? Search { get; set; }
}

// ---------- Detection rules ----------
public sealed record DetectionRuleDto(
    Guid Id, string Name, string? Description, SecurityEventType EventType, Severity MinEventSeverity,
    int Threshold, int TimeWindowMinutes, CorrelationField CorrelateBy, Severity Severity,
    string? MitreTechnique, bool IsEnabled, long TriggerCount, DateTime? LastTriggeredAt, DateTime CreatedAt);

public sealed class UpsertDetectionRuleRequest
{
    [Required, StringLength(120, MinimumLength = 3)] public string Name { get; set; } = string.Empty;
    [StringLength(500)] public string? Description { get; set; }
    [Required] public SecurityEventType EventType { get; set; }
    public Severity MinEventSeverity { get; set; } = Severity.Info;
    [Range(1, 10000)] public int Threshold { get; set; } = 1;
    [Range(1, 10080)] public int TimeWindowMinutes { get; set; } = 5;
    public CorrelationField CorrelateBy { get; set; } = CorrelationField.SourceIp;
    public Severity Severity { get; set; } = Severity.Medium;
    [StringLength(20)] public string? MitreTechnique { get; set; }
    public bool IsEnabled { get; set; } = true;
}

// ---------- Stats ----------
public sealed record CountItem(string Name, int Count);

public sealed record SecurityTimelinePoint(DateTime Hour, int Events, int Detections);

public sealed record SecurityStatsDto(
    int Events24h, int CriticalEvents24h, int OpenDetections, int CriticalOpenDetections, int ActiveRules,
    IReadOnlyList<CountItem> EventsBySeverity, IReadOnlyList<CountItem> EventsByType,
    IReadOnlyList<CountItem> DetectionsByStatus, IReadOnlyList<CountItem> TopSources,
    IReadOnlyList<SecurityTimelinePoint> Timeline);
