using Microsoft.AspNetCore.Mvc;
using SecurityManager.Firewall.Application.DTOs;
using SecurityManager.Firewall.Application.Services;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Firewall.API.Controllers;

[ApiController]
[Route("api/firewall")]
public sealed class TrafficController(ITrafficService traffic) : ControllerBase
{
    /// <summary>Evaluates a connection attempt against all active policies and records the decision.</summary>
    [HttpPost("evaluate")]
    public Task<EvaluationResultDto> Evaluate(EvaluateTrafficRequest request, CancellationToken ct) =>
        traffic.EvaluateAsync(request, ct);

    [HttpGet("traffic")]
    public Task<PagedResult<TrafficLogDto>> Logs([FromQuery] TrafficLogQuery query, CancellationToken ct) =>
        traffic.GetLogsAsync(query, ct);

    [HttpGet("stats")]
    public Task<FirewallStatsDto> Stats(CancellationToken ct) => traffic.GetStatsAsync(ct);
}
