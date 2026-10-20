using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Shared.Kernel.Entities;

namespace SecurityManager.Firewall.Domain.Entities;

public enum ThreatFeedKind
{
    /// <summary>Reputation list of malicious addresses (Spamhaus DROP, FireHOL, abuse.ch, ...).</summary>
    ThreatIntel,
    /// <summary>All address blocks registered to a country (geo-blocking or geo-allowing).</summary>
    Country
}

/// <summary>
/// A remote plain-text list of IPs / CIDR blocks / ranges, downloaded on a schedule and kept in sync with the
/// IP blocklist (or allowlist). Its entries are tagged with the feed so they can be replaced on every refresh.
/// </summary>
public class ThreatFeed : BaseEntity
{
    public const int MinRefreshMinutes = 15;

    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public ThreatFeedKind Kind { get; set; } = ThreatFeedKind.ThreatIntel;
    /// <summary>ISO 3166-1 alpha-2 code for country feeds.</summary>
    public string? CountryCode { get; set; }
    public IpListType ListType { get; set; } = IpListType.Block;
    public bool IsEnabled { get; set; } = true;
    public int RefreshMinutes { get; set; } = 360;
    /// <summary>Safety limit: refuse to apply a download with more entries than this.</summary>
    public int MaxEntries { get; set; } = 50_000;

    public DateTime? LastAttemptAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public int EntryCount { get; set; }
    /// <summary>Lines skipped on the last refresh (invalid, too broad, or private/reserved addresses).</summary>
    public int SkippedCount { get; set; }

    /// <summary>Value stored in <see cref="IpListEntry.SourceReference"/> for this feed's entries.</summary>
    public string Reference => $"feed:{Id:N}";

    public bool IsDue(DateTime now) =>
        IsEnabled && (LastAttemptAt is null || LastAttemptAt.Value.AddMinutes(Math.Max(RefreshMinutes, MinRefreshMinutes)) <= now);
}
