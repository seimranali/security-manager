using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecurityManager.Firewall.Application.Services;
using SecurityManager.Shared.Kernel.Contracts;
using SecurityManager.Shared.Kernel.Security;

namespace SecurityManager.Firewall.API.Controllers;

/// <summary>Host enforcement: desired rule set for agents, agent status reports, and settings.</summary>
[ApiController]
[Route("api/firewall/enforcement")]
public sealed class EnforcementController(IEnforcementService enforcement) : ControllerBase
{
    /// <summary>The rule set every agent should converge to (polled by agents).</summary>
    [HttpGet("desired-state")]
    [Authorize(Roles = $"{Roles.Service},{Roles.Admin}")]
    public Task<EnforcementDesiredState> DesiredState(CancellationToken ct) => enforcement.GetDesiredStateAsync(ct);

    [HttpPost("agents/report")]
    [Authorize(Roles = $"{Roles.Service},{Roles.Admin}")]
    public async Task<IActionResult> Report(EnforcementAgentReport report, CancellationToken ct)
    {
        await enforcement.ReportAsync(report, ct);
        return NoContent();
    }

    /// <summary>Settings, agents and a preview of the rules that will be applied.</summary>
    [HttpGet]
    public Task<EnforcementOverviewDto> Overview(CancellationToken ct) => enforcement.GetOverviewAsync(ct);

    [HttpPut("settings")]
    [Authorize(Roles = Roles.Admin)]
    public Task<EnforcementSettingsDto> UpdateSettings(EnforcementSettingsDto settings, CancellationToken ct) =>
        enforcement.UpdateSettingsAsync(settings, ct);

    [HttpDelete("agents/{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> DeleteAgent(Guid id, CancellationToken ct)
    {
        await enforcement.DeleteAgentAsync(id, ct);
        return NoContent();
    }
}
