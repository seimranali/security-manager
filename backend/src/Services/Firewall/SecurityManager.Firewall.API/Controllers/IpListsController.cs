using Microsoft.AspNetCore.Mvc;
using SecurityManager.Firewall.Application.DTOs;
using SecurityManager.Firewall.Application.Services;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Firewall.API.Controllers;

/// <summary>IP allowlist and blocklist. Enforced before firewall rules: allowlist → blocklist → policies.</summary>
[ApiController]
[Route("api/firewall/ip-lists")]
public sealed class IpListsController(IIpListService ipLists) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<IpListEntryDto>> Get([FromQuery] IpListQuery query, CancellationToken ct) =>
        ipLists.GetPagedAsync(query, ct);

    /// <summary>Which entries contain an IP, and whether it would be allowed or blocked.</summary>
    [HttpGet("check")]
    public Task<IpCheckResultDto> Check([FromQuery] string ip, CancellationToken ct) => ipLists.CheckAsync(ip, ct);

    [HttpPost]
    public async Task<ActionResult<IpListEntryDto>> Create(UpsertIpListEntryRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await ipLists.CreateAsync(request, User.Identity?.Name, ct));

    [HttpPut("{id:guid}")]
    public Task<IpListEntryDto> Update(Guid id, UpsertIpListEntryRequest request, CancellationToken ct) =>
        ipLists.UpdateAsync(id, request, ct);

    [HttpPatch("{id:guid}/enabled")]
    public Task<IpListEntryDto> SetEnabled(Guid id, [FromQuery] bool value, CancellationToken ct) =>
        ipLists.SetEnabledAsync(id, value, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await ipLists.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("bulk-delete")]
    public async Task<ActionResult<object>> BulkDelete(BulkIdsRequest request, CancellationToken ct) =>
        Ok(new { deleted = await ipLists.DeleteManyAsync(request.Ids, ct) });

    /// <summary>Adds many addresses at once (newline/comma separated, "#" comments allowed).</summary>
    [HttpPost("import")]
    public Task<ImportIpListResultDto> Import(ImportIpListRequest request, CancellationToken ct) =>
        ipLists.ImportAsync(request, User.Identity?.Name, ct);

    /// <summary>Temporary block used by automated responders (e.g. Security Service detections). Idempotent.</summary>
    [HttpPost("auto-block")]
    public Task<IpListEntryDto> AutoBlock(AutoBlockRequest request, CancellationToken ct) =>
        ipLists.AutoBlockAsync(request, User.Identity?.Name, ct);
}
