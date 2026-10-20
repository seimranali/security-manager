using System.ComponentModel.DataAnnotations;
using SecurityManager.Alert.Domain.Enums;
using SecurityManager.Shared.Kernel.Enums;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Alert.Application.DTOs;

public sealed record AlertDto(
    Guid Id, string Title, string Message, Severity Severity, string Source, string? SourceReference,
    string? Category, AlertStatus Status, int OccurrenceCount, DateTime CreatedAt, DateTime LastOccurredAt,
    string? AcknowledgedBy, DateTime? AcknowledgedAt, string? ResolvedBy, DateTime? ResolvedAt, string? ResolutionNotes);

/// <summary>Matches the shared <c>AlertMessage</c> integration contract.</summary>
public sealed class CreateAlertRequest
{
    [Required, StringLength(300)] public string Title { get; set; } = string.Empty;
    [Required, StringLength(2000)] public string Message { get; set; } = string.Empty;
    public Severity Severity { get; set; } = Severity.Medium;
    [Required, StringLength(50)] public string Source { get; set; } = "Manual";
    [StringLength(100)] public string? SourceReference { get; set; }
    [StringLength(100)] public string? Category { get; set; }
}

public sealed class AlertActionRequest
{
    [StringLength(100)] public string? User { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
}

public sealed class BulkAlertActionRequest
{
    [Required, MinLength(1), MaxLength(200)] public List<Guid> Ids { get; set; } = [];
    [StringLength(100)] public string? User { get; set; }
}

public sealed class AlertQuery : PagedQuery
{
    public AlertStatus? Status { get; set; }
    public bool? ActiveOnly { get; set; }
    public Severity? MinSeverity { get; set; }
    public string? Source { get; set; }
    public string? Search { get; set; }
}

public sealed record NameCount(string Name, int Count);

public sealed record DailyAlertPoint(DateTime Day, int Critical, int High, int Medium, int Low, int Info);

public sealed record AlertStatsDto(
    int Open, int Acknowledged, int Resolved24h, int CriticalActive,
    double? MeanTimeToAcknowledgeMinutes, double? MeanTimeToResolveMinutes,
    IReadOnlyList<NameCount> ActiveBySeverity, IReadOnlyList<NameCount> ActiveBySource,
    IReadOnlyList<DailyAlertPoint> Last7Days, IReadOnlyList<AlertDto> Latest);
