using SecurityManager.Scanner.Application.Abstractions;
using SecurityManager.Scanner.Application.DTOs;
using SecurityManager.Scanner.Application.Mapping;
using SecurityManager.Scanner.Domain.Entities;
using SecurityManager.Scanner.Domain.Enums;
using SecurityManager.Shared.Kernel.Exceptions;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Scanner.Application.Services;

public interface IScanJobService
{
    Task<ScanJobDto> StartAsync(StartScanRequest request, CancellationToken ct = default);
    Task<PagedResult<ScanJobDto>> GetPagedAsync(ScanJobQuery query, CancellationToken ct = default);
    Task<ScanJobDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ScanJobDto> CancelAsync(Guid id, CancellationToken ct = default);

    /// <summary>Marks scans interrupted by a restart as failed and re-queues pending ones.</summary>
    Task RecoverAsync(CancellationToken ct = default);
}

public sealed class ScanJobService(IScannerUnitOfWork uow, IScanQueue queue) : IScanJobService
{
    public async Task<ScanJobDto> StartAsync(StartScanRequest request, CancellationToken ct = default)
    {
        var target = await uow.Targets.GetByIdAsync(request.TargetId, ct: ct)
                     ?? throw new NotFoundException("Target", request.TargetId);
        if (!target.IsEnabled) throw new BusinessRuleException($"Target '{target.Name}' is disabled.");

        var targetId = target.Id;
        if (await uow.Jobs.AnyAsync(j => j.TargetId == targetId && (j.Status == ScanStatus.Queued || j.Status == ScanStatus.Running), ct))
            throw new ConflictException($"A scan for '{target.Name}' is already in progress.");

        var job = ScanJob.For(target, request.ScanTypes, ScanTrigger.Manual);
        await uow.Jobs.AddAsync(job, ct);
        await uow.SaveChangesAsync(ct);
        await queue.EnqueueAsync(job.Id, ct);
        return job.ToDto();
    }

    public async Task<PagedResult<ScanJobDto>> GetPagedAsync(ScanJobQuery q, CancellationToken ct = default)
    {
        var result = await uow.Jobs.GetPagedAsync(
            q.Page, q.PageSize,
            j => (q.TargetId == null || j.TargetId == q.TargetId) && (q.Status == null || j.Status == q.Status),
            orderBy: o => o.OrderByDescending(j => j.CreatedAt),
            ct: ct);
        return result.Map(j => j.ToDto());
    }

    public async Task<ScanJobDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var job = await uow.Jobs.GetByIdAsync(id, [j => j.Findings], ct) ?? throw new NotFoundException("Scan", id);
        var findings = job.Findings
            .OrderByDescending(f => f.Severity).ThenBy(f => f.Port).ThenBy(f => f.Title)
            .Select(f => f.ToDto()).ToList();
        return new ScanJobDetailDto(job.ToDto(), findings);
    }

    public async Task<ScanJobDto> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var job = await uow.Jobs.GetByIdAsync(id, ct: ct) ?? throw new NotFoundException("Scan", id);
        job.Cancel(DateTime.UtcNow);
        await uow.SaveChangesAsync(ct);
        return job.ToDto();
    }

    public async Task RecoverAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var interrupted = await uow.Jobs.ListAsync(j => j.Status == ScanStatus.Running, ct: ct);
        foreach (var job in interrupted) job.Fail("Scan interrupted by service restart.", now);

        var pending = await uow.Jobs.ListAsync(j => j.Status == ScanStatus.Queued, o => o.OrderBy(j => j.CreatedAt), ct: ct);
        await uow.SaveChangesAsync(ct);

        foreach (var job in pending) await queue.EnqueueAsync(job.Id, ct);
    }
}
