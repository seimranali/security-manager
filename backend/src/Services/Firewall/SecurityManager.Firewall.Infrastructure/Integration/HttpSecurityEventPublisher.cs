using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SecurityManager.Firewall.Application.Abstractions;
using SecurityManager.Shared.Kernel.Contracts;

namespace SecurityManager.Firewall.Infrastructure.Integration;

/// <summary>
/// Sends events to the Security Service. Failures are logged but never break firewall evaluation.
/// </summary>
public sealed class HttpSecurityEventPublisher(HttpClient http, ILogger<HttpSecurityEventPublisher> logger)
    : ISecurityEventPublisher
{
    public async Task PublishAsync(SecurityEventMessage message, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/security/events", message, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Security Service rejected event with status {Status}", (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Security Service unavailable: {Message}", ex.Message);
        }
    }
}
