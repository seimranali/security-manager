using System.Text.Json;
using SecurityManager.Firewall.Application.Abstractions;
using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Firewall.Domain.Services;
using SecurityManager.Shared.Kernel.Contracts;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Firewall.Application.Services;

public sealed record EnforcementSettingsDto(bool Enabled, bool EnforceBlocklist, bool EnforceAllowlist, DateTime? UpdatedAt);

public sealed record EnforcementAgentDto(
    Guid Id, string AgentId, string Hostname, string OperatingSystem, string AgentVersion, string Mode, string Status,
    bool IsOnline, bool InSync, string? AppliedVersion, int ManagedRuleCount, int LastCreated, int LastUpdated, int LastRemoved,
    int IntervalSeconds, DateTime LastSeenAt, DateTime? LastChangeAt,
    IReadOnlyList<AgentFirewallProfile> Profiles, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public sealed record EnforcementOverviewDto(
    EnforcementSettingsDto Settings, EnforcementDesiredState DesiredState, IReadOnlyList<EnforcementAgentDto> Agents,
    IReadOnlyList<string> EnforcedPolicies);

public interface IEnforcementService
{
    Task<EnforcementDesiredState> GetDesiredStateAsync(CancellationToken ct = default);
    Task ReportAsync(EnforcementAgentReport report, CancellationToken ct = default);
    Task<EnforcementOverviewDto> GetOverviewAsync(CancellationToken ct = default);
    Task<EnforcementSettingsDto> UpdateSettingsAsync(EnforcementSettingsDto settings, CancellationToken ct = default);
    Task DeleteAgentAsync(Guid id, CancellationToken ct = default);
}

public sealed class EnforcementService(IFirewallUnitOfWork uow) : IEnforcementService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<EnforcementDesiredState> GetDesiredStateAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var settings = await GetSettingsAsync(ct);
        var policies = await uow.Policies.ListAsync(p => p.IsEnabled, includes: [p => p.Rules], ct: ct);
        var ipList = await uow.IpList.ListAsync(e => e.IsEnabled && (e.ExpiresAt == null || e.ExpiresAt > now), ct: ct);
        return EnforcementPlanner.Plan(policies, ipList, settings, now);
    }

    public async Task ReportAsync(EnforcementAgentReport r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.AgentId) || r.AgentId.Length > 100) throw new BusinessRuleException("Agent id is required (max 100 characters).");
        var now = DateTime.UtcNow;
        var agentId = r.AgentId.Trim();
        var agent = await uow.EnforcementAgents.FirstOrDefaultAsync(a => a.AgentId == agentId, ct: ct);
        if (agent is null)
        {
            agent = new EnforcementAgent { AgentId = agentId };
            await uow.EnforcementAgents.AddAsync(agent, ct);
        }

        agent.Hostname = Trim(r.Hostname, 253);
        agent.OperatingSystem = Trim(r.OperatingSystem, 200);
        agent.AgentVersion = Trim(r.AgentVersion, 50);
        agent.Mode = Trim(r.Mode, 20);
        agent.Status = Trim(r.Status, 20);
        agent.AppliedVersion = r.AppliedVersion is null ? null : Trim(r.AppliedVersion, 64);
        agent.ManagedRuleCount = r.ManagedRuleCount;
        agent.LastCreated = r.Created;
        agent.LastUpdated = r.Updated;
        agent.LastRemoved = r.Removed;
        agent.IntervalSeconds = Math.Clamp(r.IntervalSeconds, 5, 3600);
        agent.LastSeenAt = now;
        if (r.Created + r.Updated + r.Removed > 0) agent.LastChangeAt = now;
        agent.ProfilesJson = JsonSerializer.Serialize(r.Profiles.Take(10), Json);
        agent.ErrorsJson = JsonSerializer.Serialize(r.Errors.Take(50).Select(e => Trim(e, 500)), Json);
        agent.WarningsJson = JsonSerializer.Serialize(r.Warnings.Take(50).Select(e => Trim(e, 500)), Json);
        await uow.SaveChangesAsync(ct);
    }

    public async Task<EnforcementOverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var desired = await GetDesiredStateAsync(ct);
        var settings = await GetSettingsAsync(ct);
        var agents = await uow.EnforcementAgents.ListAsync(orderBy: o => o.OrderBy(a => a.Hostname), ct: ct);
        var policies = await uow.Policies.ListAsync(p => p.IsEnabled, o => o.OrderBy(p => p.Priority), ct: ct);

        return new EnforcementOverviewDto(
            ToDto(settings), desired,
            agents.Select(a => new EnforcementAgentDto(
                a.Id, a.AgentId, a.Hostname, a.OperatingSystem, a.AgentVersion, a.Mode, a.Status, a.IsOnline(now),
                a.AppliedVersion == desired.Version && a.Status == "InSync", a.AppliedVersion, a.ManagedRuleCount,
                a.LastCreated, a.LastUpdated, a.LastRemoved, a.IntervalSeconds, a.LastSeenAt, a.LastChangeAt,
                Deserialize<AgentFirewallProfile>(a.ProfilesJson), Deserialize<string>(a.ErrorsJson), Deserialize<string>(a.WarningsJson))).ToList(),
            policies.Select(p => p.IsEnabled ? p.Name : $"{p.Name} (policy disabled)").ToList());
    }

    public async Task<EnforcementSettingsDto> UpdateSettingsAsync(EnforcementSettingsDto dto, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        settings.Enabled = dto.Enabled;
        settings.EnforceBlocklist = dto.EnforceBlocklist;
        settings.EnforceAllowlist = dto.EnforceAllowlist;
        await uow.SaveChangesAsync(ct);
        return ToDto(settings);
    }

    public async Task DeleteAgentAsync(Guid id, CancellationToken ct = default)
    {
        var agent = await uow.EnforcementAgents.GetByIdAsync(id, ct: ct) ?? throw new NotFoundException("Agent", id);
        uow.EnforcementAgents.Remove(agent);
        await uow.SaveChangesAsync(ct);
    }

    private async Task<EnforcementSettings> GetSettingsAsync(CancellationToken ct)
    {
        var settings = await uow.EnforcementSettings.FirstOrDefaultAsync(_ => true, ct: ct);
        if (settings is not null) return settings;
        settings = new EnforcementSettings();
        await uow.EnforcementSettings.AddAsync(settings, ct);
        await uow.SaveChangesAsync(ct);
        return settings;
    }

    private static EnforcementSettingsDto ToDto(EnforcementSettings s) => new(s.Enabled, s.EnforceBlocklist, s.EnforceAllowlist, s.UpdatedAt);

    private static IReadOnlyList<T> Deserialize<T>(string json)
    {
        try { return JsonSerializer.Deserialize<List<T>>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string Trim(string? value, int max) => (value ?? string.Empty).Length <= max ? value ?? string.Empty : value![..max];
}
