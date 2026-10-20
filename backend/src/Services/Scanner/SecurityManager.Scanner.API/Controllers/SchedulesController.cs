using Microsoft.AspNetCore.Mvc;
using SecurityManager.Scanner.Application.DTOs;
using SecurityManager.Scanner.Application.Services;

namespace SecurityManager.Scanner.API.Controllers;

[ApiController]
[Route("api/scanner/schedules")]
public sealed class SchedulesController(IScheduleService schedules) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ScheduleDto>> GetAll(CancellationToken ct) => schedules.GetAllAsync(ct);

    [HttpPost]
    public async Task<ActionResult<ScheduleDto>> Create(UpsertScheduleRequest request, CancellationToken ct)
    {
        var created = await schedules.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("{id:guid}")]
    public Task<ScheduleDto> Update(Guid id, UpsertScheduleRequest request, CancellationToken ct) =>
        schedules.UpdateAsync(id, request, ct);

    [HttpPatch("{id:guid}/enabled")]
    public Task<ScheduleDto> SetEnabled(Guid id, [FromQuery] bool value, CancellationToken ct) =>
        schedules.SetEnabledAsync(id, value, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await schedules.DeleteAsync(id, ct);
        return NoContent();
    }
}
