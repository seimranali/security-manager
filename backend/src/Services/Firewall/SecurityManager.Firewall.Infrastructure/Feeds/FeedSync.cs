using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecurityManager.Firewall.Application.Services;

namespace SecurityManager.Firewall.Infrastructure.Feeds;

/// <summary>Downloads feed bodies over HTTP(S), capped in size so a hostile or broken URL cannot exhaust memory.</summary>
public sealed class HttpFeedDownloader(HttpClient http) : IFeedDownloader
{
    private const long MaxBytes = 20 * 1024 * 1024;

    public async Task<string> DownloadAsync(string url, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Download failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
        if (response.Content.Headers.ContentLength > MaxBytes)
            throw new InvalidOperationException("The feed is larger than 20 MB.");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffered = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (buffered.Length + read > MaxBytes) throw new InvalidOperationException("The feed is larger than 20 MB.");
            buffered.Write(buffer, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(buffered.ToArray());
    }
}

/// <summary>Checks every minute for feeds whose refresh interval has elapsed.</summary>
public sealed class FeedSyncWorker(IServiceScopeFactory scopes, ILogger<FeedSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); // let the service finish starting
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var refreshed = await scope.ServiceProvider.GetRequiredService<IThreatFeedService>().RefreshDueAsync(stoppingToken);
                if (refreshed > 0) logger.LogInformation("Refreshed {Count} threat feed(s)", refreshed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Threat feed refresh failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
