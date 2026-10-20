using Microsoft.Extensions.Logging;
using SecurityManager.Scanner.Application.Abstractions;
using SecurityManager.Scanner.Domain.Entities;
using SecurityManager.Scanner.Domain.Enums;

namespace SecurityManager.Scanner.Application.Services;

public interface IScheduleDispatcher
{
    /// <summary>Creates and enqueues jobs for every schedule that is due. Returns the number of jobs queued.</summary>
    Task<int> DispatchDueAsync(DateTime now, CancellationToken ct = default);
}

public sealed class ScheduleDispatcher(IScannerUnitOfWork uow, IScanQueue queue, ILogger<ScheduleDispatcher> logger)
    : IScheduleDispatcher
{
    public async Task<int> DispatchDueAsync(DateTime now, CancellationToken ct = default)
    {
        var due = await uow.Schedules.ListAsync(
            s => s.IsEnabled && (s.NextRunAt == null || s.NextRunAt <= now),
            includes: [s => s.Target!],
            ct: ct);
        if (due.Count == 0) return 0;

        var jobs = new List<ScanJob>();
        foreach (var schedule in due)
        {
            schedule.MarkRun(now);
            if (schedule.Target is not { IsEnabled: true } target) continue;

            var targetId = target.Id;
            var busy = await uow.Jobs.AnyAsync(j => j.TargetId == targetId &&
                (j.Status == ScanStatus.Queued || j.Status == ScanStatus.Running), ct);
            if (busy) continue;

            var job = ScanJob.For(target, schedule.ScanTypes, ScanTrigger.Scheduled, schedule.Id);
            await uow.Jobs.AddAsync(job, ct);
            jobs.Add(job);
        }

        await uow.SaveChangesAsync(ct);
        foreach (var job in jobs) await queue.EnqueueAsync(job.Id, ct);

        if (jobs.Count > 0) logger.LogInformation("Dispatched {Count} scheduled scan(s)", jobs.Count);
        return jobs.Count;
    }
}
