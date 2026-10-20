using Microsoft.AspNetCore.Mvc;
using SecurityManager.Firewall.Application.DTOs;
using SecurityManager.Firewall.Application.Services;

namespace SecurityManager.Firewall.API.Controllers;

[ApiController]
[Route("api/firewall/policies")]
public sealed class PoliciesController(IPolicyService policies) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<PolicyDto>> GetAll(CancellationToken ct) => policies.GetAllAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<PolicyDto> Get(Guid id, CancellationToken ct) => policies.GetByIdAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<PolicyDto>> Create(UpsertPolicyRequest request, CancellationToken ct)
    {
        var created = await policies.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public Task<PolicyDto> Update(Guid id, UpsertPolicyRequest request, CancellationToken ct) =>
        policies.UpdateAsync(id, request, ct);

    [HttpPatch("{id:guid}/enabled")]
    public Task<PolicyDto> SetEnabled(Guid id, [FromQuery] bool value, CancellationToken ct) =>
        policies.SetEnabledAsync(id, value, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await policies.DeleteAsync(id, ct);
        return NoContent();
    }
}
