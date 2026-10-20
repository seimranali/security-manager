using SecurityManager.Scanner.Domain.Enums;
using SecurityManager.Shared.Kernel.Entities;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Scanner.Domain.Entities;

/// <summary>Recurring automated scan of a target.</summary>
public class ScanSchedule : BaseEntity
{
    public const int MinutesPerDay = 24 * 60;

    public Guid TargetId { get; set; }
    public ScanTarget? Target { get; set; }
    public string Name { get; set; } = string.Empty;
    public ScanType ScanTypes { get; set; } = ScanType.All;
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Optional time of day (UTC) for whole-day intervals (daily, every N days, weekly):
    /// runs are aligned to this time instead of drifting from the creation time.
    /// </summary>
    public TimeOnly? RunAtUtc { get; set; }

    public bool IsEnabled { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }

    public bool IsTimeAligned => RunAtUtc.HasValue && IntervalMinutes % MinutesPerDay == 0;

    public bool IsDue(DateTime now) => IsEnabled && (NextRunAt is null || NextRunAt <= now);

    public void SetRunAt(TimeOnly? runAtUtc)
    {
        if (runAtUtc.HasValue && IntervalMinutes % MinutesPerDay != 0)
            throw new BusinessRuleException("A run time can only be set for daily, multi-day or weekly schedules.");
        RunAtUtc = runAtUtc;
    }

    public void MarkRun(DateTime now)
    {
        LastRunAt = now;
        NextRunAt = IsTimeAligned
            ? now.Date.AddDays(IntervalMinutes / MinutesPerDay).Add(RunAtUtc!.Value.ToTimeSpan())
            : now.AddMinutes(IntervalMinutes);
    }

    public void Reschedule(DateTime now)
    {
        if (!IsEnabled)
        {
            NextRunAt = null;
            return;
        }

        if (IsTimeAligned)
        {
            // Next occurrence of the run time: later today, otherwise tomorrow.
            var todayAtTime = now.Date.Add(RunAtUtc!.Value.ToTimeSpan());
            NextRunAt = todayAtTime > now ? todayAtTime : todayAtTime.AddDays(1);
            return;
        }

        NextRunAt = (LastRunAt ?? now).AddMinutes(LastRunAt is null ? 1 : IntervalMinutes);
    }
}
