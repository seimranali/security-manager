using Microsoft.AspNetCore.Mvc;
using SecurityManager.Security.Application.DTOs;
using SecurityManager.Security.Application.Services;

namespace SecurityManager.Security.API.Controllers;

[ApiController]
[Route("api/security/rules")]
public sealed class DetectionRulesController(IDetectionRuleService rules) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<DetectionRuleDto>> GetAll(CancellationToken ct) => rules.GetAllAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<DetectionRuleDto> Get(Guid id, CancellationToken ct) => rules.GetByIdAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<DetectionRuleDto>> Create(UpsertDetectionRuleRequest request, CancellationToken ct)
    {
        var created = await rules.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public Task<DetectionRuleDto> Update(Guid id, UpsertDetectionRuleRequest request, CancellationToken ct) =>
        rules.UpdateAsync(id, request, ct);

    [HttpPatch("{id:guid}/enabled")]
    public Task<DetectionRuleDto> SetEnabled(Guid id, [FromQuery] bool value, CancellationToken ct) =>
        rules.SetEnabledAsync(id, value, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await rules.DeleteAsync(id, ct);
        return NoContent();
    }
}
