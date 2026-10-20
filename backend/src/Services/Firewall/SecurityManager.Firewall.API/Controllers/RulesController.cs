using Microsoft.AspNetCore.Mvc;
using SecurityManager.Firewall.Application.DTOs;
using SecurityManager.Firewall.Application.Services;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Firewall.API.Controllers;

[ApiController]
[Route("api/firewall/rules")]
public sealed class RulesController(IRuleService rules) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<RuleDto>> Get([FromQuery] RuleQuery query, CancellationToken ct) =>
        rules.GetPagedAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<RuleDto> Get(Guid id, CancellationToken ct) => rules.GetByIdAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<RuleDto>> Create(UpsertRuleRequest request, CancellationToken ct)
    {
        var created = await rules.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public Task<RuleDto> Update(Guid id, UpsertRuleRequest request, CancellationToken ct) =>
        rules.UpdateAsync(id, request, ct);

    [HttpPatch("{id:guid}/enabled")]
    public Task<RuleDto> SetEnabled(Guid id, [FromQuery] bool value, CancellationToken ct) =>
        rules.SetEnabledAsync(id, value, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await rules.DeleteAsync(id, ct);
        return NoContent();
    }
}
