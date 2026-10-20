using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SecurityManager.Shared.Kernel.Contracts;

namespace SecurityManager.Agent.Sync;

/// <summary>Talks to the Firewall service with a short-lived service token (see ServiceTokenHandler).</summary>
public sealed class EnforcementClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task<EnforcementDesiredState> GetDesiredStateAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<EnforcementDesiredState>("api/firewall/enforcement/desired-state", Json, ct)
        ?? throw new InvalidOperationException("Empty desired state.");

    public async Task ReportAsync(EnforcementAgentReport report, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("api/firewall/enforcement/agents/report", report, Json, ct);
        response.EnsureSuccessStatusCode();
    }
}
