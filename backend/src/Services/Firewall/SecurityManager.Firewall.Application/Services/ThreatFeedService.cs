using System.ComponentModel.DataAnnotations;
using SecurityManager.Firewall.Application.Abstractions;
using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Firewall.Domain.Services;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Firewall.Application.Services;

public sealed record ThreatFeedDto(
    Guid Id, string Name, string Url, ThreatFeedKind Kind, string? CountryCode, IpListType ListType, bool IsEnabled,
    int RefreshMinutes, int MaxEntries, DateTime? LastAttemptAt, DateTime? LastSuccessAt, string? LastError,
    int EntryCount, int SkippedCount, DateTime CreatedAt);

public sealed class SaveThreatFeedRequest
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(1000)] public string Url { get; set; } = string.Empty;
    public ThreatFeedKind Kind { get; set; } = ThreatFeedKind.ThreatIntel;
    [StringLength(2, MinimumLength = 2)] public string? CountryCode { get; set; }
    public IpListType ListType { get; set; } = IpListType.Block;
    public bool IsEnabled { get; set; } = true;
    [Range(ThreatFeed.MinRefreshMinutes, 10080)] public int RefreshMinutes { get; set; } = 360;
    [Range(1, 200_000)] public int MaxEntries { get; set; } = 50_000;
}

public sealed record FeedRefreshResultDto(ThreatFeedDto Feed, int Added, int Removed, IReadOnlyList<string> SkippedExamples);

/// <summary>Downloads a feed body (implemented over HTTP in Infrastructure).</summary>
public interface IFeedDownloader
{
    Task<string> DownloadAsync(string url, CancellationToken ct = default);
}

public interface IThreatFeedService
{
    Task<IReadOnlyList<ThreatFeedDto>> GetAllAsync(CancellationToken ct = default);
    Task<ThreatFeedDto> CreateAsync(SaveThreatFeedRequest request, CancellationToken ct = default);
    Task<ThreatFeedDto> UpdateAsync(Guid id, SaveThreatFeedRequest request, CancellationToken ct = default);
    /// <summary>Deletes the feed and every list entry it created.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<FeedRefreshResultDto> RefreshAsync(Guid id, CancellationToken ct = default);
    /// <summary>Refreshes every enabled feed whose interval has elapsed (called by the background worker).</summary>
    Task<int> RefreshDueAsync(CancellationToken ct = default);
}

public sealed class ThreatFeedService(IFirewallUnitOfWork uow, IFeedDownloader downloader) : IThreatFeedService
{
    public async Task<IReadOnlyList<ThreatFeedDto>> GetAllAsync(CancellationToken ct = default) =>
        (await uow.ThreatFeeds.ListAsync(orderBy: o => o.OrderBy(f => f.Kind).ThenBy(f => f.Name), ct: ct)).Select(ToDto).ToList();

    public async Task<ThreatFeedDto> CreateAsync(SaveThreatFeedRequest r, CancellationToken ct = default)
    {
        var feed = new ThreatFeed();
        Apply(feed, r);
        if (await uow.ThreatFeeds.AnyAsync(f => f.Url == feed.Url && f.ListType == feed.ListType, ct))
            throw new ConflictException("This feed is already configured.");
        await uow.ThreatFeeds.AddAsync(feed, ct);
        await uow.SaveChangesAsync(ct);
        return ToDto(feed);
    }

    public async Task<ThreatFeedDto> UpdateAsync(Guid id, SaveThreatFeedRequest r, CancellationToken ct = default)
    {
        var feed = await Load(id, ct);
        var listChanged = feed.ListType != r.ListType || feed.Url != r.Url.Trim();
        Apply(feed, r);
        if (listChanged)
        {
            // Entries from the old URL / list no longer apply; the next refresh repopulates.
            await RemoveEntriesAsync(feed, ct);
            feed.LastAttemptAt = null;
            feed.EntryCount = 0;
        }
        if (!feed.IsEnabled) await SetEntriesEnabledAsync(feed, false, ct);
        await uow.SaveChangesAsync(ct);
        return ToDto(feed);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var feed = await Load(id, ct);
        await RemoveEntriesAsync(feed, ct);
        uow.ThreatFeeds.Remove(feed);
        await uow.SaveChangesAsync(ct);
    }

    public async Task<FeedRefreshResultDto> RefreshAsync(Guid id, CancellationToken ct = default) =>
        await RefreshFeedAsync(await Load(id, ct), ct);

