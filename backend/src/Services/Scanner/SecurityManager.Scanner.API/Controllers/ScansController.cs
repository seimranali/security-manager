using Microsoft.AspNetCore.Mvc;
using SecurityManager.Scanner.Application.DTOs;
using SecurityManager.Scanner.Application.Services;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Scanner.API.Controllers;

[ApiController]
[Route("api/scanner/jobs")]
public sealed class ScansController(IScanJobService jobs) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ScanJobDto>> Get([FromQuery] ScanJobQuery query, CancellationToken ct) =>
        jobs.GetPagedAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<ScanJobDetailDto> Get(Guid id, CancellationToken ct) => jobs.GetByIdAsync(id, ct);

    /// <summary>Queues an on-demand scan. The scan runs asynchronously; poll the job for progress.</summary>
    [HttpPost]
    public async Task<ActionResult<ScanJobDto>> Start(StartScanRequest request, CancellationToken ct)
    {
        var job = await jobs.StartAsync(request, ct);
        return AcceptedAtAction(nameof(Get), new { id = job.Id }, job);
    }

    [HttpPost("{id:guid}/cancel")]
    public Task<ScanJobDto> Cancel(Guid id, CancellationToken ct) => jobs.CancelAsync(id, ct);
}
