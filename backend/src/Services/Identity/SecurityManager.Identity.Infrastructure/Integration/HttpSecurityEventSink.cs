using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SecurityManager.Identity.Application.Abstractions;

namespace SecurityManager.Identity.Infrastructure.Integration;

/// <summary>
/// Posts authentication events to the Security service, where the detection rules ("Brute Force Attack",
/// "Targeted Account Attack") correlate them and raise alerts. Failures never block sign-in.
/// </summary>
public sealed class HttpSecurityEventSink(HttpClient http, ILogger<HttpSecurityEventSink> logger) : ISecurityEventSink
{
    public Task ReportLoginFailureAsync(string userName, string? ipAddress, string reason, CancellationToken ct = default) =>
        PostAsync(new
        {
            eventType = "AuthenticationFailure",
            severity = "Low",
            source = "Security Manager sign-in",
            message = $"Failed sign-in for '{userName}' ({reason})",
            sourceIp = ipAddress,
            userName,
            targetHost = Environment.MachineName
        }, ct);

    public Task ReportLockoutAsync(string userName, string? ipAddress, CancellationToken ct = default) =>
        PostAsync(new
        {
            eventType = "AuthenticationFailure",
            severity = "High",
            source = "Security Manager sign-in",
            message = $"Account '{userName}' locked after repeated failed sign-ins",
            sourceIp = ipAddress,
            userName,
            targetHost = Environment.MachineName
        }, ct);

    private async Task PostAsync(object body, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/security/events", body, ct);
            if (!response.IsSuccessStatusCode) logger.LogDebug("Security service rejected auth event: {Status}", (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogDebug("Security service unavailable for auth event: {Message}", ex.Message);
        }
    }
}