    public async Task<int> RefreshDueAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var due = (await uow.ThreatFeeds.ListAsync(f => f.IsEnabled, ct: ct)).Where(f => f.IsDue(now)).ToList();
        foreach (var feed in due) await RefreshFeedAsync(feed, ct);
        return due.Count;
    }

    private async Task<FeedRefreshResultDto> RefreshFeedAsync(ThreatFeed feed, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        feed.LastAttemptAt = now;
        ParsedFeed parsed;
        try
        {
            parsed = FeedParser.Parse(await downloader.DownloadAsync(feed.Url, ct), feed.ListType);
            if (parsed.Addresses.Count == 0)
                throw new InvalidOperationException("The download contained no usable addresses; the existing entries were kept.");
            if (parsed.Addresses.Count > feed.MaxEntries)
                throw new InvalidOperationException($"The feed has {parsed.Addresses.Count:N0} entries, above this feed's limit of {feed.MaxEntries:N0}; the existing entries were kept.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            feed.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            await uow.SaveChangesAsync(ct);
            return new FeedRefreshResultDto(ToDto(feed), 0, 0, []);
        }

        var reference = feed.Reference;
        var owned = await uow.IpList.ListAsync(e => e.SourceReference == reference, ct: ct);
        var wanted = parsed.Addresses.ToHashSet();

        var stale = owned.Where(e => !wanted.Contains(e.Address)).ToList();
        uow.IpList.RemoveRange(stale);

        // Addresses already listed by someone else (manual entries, other feeds) are left alone.
        var listed = (await uow.IpList.ListAsync(e => e.ListType == feed.ListType, ct: ct)).Select(e => e.Address).ToHashSet();
        var source = feed.Kind == ThreatFeedKind.Country ? IpListSource.Country : IpListSource.ThreatIntel;
        var description = feed.Kind == ThreatFeedKind.Country ? $"Country {feed.CountryCode?.ToUpperInvariant()} ({feed.Name})" : $"Feed: {feed.Name}";
        var toAdd = parsed.Addresses.Where(a => !listed.Contains(a)).Select(a => new IpListEntry
        {
            ListType = feed.ListType,
            Address = a,
            Description = description.Length > 500 ? description[..500] : description,
            Source = source,
            SourceReference = reference,
            CreatedBy = "feed"
        }).ToList();
        await uow.IpList.AddRangeAsync(toAdd, ct);

        feed.LastSuccessAt = now;
        feed.LastError = null;
        feed.EntryCount = owned.Count - stale.Count + toAdd.Count;
        feed.SkippedCount = parsed.Skipped;
        await uow.SaveChangesAsync(ct);
        return new FeedRefreshResultDto(ToDto(feed), toAdd.Count, stale.Count, parsed.SkippedExamples);
    }

    private async Task RemoveEntriesAsync(ThreatFeed feed, CancellationToken ct)
    {
        var reference = feed.Reference;
        uow.IpList.RemoveRange(await uow.IpList.ListAsync(e => e.SourceReference == reference, ct: ct));
    }

    private async Task SetEntriesEnabledAsync(ThreatFeed feed, bool enabled, CancellationToken ct)
    {
        var reference = feed.Reference;
        foreach (var e in await uow.IpList.ListAsync(e => e.SourceReference == reference, ct: ct)) e.IsEnabled = enabled;
    }

    private static void Apply(ThreatFeed f, SaveThreatFeedRequest r)
    {
        if (!Uri.TryCreate(r.Url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new BusinessRuleException("The feed URL must be an http(s) address.");
        if (r.Kind == ThreatFeedKind.Country && (r.CountryCode is null || !r.CountryCode.All(char.IsAsciiLetter)))
            throw new BusinessRuleException("Country feeds need a two-letter country code.");
        f.Name = r.Name.Trim();
        f.Url = uri.ToString();
        f.Kind = r.Kind;
        f.CountryCode = r.Kind == ThreatFeedKind.Country ? r.CountryCode!.ToUpperInvariant() : null;
        f.ListType = r.ListType;
        f.IsEnabled = r.IsEnabled;
        f.RefreshMinutes = r.RefreshMinutes;
        f.MaxEntries = r.MaxEntries;
    }

    private async Task<ThreatFeed> Load(Guid id, CancellationToken ct) =>
        await uow.ThreatFeeds.GetByIdAsync(id, ct: ct) ?? throw new NotFoundException("Threat feed", id);

    public static ThreatFeedDto ToDto(ThreatFeed f) => new(
        f.Id, f.Name, f.Url, f.Kind, f.CountryCode, f.ListType, f.IsEnabled, f.RefreshMinutes, f.MaxEntries,
        f.LastAttemptAt, f.LastSuccessAt, f.LastError, f.EntryCount, f.SkippedCount, f.CreatedAt);
}
