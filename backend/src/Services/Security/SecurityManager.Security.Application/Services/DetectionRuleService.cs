using SecurityManager.Security.Application.Abstractions;
using SecurityManager.Security.Application.DTOs;
using SecurityManager.Security.Application.Mapping;
using SecurityManager.Security.Domain.Entities;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Security.Application.Services;

public interface IDetectionRuleService
{
    Task<IReadOnlyList<DetectionRuleDto>> GetAllAsync(CancellationToken ct = default);
    Task<DetectionRuleDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<DetectionRuleDto> CreateAsync(UpsertDetectionRuleRequest request, CancellationToken ct = default);
    Task<DetectionRuleDto> UpdateAsync(Guid id, UpsertDetectionRuleRequest request, CancellationToken ct = default);
    Task<DetectionRuleDto> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class DetectionRuleService(ISecurityUnitOfWork uow) : IDetectionRuleService
{
    public async Task<IReadOnlyList<DetectionRuleDto>> GetAllAsync(CancellationToken ct = default)
    {
        var rules = await uow.Rules.ListAsync(orderBy: o => o.OrderByDescending(r => r.Severity).ThenBy(r => r.Name), ct: ct);
        return rules.Select(r => r.ToDto()).ToList();
    }

    public async Task<DetectionRuleDto> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        (await LoadAsync(id, ct)).ToDto();

    public async Task<DetectionRuleDto> CreateAsync(UpsertDetectionRuleRequest request, CancellationToken ct = default)
    {
        await EnsureUniqueAsync(request.Name, null, ct);
        var rule = new DetectionRule();
        rule.Apply(request);
        await uow.Rules.AddAsync(rule, ct);
        await uow.SaveChangesAsync(ct);
        return rule.ToDto();
    }

    public async Task<DetectionRuleDto> UpdateAsync(Guid id, UpsertDetectionRuleRequest request, CancellationToken ct = default)
    {
        var rule = await LoadAsync(id, ct);
        await EnsureUniqueAsync(request.Name, id, ct);
        rule.Apply(request);
        await uow.SaveChangesAsync(ct);
        return rule.ToDto();
    }

    public async Task<DetectionRuleDto> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
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

    private async Task<DetectionRule> LoadAsync(Guid id, CancellationToken ct) =>
        await uow.Rules.GetByIdAsync(id, ct: ct) ?? throw new NotFoundException("Detection rule", id);

    private async Task EnsureUniqueAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var trimmed = name.Trim();
        if (await uow.Rules.AnyAsync(r => r.Name == trimmed && r.Id != excludeId, ct))
            throw new ConflictException($"A detection rule named '{trimmed}' already exists.");
    }
}
