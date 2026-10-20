using SecurityManager.Scanner.Domain.Entities;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.UnitTests.Scanner;

public class ScanScheduleTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 14, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Daily_schedule_with_run_time_runs_later_today_or_tomorrow()
    {
        var later = Daily(new TimeOnly(18, 0));
        later.Reschedule(Now);
        Assert.Equal(new DateTime(2026, 10, 19, 18, 0, 0), later.NextRunAt);

        var earlier = Daily(new TimeOnly(2, 0));
        earlier.Reschedule(Now);
        Assert.Equal(new DateTime(2026, 10, 20, 2, 0, 0), earlier.NextRunAt);
    }

    [Fact]
    public void Aligned_schedule_stays_on_its_time_even_when_a_run_starts_late()
    {
        var weekly = Daily(new TimeOnly(2, 0));
        weekly.IntervalMinutes = 7 * ScanSchedule.MinutesPerDay;

        weekly.MarkRun(new DateTime(2026, 10, 19, 2, 0, 40)); // dispatcher picked it up 40s late
        Assert.Equal(new DateTime(2026, 10, 26, 2, 0, 0), weekly.NextRunAt);
    }

    [Fact]
    public void Interval_schedule_without_run_time_counts_from_last_run()
    {
        var hourly = new ScanSchedule { IntervalMinutes = 60 };
        hourly.MarkRun(Now);
        Assert.Equal(Now.AddHours(1), hourly.NextRunAt);
    }

    [Fact]
    public void Run_time_is_rejected_for_sub_daily_intervals()
    {
        var hourly = new ScanSchedule { IntervalMinutes = 60 };
        Assert.Throws<BusinessRuleException>(() => hourly.SetRunAt(new TimeOnly(9, 0)));
    }

    private static ScanSchedule Daily(TimeOnly runAt)
    {
        var s = new ScanSchedule { IntervalMinutes = ScanSchedule.MinutesPerDay };
        s.SetRunAt(runAt);
        return s;
    }
}
