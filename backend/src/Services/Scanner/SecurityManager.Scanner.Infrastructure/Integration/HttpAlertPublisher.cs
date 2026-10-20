using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SecurityManager.Scanner.Application.Abstractions;
using SecurityManager.Shared.Kernel.Contracts;

namespace SecurityManager.Scanner.Infrastructure.Integration;

public sealed class HttpAlertPublisher(HttpClient http, ILogger<HttpAlertPublisher> logger) : IAlertPublisher
{
    public async Task PublishAsync(AlertMessage message, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/alerts", message, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Alert Service rejected alert with status {Status}", (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Alert Service unavailable: {Message}", ex.Message);
        }
    }
}
