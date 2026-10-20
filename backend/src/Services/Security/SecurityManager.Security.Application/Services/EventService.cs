using SecurityManager.Security.Application.Abstractions;
using SecurityManager.Security.Application.DTOs;
using SecurityManager.Security.Application.Mapping;
using SecurityManager.Security.Domain.Entities;
using SecurityManager.Shared.Kernel.Exceptions;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Security.Application.Services;

public interface IEventService
{
    Task<IngestResultDto> IngestAsync(CreateEventRequest request, CancellationToken ct = default);
    Task<PagedResult<SecurityEventDto>> GetPagedAsync(EventQuery query, CancellationToken ct = default);
    Task<SecurityEventDto> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Distinct values (with counts) of an event attribute, for searchable filter dropdowns.</summary>
    Task<PagedResult<ValueCount<string>>> LookupAsync(string field, LookupQuery query, CancellationToken ct = default);
}

public sealed class EventService(ISecurityUnitOfWork uow, IDetectionEngine engine) : IEventService
{
    public async Task<IngestResultDto> IngestAsync(CreateEventRequest r, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var timestamp = r.Timestamp is { } ts && ts <= now ? DateTime.SpecifyKind(ts, DateTimeKind.Utc) : now;

        var entity = new SecurityEvent
        {
            Timestamp = timestamp,
            EventType = r.EventType,
            Severity = r.Severity,
            Source = r.Source.Trim(),
            Message = r.Message.Trim(),
            SourceIp = r.SourceIp?.Trim(),
            TargetHost = r.TargetHost?.Trim(),
            UserName = r.UserName?.Trim(),
            RawData = r.RawData
        };

        await uow.Events.AddAsync(entity, ct);
        await uow.SaveChangesAsync(ct);

        var detections = await engine.ProcessAsync(entity, ct);
        return new IngestResultDto(entity.ToDto(), detections.Select(d => d.ToDto()).ToList());
    }

    public async Task<PagedResult<SecurityEventDto>> GetPagedAsync(EventQuery q, CancellationToken ct = default)
    {
        var search = q.Search?.Trim();
        var sourceIp = q.SourceIp?.Trim();
        var userName = q.UserName?.Trim();
        var targetHost = q.TargetHost?.Trim();
        // Model binding yields server-local DateTimes for "...Z" input; timestamps are stored in UTC.
        var from = q.From?.ToUniversalTime();
        var to = q.To?.ToUniversalTime();
        var result = await uow.Events.GetPagedAsync(
            q.Page, q.PageSize,
            e => (q.EventType == null || e.EventType == q.EventType)
                 && (q.MinSeverity == null || e.Severity >= q.MinSeverity)
                 && (string.IsNullOrEmpty(sourceIp) || e.SourceIp == sourceIp)
                 && (string.IsNullOrEmpty(userName) || e.UserName == userName)
                 && (string.IsNullOrEmpty(targetHost) || e.TargetHost == targetHost)
                 && (from == null || e.Timestamp >= from)
                 && (to == null || e.Timestamp <= to)
                 && (string.IsNullOrEmpty(search)
                     || e.Message.Contains(search)
                     || e.SourceIp == search
                     || e.UserName == search
                     || e.TargetHost == search
                     || e.Source.Contains(search)),
            orderBy: o => o.OrderByDescending(e => e.Timestamp),
            ct: ct);
        return result.Map(e => e.ToDto());
    }

    public async Task<SecurityEventDto> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        (await uow.Events.GetByIdAsync(id, ct: ct) ?? throw new NotFoundException("Event", id)).ToDto();

    public Task<PagedResult<ValueCount<string>>> LookupAsync(string field, LookupQuery q, CancellationToken ct = default)
    {
        var s = q.Search?.Trim() ?? string.Empty;
        return field.ToLowerInvariant() switch
        {
            "sourceip" => uow.Events.GetValueCountsPagedAsync(e => e.SourceIp!, q.Page, q.PageSize,
                e => e.SourceIp != null && e.SourceIp.Contains(s), ct),
            "username" => uow.Events.GetValueCountsPagedAsync(e => e.UserName!, q.Page, q.PageSize,
                e => e.UserName != null && e.UserName.Contains(s), ct),
            "targethost" => uow.Events.GetValueCountsPagedAsync(e => e.TargetHost!, q.Page, q.PageSize,
                e => e.TargetHost != null && e.TargetHost.Contains(s), ct),
            "source" => uow.Events.GetValueCountsPagedAsync(e => e.Source, q.Page, q.PageSize,
                e => e.Source.Contains(s), ct),
            _ => throw new BusinessRuleException($"Unsupported lookup field '{field}'. Use sourceIp, userName, targetHost or source.")
        };
    }
}
