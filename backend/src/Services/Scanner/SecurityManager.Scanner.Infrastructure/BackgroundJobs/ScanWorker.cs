using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecurityManager.Scanner.Application.Abstractions;
using SecurityManager.Scanner.Application.Services;

namespace SecurityManager.Scanner.Infrastructure.BackgroundJobs;

/// <summary>Consumes the scan queue and executes jobs with bounded parallelism.</summary>
public sealed class ScanWorker(
    IServiceScopeFactory scopeFactory,
    IScanQueue queue,
    IOptions<ScannerOptions> options,
    ILogger<ScanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = scopeFactory.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IScanJobService>().RecoverAsync(stoppingToken);
        }

        var workers = Enumerable.Range(0, Math.Max(1, options.Value.MaxParallelScans))
            .Select(_ => RunConsumerAsync(stoppingToken));
        await Task.WhenAll(workers);
    }

    private async Task RunConsumerAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var jobId in queue.DequeueAllAsync(ct))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IScanExecutor>().ExecuteAsync(jobId, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Unhandled error executing scan {JobId}", jobId);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
    }
}
