using System.Net;
using SecurityManager.Firewall.Application.Abstractions;
using SecurityManager.Firewall.Application.DTOs;
using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Firewall.Domain.Services;
using SecurityManager.Firewall.Domain.ValueObjects;
using SecurityManager.Shared.Kernel.Exceptions;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Firewall.Application.Services;

public interface IIpListService
{
    Task<PagedResult<IpListEntryDto>> GetPagedAsync(IpListQuery query, CancellationToken ct = default);
    Task<IpListEntryDto> CreateAsync(UpsertIpListEntryRequest request, string? actor, CancellationToken ct = default);
    Task<IpListEntryDto> UpdateAsync(Guid id, UpsertIpListEntryRequest request, CancellationToken ct = default);
    Task<IpListEntryDto> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<int> DeleteManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<ImportIpListResultDto> ImportAsync(ImportIpListRequest request, string? actor, CancellationToken ct = default);
    /// <summary>Blocks an address temporarily, or extends an existing block (idempotent).</summary>
    Task<IpListEntryDto> AutoBlockAsync(AutoBlockRequest request, string? actor, CancellationToken ct = default);
    Task<IpCheckResultDto> CheckAsync(string ip, CancellationToken ct = default);
}

public sealed class IpListService(IFirewallUnitOfWork uow) : IIpListService
{
    private const int MaxImportEntries = 5000;

    public async Task<PagedResult<IpListEntryDto>> GetPagedAsync(IpListQuery q, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var search = q.Search?.Trim().ToLowerInvariant();
        var activeOnly = q.ActiveOnly == true;
        var result = await uow.IpList.GetPagedAsync(
            q.Page, q.PageSize,
            e => (q.ListType == null || e.ListType == q.ListType)
                 && (!activeOnly || (e.IsEnabled && (e.ExpiresAt == null || e.ExpiresAt > now)))
                 && (string.IsNullOrEmpty(search) || e.Address.Contains(search) || (e.Description != null && e.Description.Contains(search))),
            orderBy: o => o.OrderByDescending(e => e.CreatedAt),
            ct: ct);
        return result.Map(e => ToDto(e, now));
    }

    public async Task<IpListEntryDto> CreateAsync(UpsertIpListEntryRequest r, string? actor, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var address = await ValidateAsync(r.Address, r.ListType, r.ExpiresAt, null, now, ct);
        var entry = new IpListEntry
        {
            ListType = r.ListType,
            Address = address,
            Description = Clean(r.Description),
            ExpiresAt = r.ExpiresAt?.ToUniversalTime(),
            IsEnabled = r.IsEnabled,
            Source = IpListSource.Manual,
            CreatedBy = actor
        };
        await uow.IpList.AddAsync(entry, ct);
        await uow.SaveChangesAsync(ct);
        return ToDto(entry, now);
    }

    public async Task<IpListEntryDto> UpdateAsync(Guid id, UpsertIpListEntryRequest r, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var entry = await LoadAsync(id, ct);
        entry.Address = await ValidateAsync(r.Address, r.ListType, r.ExpiresAt, id, now, ct);
        entry.ListType = r.ListType;
        entry.Description = Clean(r.Description);
        entry.ExpiresAt = r.ExpiresAt?.ToUniversalTime();
        entry.IsEnabled = r.IsEnabled;
        await uow.SaveChangesAsync(ct);
        return ToDto(entry, now);
    }

    public async Task<IpListEntryDto> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
    {
        var entry = await LoadAsync(id, ct);
        entry.IsEnabled = enabled;
        await uow.SaveChangesAsync(ct);
        return ToDto(entry, DateTime.UtcNow);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        uow.IpList.Remove(await LoadAsync(id, ct));
        await uow.SaveChangesAsync(ct);
    }

