using Microsoft.AspNetCore.Mvc;
using SecurityManager.Scanner.Application.DTOs;
using SecurityManager.Scanner.Application.Services;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Scanner.API.Controllers;

[ApiController]
[Route("api/scanner")]
public sealed class FindingsController(IFindingService findings) : ControllerBase
{
    [HttpGet("findings")]
    public Task<PagedResult<FindingDto>> Get([FromQuery] FindingQuery query, CancellationToken ct) =>
        findings.GetPagedAsync(query, ct);

    [HttpPut("findings/{id:guid}/status")]
    public Task<FindingDto> UpdateStatus(Guid id, UpdateFindingStatusRequest request, CancellationToken ct) =>
        findings.UpdateStatusAsync(id, request, ct);

    [HttpGet("stats")]
    public Task<ScannerStatsDto> Stats(CancellationToken ct) => findings.GetStatsAsync(ct);
}
