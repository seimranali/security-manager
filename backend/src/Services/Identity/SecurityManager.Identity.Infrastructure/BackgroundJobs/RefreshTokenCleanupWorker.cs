using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecurityManager.Identity.Infrastructure.Persistence;

namespace SecurityManager.Identity.Infrastructure.BackgroundJobs;

/// <summary>Purges refresh tokens that expired or were revoked more than 30 days ago.</summary>
public sealed class RefreshTokenCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<RefreshTokenCleanupWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var cutoff = DateTime.UtcNow - Retention;
                var deleted = await db.RefreshTokens
                    .Where(t => t.ExpiresAt < cutoff || (t.RevokedAt != null && t.RevokedAt < cutoff))
                    .ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0) logger.LogInformation("Purged {Count} stale refresh token(s)", deleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Refresh token cleanup failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
