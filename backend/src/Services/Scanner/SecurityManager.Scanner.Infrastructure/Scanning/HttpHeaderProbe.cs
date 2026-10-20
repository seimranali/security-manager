using SecurityManager.Scanner.Application.Abstractions;
using SecurityManager.Scanner.Domain.Models;

namespace SecurityManager.Scanner.Infrastructure.Scanning;

/// <summary>
/// Fetches a URL and returns the response headers. Certificate validation is intentionally
/// disabled here (configured on the HttpClient) because TLS validity is assessed by the TLS probe.
/// </summary>
public sealed class HttpHeaderProbe(HttpClient http) : IHttpProbe
{
    public const string ClientName = "scanner-probe";

    public async Task<HttpProbeResult> ProbeAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            var headers = response.Headers
                .Concat(response.Content.Headers)
                .GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => string.Join("\n", g.SelectMany(h => h.Value)), StringComparer.OrdinalIgnoreCase);

            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
            return new HttpProbeResult(finalUrl, (int)response.StatusCode, headers);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (ex is TaskCanceledException && ct.IsCancellationRequested) throw;
            return new HttpProbeResult(url, null, new Dictionary<string, string>(), ex.Message);
        }
    }
}
