using SecurityManager.Security.Application.Abstractions;
using SecurityManager.Security.Application.DTOs;
using SecurityManager.Security.Domain.Enums;
using SecurityManager.Shared.Kernel.Enums;

namespace SecurityManager.Security.Application.Services;

public interface ISecurityStatsService
{
    Task<SecurityStatsDto> GetAsync(CancellationToken ct = default);
}

public sealed class SecurityStatsService(ISecurityUnitOfWork uow) : ISecurityStatsService
{
    public async Task<SecurityStatsDto> GetAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var since = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(-23);

        var events = await uow.Events.ListAsync(e => e.Timestamp >= since, take: 50000, ct: ct);
        var detections = await uow.Detections.ListAsync(d => d.CreatedAt >= since || d.Status == DetectionStatus.New || d.Status == DetectionStatus.Investigating, ct: ct);
        var allStatuses = new List<CountItem>();
        foreach (var status in Enum.GetValues<DetectionStatus>())
        {
            var s = status;
            allStatuses.Add(new CountItem(s.ToString(), await uow.Detections.CountAsync(d => d.Status == s, ct)));
        }

        var open = detections.Where(d => d.IsOpen).ToList();

        var timeline = Enumerable.Range(0, 24).Select(i =>
        {
            var hour = since.AddHours(i);
            var next = hour.AddHours(1);
            return new SecurityTimelinePoint(hour,
                events.Count(e => e.Timestamp >= hour && e.Timestamp < next),
                detections.Count(d => d.CreatedAt >= hour && d.CreatedAt < next));
        }).ToList();

        return new SecurityStatsDto(
            Events24h: events.Count,
            CriticalEvents24h: events.Count(e => e.Severity >= Severity.High),
            OpenDetections: open.Count,
            CriticalOpenDetections: open.Count(d => d.Severity == Severity.Critical),
            ActiveRules: await uow.Rules.CountAsync(r => r.IsEnabled, ct),
            EventsBySeverity: Enum.GetValues<Severity>()
                .Select(s => new CountItem(s.ToString(), events.Count(e => e.Severity == s))).ToList(),
            EventsByType: events.GroupBy(e => e.EventType)
                .Select(g => new CountItem(g.Key.ToString(), g.Count()))
                .OrderByDescending(c => c.Count).ToList(),
            DetectionsByStatus: allStatuses,
            TopSources: events.Where(e => !string.IsNullOrEmpty(e.SourceIp))
                .GroupBy(e => e.SourceIp!)
                .Select(g => new CountItem(g.Key, g.Count()))
                .OrderByDescending(c => c.Count).Take(6).ToList(),
            Timeline: timeline);
    }
}
