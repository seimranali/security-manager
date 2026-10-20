using SecurityManager.Firewall.Application.Abstractions;
using SecurityManager.Firewall.Application.DTOs;
using SecurityManager.Firewall.Application.Mapping;
using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Firewall.Domain.Services;
using SecurityManager.Shared.Kernel.Exceptions;
using SecurityManager.Shared.Kernel.Models;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.Firewall.Application.Services;

public interface IRuleService
{
    Task<PagedResult<RuleDto>> GetPagedAsync(RuleQuery query, CancellationToken ct = default);
    Task<RuleDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RuleDto> CreateAsync(UpsertRuleRequest request, CancellationToken ct = default);
    Task<RuleDto> UpdateAsync(Guid id, UpsertRuleRequest request, CancellationToken ct = default);
    Task<RuleDto> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class RuleService(IFirewallUnitOfWork uow) : IRuleService
{
    public async Task<PagedResult<RuleDto>> GetPagedAsync(RuleQuery q, CancellationToken ct = default)
    {
        var search = q.Search?.Trim();
        var result = await uow.Rules.GetPagedAsync(
            q.Page, q.PageSize,
            r => (q.PolicyId == null || r.PolicyId == q.PolicyId)
                 && (q.Action == null || r.Action == q.Action)
                 && (q.IsEnabled == null || r.IsEnabled == q.IsEnabled)
                 && (string.IsNullOrEmpty(search)
                     || r.Name.Contains(search)
                     || r.SourceAddress.Contains(search)
                     || r.DestinationAddress.Contains(search)
                     || r.DestinationPort.Contains(search)),
            orderBy: o => o.OrderBy(r => r.Policy!.Priority).ThenBy(r => r.Priority).ThenBy(r => r.Name),
            includes: [r => r.Policy!],
            ct: ct);
        return result.Map(r => r.ToDto());
    }

    public async Task<RuleDto> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        (await LoadAsync(id, ct)).ToDto();

    public async Task<RuleDto> CreateAsync(UpsertRuleRequest request, CancellationToken ct = default)
    {
        Validate(request);
        var policy = await uow.Policies.GetByIdAsync(request.PolicyId, ct: ct)
                     ?? throw new BusinessRuleException($"Policy '{request.PolicyId}' does not exist.");

        var rule = new FirewallRule { Policy = policy };
        rule.Apply(request);
        await uow.Rules.AddAsync(rule, ct);
        await uow.SaveChangesAsync(ct);
        return rule.ToDto();
    }

    public async Task<RuleDto> UpdateAsync(Guid id, UpsertRuleRequest request, CancellationToken ct = default)
    {
        Validate(request);
        var rule = await LoadAsync(id, ct);

        if (rule.PolicyId != request.PolicyId)
        {
            rule.Policy = await uow.Policies.GetByIdAsync(request.PolicyId, ct: ct)
                          ?? throw new BusinessRuleException($"Policy '{request.PolicyId}' does not exist.");
        }

        rule.Apply(request);
        await uow.SaveChangesAsync(ct);
        return rule.ToDto();
    }

    public async Task<RuleDto> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
    {
        var rule = await LoadAsync(id, ct);
        rule.IsEnabled = enabled;
        await uow.SaveChangesAsync(ct);
        return rule.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var rule = await LoadAsync(id, ct);
        uow.Rules.Remove(rule);
        await uow.SaveChangesAsync(ct);
    }

    private async Task<FirewallRule> LoadAsync(Guid id, CancellationToken ct) =>
        await uow.Rules.GetByIdAsync(id, [r => r.Policy!], ct)
        ?? throw new NotFoundException("Rule", id);

    private static void Validate(UpsertRuleRequest r)
    {
        var errors = new List<string>();
        if (!AddressMatcher.IsValid(r.SourceAddress)) errors.Add($"Invalid source address '{r.SourceAddress}'.");
        if (!AddressMatcher.IsValid(r.DestinationAddress)) errors.Add($"Invalid destination address '{r.DestinationAddress}'.");
        if (!PortSpec.IsValid(r.SourcePort)) errors.Add($"Invalid source port '{r.SourcePort}'.");
        if (!PortSpec.IsValid(r.DestinationPort)) errors.Add($"Invalid destination port '{r.DestinationPort}'.");
        if (errors.Count > 0) throw new BusinessRuleException(string.Join(" ", errors));
    }
}
