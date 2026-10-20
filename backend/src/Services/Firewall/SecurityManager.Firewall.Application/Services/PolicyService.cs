using SecurityManager.Firewall.Application.Abstractions;
using SecurityManager.Firewall.Application.DTOs;
using SecurityManager.Firewall.Application.Mapping;
using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Firewall.Application.Services;

public interface IPolicyService
{
    Task<IReadOnlyList<PolicyDto>> GetAllAsync(CancellationToken ct = default);
    Task<PolicyDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PolicyDto> CreateAsync(UpsertPolicyRequest request, CancellationToken ct = default);
    Task<PolicyDto> UpdateAsync(Guid id, UpsertPolicyRequest request, CancellationToken ct = default);
    Task<PolicyDto> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class PolicyService(IFirewallUnitOfWork uow) : IPolicyService
{
    public async Task<IReadOnlyList<PolicyDto>> GetAllAsync(CancellationToken ct = default)
    {
        var policies = await uow.Policies.ListAsync(
            orderBy: q => q.OrderBy(p => p.Priority).ThenBy(p => p.Name),
            includes: [p => p.Rules],
            ct: ct);
        return policies.Select(p => p.ToDto()).ToList();
    }

    public async Task<PolicyDto> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        (await LoadAsync(id, ct)).ToDto();

    public async Task<PolicyDto> CreateAsync(UpsertPolicyRequest request, CancellationToken ct = default)
    {
        await EnsureUniqueNameAsync(request.Name, null, ct);

        var policy = new FirewallPolicy();
        policy.Apply(request);
        await uow.Policies.AddAsync(policy, ct);
        await uow.SaveChangesAsync(ct);
        return policy.ToDto();
    }

    public async Task<PolicyDto> UpdateAsync(Guid id, UpsertPolicyRequest request, CancellationToken ct = default)
    {
        var policy = await LoadAsync(id, ct);
        await EnsureUniqueNameAsync(request.Name, id, ct);

        policy.Apply(request);
        await uow.SaveChangesAsync(ct);
        return policy.ToDto();
    }

    public async Task<PolicyDto> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
    {
        var policy = await LoadAsync(id, ct);
        policy.IsEnabled = enabled;
        await uow.SaveChangesAsync(ct);
        return policy.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var policy = await LoadAsync(id, ct);
        // Rules are removed together with the policy in one atomic unit of work.
        uow.Rules.RemoveRange(policy.Rules);
        uow.Policies.Remove(policy);
        await uow.SaveChangesAsync(ct);
    }

    private async Task<FirewallPolicy> LoadAsync(Guid id, CancellationToken ct) =>
        await uow.Policies.GetByIdAsync(id, [p => p.Rules], ct)
        ?? throw new NotFoundException("Policy", id);

    private async Task EnsureUniqueNameAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var trimmed = name.Trim();
        if (await uow.Policies.AnyAsync(p => p.Name == trimmed && p.Id != excludeId, ct))
            throw new ConflictException($"A policy named '{trimmed}' already exists.");
    }
}
