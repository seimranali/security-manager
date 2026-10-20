namespace SecurityManager.Scanner.Infrastructure;

public sealed class ScannerOptions
{
    public const string Section = "Scanner";

    /// <summary>Concurrent TCP connection attempts per scan.</summary>
    public int MaxConcurrentProbes { get; set; } = 100;

    public int ConnectTimeoutMs { get; set; } = 1000;

    public int BannerTimeoutMs { get; set; } = 700;

    public int HttpTimeoutSeconds { get; set; } = 10;

    /// <summary>Number of scan jobs executed in parallel.</summary>
    public int MaxParallelScans { get; set; } = 2;

    /// <summary>How often the scheduler checks for due schedules.</summary>
    public int SchedulerIntervalSeconds { get; set; } = 30;
}
