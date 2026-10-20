using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SecurityManager.Security.Application.Abstractions;

namespace SecurityManager.Security.Infrastructure.Integration;

/// <summary>Calls the Firewall Service auto-block endpoint. Failures are logged and never break detection.</summary>
public sealed class HttpFirewallBlocker(HttpClient http, ILogger<HttpFirewallBlocker> logger) : IFirewallBlocker
{
    public async Task BlockAsync(string ipAddress, string reason, TimeSpan duration, string sourceReference, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/firewall/ip-lists/auto-block", new
            {
                address = ipAddress,
                reason,
                durationMinutes = (int)Math.Clamp(duration.TotalMinutes, 1, 43200),
                sourceReference
            }, ct);

            if (response.IsSuccessStatusCode)
                logger.LogWarning("Auto-blocked {Ip} for {Minutes} min ({Reason})", ipAddress, (int)duration.TotalMinutes, reason);
            else
                logger.LogWarning("Firewall refused auto-block of {Ip}: {Status}", ipAddress, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Firewall Service unavailable for auto-block of {Ip}: {Message}", ipAddress, ex.Message);
        }
    }
}
