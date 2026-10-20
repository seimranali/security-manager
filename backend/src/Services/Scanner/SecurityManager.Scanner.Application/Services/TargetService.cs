using SecurityManager.Scanner.Application.Abstractions;
using SecurityManager.Scanner.Application.DTOs;
using SecurityManager.Scanner.Application.Mapping;
using SecurityManager.Scanner.Domain.Entities;
using SecurityManager.Shared.Kernel.Exceptions;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.Scanner.Application.Services;

public interface ITargetService
{
    Task<IReadOnlyList<TargetDto>> GetAllAsync(CancellationToken ct = default);
    Task<TargetDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TargetDto> CreateAsync(UpsertTargetRequest request, CancellationToken ct = default);
    Task<TargetDto> UpdateAsync(Guid id, UpsertTargetRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class TargetService(IScannerUnitOfWork uow) : ITargetService
{
    public const int MaxPortsPerTarget = 2000;

    public async Task<IReadOnlyList<TargetDto>> GetAllAsync(CancellationToken ct = default)
    {
        var targets = await uow.Targets.ListAsync(orderBy: o => o.OrderBy(t => t.Name), ct: ct);
        return targets.Select(t => t.ToDto()).ToList();
    }

    public async Task<TargetDto> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        (await LoadAsync(id, ct)).ToDto();

    public async Task<TargetDto> CreateAsync(UpsertTargetRequest request, CancellationToken ct = default)
    {
        Validate(request);
        var target = new ScanTarget();
        Apply(target, request);
        await uow.Targets.AddAsync(target, ct);
        await uow.SaveChangesAsync(ct);
        return target.ToDto();
    }

    public async Task<TargetDto> UpdateAsync(Guid id, UpsertTargetRequest request, CancellationToken ct = default)
    {
        Validate(request);
        var target = await LoadAsync(id, ct);
        Apply(target, request);
        await uow.SaveChangesAsync(ct);
        return target.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var target = await LoadAsync(id, ct);
        uow.Targets.Remove(target);
        await uow.SaveChangesAsync(ct);
    }

    private async Task<ScanTarget> LoadAsync(Guid id, CancellationToken ct) =>
        await uow.Targets.GetByIdAsync(id, ct: ct) ?? throw new NotFoundException("Target", id);

    private static void Validate(UpsertTargetRequest r)
    {
        if (!ScanTarget.IsValidHost(r.Host.Trim()))
            throw new BusinessRuleException($"'{r.Host}' is not a valid hostname or IP address.");
        if (PortSpec.IsAny(r.Ports) || !PortSpec.IsValid(r.Ports))
            throw new BusinessRuleException("Ports must be a list such as '22,80,443' or '1-1024'.");
        if (PortSpec.Expand(r.Ports, MaxPortsPerTarget + 1).Count > MaxPortsPerTarget)
            throw new BusinessRuleException($"A target may define at most {MaxPortsPerTarget} ports.");
    }

    private static void Apply(ScanTarget t, UpsertTargetRequest r)
    {
        t.Name = r.Name.Trim();
        t.Host = r.Host.Trim().ToLowerInvariant();
        t.Ports = string.Join(',', r.Ports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        t.Description = r.Description?.Trim();
        t.IsEnabled = r.IsEnabled;
    }
}
