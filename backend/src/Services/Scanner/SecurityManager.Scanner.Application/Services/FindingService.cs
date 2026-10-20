using SecurityManager.Scanner.Application.Abstractions;
using SecurityManager.Scanner.Application.DTOs;
using SecurityManager.Scanner.Application.Mapping;
using SecurityManager.Scanner.Domain.Enums;
using SecurityManager.Shared.Kernel.Enums;
using SecurityManager.Shared.Kernel.Exceptions;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Scanner.Application.Services;

public interface IFindingService
{
    Task<PagedResult<FindingDto>> GetPagedAsync(FindingQuery query, CancellationToken ct = default);
    Task<FindingDto> UpdateStatusAsync(Guid id, UpdateFindingStatusRequest request, CancellationToken ct = default);
    Task<ScannerStatsDto> GetStatsAsync(CancellationToken ct = default);
}

public sealed class FindingService(IScannerUnitOfWork uow) : IFindingService
{
    public async Task<PagedResult<FindingDto>> GetPagedAsync(FindingQuery q, CancellationToken ct = default)
    {
        var search = q.Search?.Trim();
        var category = q.Category?.Trim();
        var result = await uow.Findings.GetPagedAsync(
            q.Page, q.PageSize,
            f => (q.TargetId == null || f.TargetId == q.TargetId)
                 && (q.MinSeverity == null || f.Severity >= q.MinSeverity)
                 && (q.Status == null || f.Status == q.Status)
                 && (string.IsNullOrEmpty(category) || f.Category == category)
                 && (string.IsNullOrEmpty(search) || f.Title.Contains(search) || f.TargetName.Contains(search)),
            orderBy: o => o.OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Severity),
            ct: ct);
        return result.Map(f => f.ToDto());
    }

    public async Task<FindingDto> UpdateStatusAsync(Guid id, UpdateFindingStatusRequest request, CancellationToken ct = default)
    {
        var finding = await uow.Findings.GetByIdAsync(id, ct: ct) ?? throw new NotFoundException("Finding", id);
        finding.Status = request.Status;
        await uow.SaveChangesAsync(ct);
        return finding.ToDto();
    }

    public async Task<ScannerStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddHours(-24);

        var openBySeverity = new List<SeverityCount>();
        foreach (var severity in Enum.GetValues<Severity>().Reverse())
        {
            var s = severity;
            openBySeverity.Add(new SeverityCount(s.ToString(),
                await uow.Findings.CountAsync(f => f.Status == FindingStatus.Open && f.Severity == s, ct)));
        }

        var topRisk = await uow.Targets.ListAsync(t => t.RiskScore != null,
            o => o.OrderByDescending(t => t.RiskScore), take: 5, ct: ct);
        var recent = await uow.Jobs.ListAsync(orderBy: o => o.OrderByDescending(j => j.CreatedAt), take: 6, ct: ct);

        return new ScannerStatsDto(
            Targets: await uow.Targets.CountAsync(ct: ct),
            ActiveSchedules: await uow.Schedules.CountAsync(s => s.IsEnabled, ct),
            Scans24h: await uow.Jobs.CountAsync(j => j.CreatedAt >= since, ct),
            ActiveScans: await uow.Jobs.CountAsync(j => j.Status == ScanStatus.Queued || j.Status == ScanStatus.Running, ct),
            OpenFindings: openBySeverity.Sum(s => s.Count),
            CriticalOpenFindings: openBySeverity.First(s => s.Severity == nameof(Severity.Critical)).Count,
            OpenFindingsBySeverity: openBySeverity,
            TopRiskTargets: topRisk.Select(t => new RiskTargetDto(t.Id, t.Name, t.Host, t.RiskScore ?? 0, t.LastScannedAt)).ToList(),
            RecentJobs: recent.Select(j => j.ToDto()).ToList());
    }
}
