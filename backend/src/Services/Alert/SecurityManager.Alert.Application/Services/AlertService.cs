using SecurityManager.Alert.Application.Abstractions;
using SecurityManager.Alert.Application.DTOs;
using SecurityManager.Alert.Domain.Entities;
using SecurityManager.Alert.Domain.Enums;
using SecurityManager.Shared.Kernel.Enums;
using SecurityManager.Shared.Kernel.Exceptions;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Alert.Application.Services;

public interface IAlertService
{
    Task<AlertDto> CreateAsync(CreateAlertRequest request, CancellationToken ct = default);
    Task<PagedResult<AlertDto>> GetPagedAsync(AlertQuery query, CancellationToken ct = default);
    Task<AlertDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AlertDto> AcknowledgeAsync(Guid id, AlertActionRequest request, CancellationToken ct = default);
    Task<AlertDto> ResolveAsync(Guid id, AlertActionRequest request, CancellationToken ct = default);
    Task<AlertDto> ReopenAsync(Guid id, CancellationToken ct = default);
    Task<int> BulkAcknowledgeAsync(BulkAlertActionRequest request, CancellationToken ct = default);
    Task<int> BulkResolveAsync(BulkAlertActionRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<AlertStatsDto> GetStatsAsync(CancellationToken ct = default);
}

public sealed class AlertService(IAlertUnitOfWork uow, IAlertNotifier notifier, INotificationQueue notifications) : IAlertService
{
    private const string DefaultUser = "analyst";

    public async Task<AlertDto> CreateAsync(CreateAlertRequest r, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var source = r.Source.Trim();
        var reference = string.IsNullOrWhiteSpace(r.SourceReference) ? null : r.SourceReference.Trim();
        var title = r.Title.Trim();

        // Deduplicate: an active alert from the same source/reference (or same title when no reference) is reused.
        var existing = await uow.Alerts.FirstOrDefaultAsync(a =>
            a.Status != AlertStatus.Resolved && a.Source == source &&
            (reference != null ? a.SourceReference == reference : a.SourceReference == null && a.Title == title), ct: ct);

        if (existing is not null)
        {
            var previousSeverity = existing.Severity;
            existing.RegisterOccurrence(r.Severity, r.Message.Trim(), now);
            await uow.SaveChangesAsync(ct);
            var updated = ToDto(existing);
            await notifier.AlertUpdatedAsync(updated, ct);
            if (existing.Severity > previousSeverity) notifications.Enqueue(updated, escalation: true);
            return updated;
        }

        var alert = new SecurityAlert
        {
            Title = title,
            Message = r.Message.Trim(),
            Severity = r.Severity,
            Source = source,
            SourceReference = reference,
            Category = r.Category?.Trim(),
            LastOccurredAt = now
        };
        await uow.Alerts.AddAsync(alert, ct);
        await uow.SaveChangesAsync(ct);

        var dto = ToDto(alert);
        await notifier.AlertCreatedAsync(dto, ct);
        notifications.Enqueue(dto, escalation: false);
        return dto;
    }

    public async Task<PagedResult<AlertDto>> GetPagedAsync(AlertQuery q, CancellationToken ct = default)
    {
        var search = q.Search?.Trim();
        var source = q.Source?.Trim();
        var activeOnly = q.ActiveOnly == true;
        var result = await uow.Alerts.GetPagedAsync(
            q.Page, q.PageSize,
            a => (q.Status == null || a.Status == q.Status)
                 && (!activeOnly || a.Status != AlertStatus.Resolved)
                 && (q.MinSeverity == null || a.Severity >= q.MinSeverity)
                 && (string.IsNullOrEmpty(source) || a.Source == source)
                 && (string.IsNullOrEmpty(search) || a.Title.Contains(search) || a.Message.Contains(search)),
            orderBy: o => o.OrderBy(a => a.Status == AlertStatus.Resolved)
                           .ThenByDescending(a => a.Severity)
                           .ThenByDescending(a => a.LastOccurredAt),
            ct: ct);
        return result.Map(ToDto);
    }

    public async Task<AlertDto> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        ToDto(await LoadAsync(id, ct));

    public Task<AlertDto> AcknowledgeAsync(Guid id, AlertActionRequest r, CancellationToken ct = default) =>
        MutateAsync(id, a => a.Acknowledge(User(r.User), DateTime.UtcNow), ct);

    public Task<AlertDto> ResolveAsync(Guid id, AlertActionRequest r, CancellationToken ct = default) =>
        MutateAsync(id, a => a.Resolve(User(r.User), r.Notes?.Trim(), DateTime.UtcNow), ct);

    public Task<AlertDto> ReopenAsync(Guid id, CancellationToken ct = default) =>
        MutateAsync(id, a => a.Reopen(), ct);

    public Task<int> BulkAcknowledgeAsync(BulkAlertActionRequest r, CancellationToken ct = default) =>
        BulkAsync(r, a => a.Status == AlertStatus.Open, a => a.Acknowledge(User(r.User), DateTime.UtcNow), ct);

