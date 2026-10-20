using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecurityManager.Scanner.Application.Services;

namespace SecurityManager.Scanner.Infrastructure.BackgroundJobs;

/// <summary>Periodically dispatches due scan schedules (automated scanning).</summary>
public sealed class ScanSchedulerWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ScannerOptions> options,
    ILogger<ScanSchedulerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.SchedulerIntervalSeconds)));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IScheduleDispatcher>()
                    .DispatchDueAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Scan scheduler tick failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
