using Microsoft.AspNetCore.Mvc;
using SecurityManager.Security.Application.DTOs;
using SecurityManager.Security.Application.Services;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Security.API.Controllers;

[ApiController]
[Route("api/security/events")]
public sealed class EventsController(IEventService events) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<SecurityEventDto>> Get([FromQuery] EventQuery query, CancellationToken ct) =>
        events.GetPagedAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<SecurityEventDto> Get(Guid id, CancellationToken ct) => events.GetByIdAsync(id, ct);

    /// <summary>Searchable, paged distinct values of an event field (sourceIp, userName, targetHost, source).</summary>
    [HttpGet("lookup/{field}")]
    public Task<PagedResult<ValueCount<string>>> Lookup(string field, [FromQuery] LookupQuery query, CancellationToken ct) =>
        events.LookupAsync(field, query, ct);

    /// <summary>Ingests an event and runs it through the detection engine.</summary>
    [HttpPost]
    public async Task<ActionResult<IngestResultDto>> Ingest(CreateEventRequest request, CancellationToken ct)
    {
        var result = await events.IngestAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Event.Id }, result);
    }
}
