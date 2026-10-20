using Microsoft.AspNetCore.Mvc;
using SecurityManager.Scanner.Application.DTOs;
using SecurityManager.Scanner.Application.Services;

namespace SecurityManager.Scanner.API.Controllers;

[ApiController]
[Route("api/scanner/targets")]
public sealed class TargetsController(ITargetService targets) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TargetDto>> GetAll(CancellationToken ct) => targets.GetAllAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<TargetDto> Get(Guid id, CancellationToken ct) => targets.GetByIdAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<TargetDto>> Create(UpsertTargetRequest request, CancellationToken ct)
    {
        var created = await targets.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public Task<TargetDto> Update(Guid id, UpsertTargetRequest request, CancellationToken ct) =>
        targets.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await targets.DeleteAsync(id, ct);
        return NoContent();
    }
}