    public Task<int> BulkResolveAsync(BulkAlertActionRequest r, CancellationToken ct = default) =>
        BulkAsync(r, a => a.Status != AlertStatus.Resolved, a => a.Resolve(User(r.User), "Bulk resolved", DateTime.UtcNow), ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var alert = await LoadAsync(id, ct);
        uow.Alerts.Remove(alert);
        await uow.SaveChangesAsync(ct);
    }

    public async Task<AlertStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var weekStart = now.Date.AddDays(-6);

        var active = await uow.Alerts.ListAsync(a => a.Status != AlertStatus.Resolved, ct: ct);
        var week = await uow.Alerts.ListAsync(a => a.CreatedAt >= weekStart, ct: ct);
        var resolvedRecent = await uow.Alerts.ListAsync(a => a.Status == AlertStatus.Resolved && a.ResolvedAt != null,
            o => o.OrderByDescending(a => a.ResolvedAt), take: 500, ct: ct);
        var acknowledgedRecent = await uow.Alerts.ListAsync(a => a.AcknowledgedAt != null,
            o => o.OrderByDescending(a => a.AcknowledgedAt), take: 500, ct: ct);
        var latest = await uow.Alerts.ListAsync(a => a.Status != AlertStatus.Resolved,
            o => o.OrderByDescending(a => a.LastOccurredAt), take: 6, ct: ct);

        double? Mean(IEnumerable<double> values) => values.Any() ? Math.Round(values.Average(), 1) : null;

        return new AlertStatsDto(
            Open: active.Count(a => a.Status == AlertStatus.Open),
            Acknowledged: active.Count(a => a.Status == AlertStatus.Acknowledged),
            Resolved24h: resolvedRecent.Count(a => a.ResolvedAt >= now.AddHours(-24)),
            CriticalActive: active.Count(a => a.Severity == Severity.Critical),
            MeanTimeToAcknowledgeMinutes: Mean(acknowledgedRecent.Select(a => (a.AcknowledgedAt!.Value - a.CreatedAt).TotalMinutes)),
            MeanTimeToResolveMinutes: Mean(resolvedRecent.Select(a => (a.ResolvedAt!.Value - a.CreatedAt).TotalMinutes)),
            ActiveBySeverity: Enum.GetValues<Severity>().Reverse()
                .Select(s => new NameCount(s.ToString(), active.Count(a => a.Severity == s))).ToList(),
            ActiveBySource: active.GroupBy(a => a.Source)
                .Select(g => new NameCount(g.Key, g.Count())).OrderByDescending(x => x.Count).ToList(),
            Last7Days: Enumerable.Range(0, 7).Select(i =>
            {
                var day = weekStart.AddDays(i);
                var items = week.Where(a => a.CreatedAt >= day && a.CreatedAt < day.AddDays(1)).ToList();
                return new DailyAlertPoint(DateTime.SpecifyKind(day, DateTimeKind.Utc),
                    items.Count(a => a.Severity == Severity.Critical),
                    items.Count(a => a.Severity == Severity.High),
                    items.Count(a => a.Severity == Severity.Medium),
                    items.Count(a => a.Severity == Severity.Low),
                    items.Count(a => a.Severity == Severity.Info));
            }).ToList(),
            Latest: latest.Select(ToDto).ToList());
    }

    private async Task<AlertDto> MutateAsync(Guid id, Action<SecurityAlert> mutate, CancellationToken ct)
    {
        var alert = await LoadAsync(id, ct);
        mutate(alert);
        await uow.SaveChangesAsync(ct);
        var dto = ToDto(alert);
        await notifier.AlertUpdatedAsync(dto, ct);
        return dto;
    }

    private async Task<int> BulkAsync(BulkAlertActionRequest r, Func<SecurityAlert, bool> eligible, Action<SecurityAlert> mutate, CancellationToken ct)
    {
        var ids = r.Ids.Distinct().ToList();
        var alerts = await uow.Alerts.ListAsync(a => ids.Contains(a.Id), ct: ct);
        var targets = alerts.Where(eligible).ToList();
        foreach (var alert in targets) mutate(alert);

        // All changes are committed as a single unit of work.
        await uow.SaveChangesAsync(ct);
        foreach (var alert in targets) await notifier.AlertUpdatedAsync(ToDto(alert), ct);
        return targets.Count;
    }

    private async Task<SecurityAlert> LoadAsync(Guid id, CancellationToken ct) =>
        await uow.Alerts.GetByIdAsync(id, ct: ct) ?? throw new NotFoundException("Alert", id);

    private static string User(string? user) => string.IsNullOrWhiteSpace(user) ? DefaultUser : user.Trim();

    private static AlertDto ToDto(SecurityAlert a) => new(
        a.Id, a.Title, a.Message, a.Severity, a.Source, a.SourceReference, a.Category, a.Status,
        a.OccurrenceCount, a.CreatedAt, a.LastOccurredAt, a.AcknowledgedBy, a.AcknowledgedAt,
        a.ResolvedBy, a.ResolvedAt, a.ResolutionNotes);
}