    public async Task<int> DeleteManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        var entries = await uow.IpList.ListAsync(e => ids.Contains(e.Id), ct: ct);
        uow.IpList.RemoveRange(entries);
        await uow.SaveChangesAsync(ct); // one unit of work
        return entries.Count;
    }

    public async Task<ImportIpListResultDto> ImportAsync(ImportIpListRequest r, string? actor, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (r.ExpiresAt is { } exp && exp.ToUniversalTime() <= now) throw new BusinessRuleException("Expiry must be in the future.");

        var tokens = r.Addresses
            .Split('\n')
            .Select(line => line.Split('#')[0]) // strip comments
            .SelectMany(line => line.Split([',', ';', ' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries))
            .Select(IpListEntry.Normalize)
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();
        if (tokens.Count > MaxImportEntries) throw new BusinessRuleException($"Import at most {MaxImportEntries} addresses at a time.");

        var invalid = new List<string>();
        var valid = new List<string>();
        foreach (var token in tokens)
        {
            if (IpListEntry.Validate(token, r.ListType) is null) valid.Add(token);
            else invalid.Add(token);
        }

        var existing = (await uow.IpList.ListAsync(e => valid.Contains(e.Address), ct: ct)).Select(e => e.Address).ToHashSet();
        var toAdd = valid.Where(a => !existing.Contains(a)).ToList();
        await uow.IpList.AddRangeAsync(toAdd.Select(a => new IpListEntry
        {
            ListType = r.ListType,
            Address = a,
            Description = Clean(r.Description),
            ExpiresAt = r.ExpiresAt?.ToUniversalTime(),
            Source = IpListSource.Import,
            CreatedBy = actor
        }), ct);
        await uow.SaveChangesAsync(ct);

        return new ImportIpListResultDto(toAdd.Count, valid.Count - toAdd.Count, invalid.Take(100).ToList());
    }

    public async Task<IpListEntryDto> AutoBlockAsync(AutoBlockRequest r, string? actor, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var address = IpListEntry.Normalize(r.Address);
        if (IpListEntry.Validate(address, IpListType.Block) is { } error) throw new BusinessRuleException(error);
        var expiresAt = now.AddMinutes(r.DurationMinutes);

        var allowlisted = await uow.IpList.AnyAsync(e => e.ListType == IpListType.Allow && e.Address == address, ct);
        if (allowlisted) throw new ConflictException($"{address} is on the allowlist and will not be auto-blocked.");

        var entry = await uow.IpList.FirstOrDefaultAsync(e => e.ListType == IpListType.Block && e.Address == address, ct: ct);
        if (entry is null)
        {
            entry = new IpListEntry
            {
                ListType = IpListType.Block,
                Address = address,
                Description = Clean(r.Reason),
                Source = IpListSource.Detection,
                SourceReference = r.SourceReference,
                ExpiresAt = expiresAt,
                CreatedBy = actor
            };
            await uow.IpList.AddAsync(entry, ct);
        }
        else
        {
            entry.ExtendUntil(expiresAt, now);
            entry.SourceReference = r.SourceReference ?? entry.SourceReference;
        }

        await uow.SaveChangesAsync(ct);
        return ToDto(entry, now);
    }

    public async Task<IpCheckResultDto> CheckAsync(string ip, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(ip?.Trim(), out var parsed)) throw new BusinessRuleException($"'{ip}' is not a valid IP address.");
        var now = DateTime.UtcNow;
        var address = parsed.ToString();
        var all = await uow.IpList.ListAsync(ct: ct);
        var matches = IpListEvaluator.EntriesContaining(all, address);
        var deciding = IpListEvaluator.Match(matches, new TrafficPacket(address, address, null, null, NetworkProtocol.Any, TrafficDirection.Inbound), now);
        var verdict = deciding is null ? "NotListed" : deciding.ListType == IpListType.Allow ? "Allowed" : "Blocked";
        return new IpCheckResultDto(address, verdict, deciding is null ? null : ToDto(deciding, now), matches.Select(m => ToDto(m, now)).ToList());
    }

    private async Task<string> ValidateAsync(string raw, IpListType list, DateTime? expiresAt, Guid? excludeId, DateTime now, CancellationToken ct)
    {
        var address = IpListEntry.Normalize(raw);
        if (IpListEntry.Validate(address, list) is { } error) throw new BusinessRuleException(error);
        if (expiresAt is { } exp && exp.ToUniversalTime() <= now) throw new BusinessRuleException("Expiry must be in the future.");

        var clash = await uow.IpList.FirstOrDefaultAsync(e => e.Address == address && e.Id != excludeId, ct: ct);
        if (clash is not null)
            throw new ConflictException($"{address} is already on the {(clash.ListType == IpListType.Allow ? "allowlist" : "blocklist")}.");
        return address;
    }

    private async Task<IpListEntry> LoadAsync(Guid id, CancellationToken ct) =>
        await uow.IpList.GetByIdAsync(id, ct: ct) ?? throw new NotFoundException("IP list entry", id);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    internal static IpListEntryDto ToDto(IpListEntry e, DateTime now) => new(
        e.Id, e.ListType, e.Address, e.Description, e.Source, e.SourceReference, e.ExpiresAt, e.IsEnabled,
        e.IsActive(now), e.IsExpired(now), e.HitCount, e.LastHitAt, e.CreatedBy, e.CreatedAt, e.UpdatedAt);
}
