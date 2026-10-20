using Microsoft.AspNetCore.Mvc;
using SecurityManager.Alert.Application.DTOs;
using SecurityManager.Alert.Application.Services;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Alert.API.Controllers;

[ApiController]
[Route("api/alerts")]
public sealed class AlertsController(IAlertService alerts) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AlertDto>> Get([FromQuery] AlertQuery query, CancellationToken ct) =>
        alerts.GetPagedAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<AlertDto> Get(Guid id, CancellationToken ct) => alerts.GetByIdAsync(id, ct);

    [HttpGet("stats")]
    public Task<AlertStatsDto> Stats(CancellationToken ct) => alerts.GetStatsAsync(ct);

    /// <summary>Raises an alert (used by other services and for manual alerts). Duplicates are merged.</summary>
    [HttpPost]
    public async Task<ActionResult<AlertDto>> Create(CreateAlertRequest request, CancellationToken ct)
    {
        var alert = await alerts.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = alert.Id }, alert);
    }

    [HttpPost("{id:guid}/acknowledge")]
    public Task<AlertDto> Acknowledge(Guid id, AlertActionRequest request, CancellationToken ct) =>
        alerts.AcknowledgeAsync(id, request, ct);

    [HttpPost("{id:guid}/resolve")]
    public Task<AlertDto> Resolve(Guid id, AlertActionRequest request, CancellationToken ct) =>
        alerts.ResolveAsync(id, request, ct);

    [HttpPost("{id:guid}/reopen")]
    public Task<AlertDto> Reopen(Guid id, CancellationToken ct) => alerts.ReopenAsync(id, ct);

    [HttpPost("bulk/acknowledge")]
    public async Task<ActionResult<object>> BulkAcknowledge(BulkAlertActionRequest request, CancellationToken ct) =>
        Ok(new { updated = await alerts.BulkAcknowledgeAsync(request, ct) });

    [HttpPost("bulk/resolve")]
    public async Task<ActionResult<object>> BulkResolve(BulkAlertActionRequest request, CancellationToken ct) =>
        Ok(new { updated = await alerts.BulkResolveAsync(request, ct) });

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await alerts.DeleteAsync(id, ct);
        return NoContent();
    }
}
