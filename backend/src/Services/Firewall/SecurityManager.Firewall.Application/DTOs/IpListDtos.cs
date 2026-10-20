using System.ComponentModel.DataAnnotations;
using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Firewall.Application.DTOs;

public sealed record IpListEntryDto(
    Guid Id, IpListType ListType, string Address, string? Description, IpListSource Source, string? SourceReference,
    DateTime? ExpiresAt, bool IsEnabled, bool IsActive, bool IsExpired, long HitCount, DateTime? LastHitAt,
    string? CreatedBy, DateTime CreatedAt, DateTime? UpdatedAt);

public sealed class UpsertIpListEntryRequest
{
    public IpListType ListType { get; set; } = IpListType.Block;
    [Required, StringLength(100)] public string Address { get; set; } = string.Empty;
    [StringLength(500)] public string? Description { get; set; }
    /// <summary>UTC expiry; null = permanent.</summary>
    public DateTime? ExpiresAt { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class IpListQuery : PagedQuery
{
    public IpListType? ListType { get; set; }
    public bool? ActiveOnly { get; set; }
    public string? Search { get; set; }
}

public sealed class ImportIpListRequest
{
    public IpListType ListType { get; set; } = IpListType.Block;
    /// <summary>Addresses separated by newlines, commas, spaces or semicolons; "#" starts a comment.</summary>
    [Required, StringLength(200_000)] public string Addresses { get; set; } = string.Empty;
    [StringLength(500)] public string? Description { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public sealed record ImportIpListResultDto(int Added, int Skipped, IReadOnlyList<string> Invalid);

/// <summary>Used by other services (e.g. Security detections) to block an address temporarily.</summary>
public sealed class AutoBlockRequest
{
    [Required, StringLength(100)] public string Address { get; set; } = string.Empty;
    [Required, StringLength(500)] public string Reason { get; set; } = string.Empty;
    [Range(1, 43200)] public int DurationMinutes { get; set; } = 1440;
    [StringLength(100)] public string? SourceReference { get; set; }
}

public sealed record IpCheckResultDto(string Ip, string Verdict, IpListEntryDto? DecidingEntry, IReadOnlyList<IpListEntryDto> Matches);

public sealed class BulkIdsRequest
{
    [Required, MinLength(1), MaxLength(1000)] public List<Guid> Ids { get; set; } = [];
}
