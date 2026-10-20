using Microsoft.AspNetCore.Mvc;
using SecurityManager.Security.Application.DTOs;
using SecurityManager.Security.Application.Services;

namespace SecurityManager.Security.API.Controllers;

[ApiController]
[Route("api/security/stats")]
public sealed class StatsController(ISecurityStatsService stats) : ControllerBase
{
    [HttpGet]
    public Task<SecurityStatsDto> Get(CancellationToken ct) => stats.GetAsync(ct);
}
