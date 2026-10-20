using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecurityManager.Firewall.Application.Services;
using SecurityManager.Shared.Web;

namespace SecurityManager.Firewall.API.Controllers;

/// <summary>Threat-intelligence and country (geo) feeds that keep the IP lists up to date automatically.</summary>
[ApiController]
[Route("api/firewall/feeds")]
public sealed class ThreatFeedsController(IThreatFeedService feeds) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ThreatFeedDto>> GetAll(CancellationToken ct) => feeds.GetAllAsync(ct);

    [HttpPost]
    public async Task<ActionResult<ThreatFeedDto>> Create(SaveThreatFeedRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await feeds.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<ThreatFeedDto> Update(Guid id, SaveThreatFeedRequest request, CancellationToken ct) => feeds.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await feeds.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Downloads the feed now and applies it.</summary>
    [HttpPost("{id:guid}/refresh")]
    public Task<FeedRefreshResultDto> Refresh(Guid id, CancellationToken ct) => feeds.RefreshAsync(id, ct);
}

/// <summary>Export and restore of the whole firewall configuration (administrators only).</summary>
[ApiController]
[Route("api/firewall/config")]
[Authorize(Policy = Authentication.AdminPolicy)]
public sealed class ConfigBackupController(IConfigBackupService backups) : ControllerBase
{
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var backup = await backups.ExportAsync(User.Identity?.Name, ct);
        Response.Headers.ContentDisposition = $"attachment; filename=\"firewall-config-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json\"";
        return Ok(backup);
    }

    /// <summary>Restores a backup; <c>replace=true</c> removes the current configuration first, otherwise merges.</summary>
    [HttpPost("import")]
    public Task<ConfigRestoreResultDto> Import(FirewallConfigBackup backup, [FromQuery] bool replace, CancellationToken ct) =>
        backups.RestoreAsync(backup, replace, ct);
}
