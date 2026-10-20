using SecurityManager.Security.Application.Abstractions;
using SecurityManager.Security.Application.DTOs;
using SecurityManager.Security.Application.Mapping;
using SecurityManager.Security.Domain.Entities;
using SecurityManager.Security.Domain.Enums;
using SecurityManager.Shared.Kernel.Exceptions;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Security.Application.Services;

public interface IDetectionService
{
    Task<PagedResult<DetectionDto>> GetPagedAsync(DetectionQuery query, CancellationToken ct = default);
    Task<DetectionDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<DetectionDto> UpdateStatusAsync(Guid id, UpdateDetectionStatusRequest request, CancellationToken ct = default);
}

public sealed class DetectionService(ISecurityUnitOfWork uow) : IDetectionService
{
    public async Task<PagedResult<DetectionDto>> GetPagedAsync(DetectionQuery q, CancellationToken ct = default)
    {
        var search = q.Search?.Trim();
        var openOnly = q.OpenOnly == true;
        var result = await uow.Detections.GetPagedAsync(
            q.Page, q.PageSize,
            d => (q.Status == null || d.Status == q.Status)
                 && (q.MinSeverity == null || d.Severity >= q.MinSeverity)
                 && (!openOnly || d.Status == DetectionStatus.New || d.Status == DetectionStatus.Investigating)
                 && (string.IsNullOrEmpty(search) || d.Title.Contains(search) || d.CorrelationKey == search),
            orderBy: o => o.OrderByDescending(d => d.LastSeen),
            ct: ct);
        return result.Map(d => d.ToDto());
    }

    public async Task<DetectionDetailDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var detection = await LoadAsync(id, ct);
        var rule = new DetectionRule
        {
            EventType = detection.EventType,
            CorrelateBy = detection.CorrelateBy
        };

        // Evidence: the correlated events that make up this detection.
        var evidence = await uow.Events.ListAsync(
            DetectionEngine.WindowPredicate(rule, detection.CorrelationKey, detection.FirstSeen, detection.LastSeen),
            o => o.OrderByDescending(e => e.Timestamp),
            take: 100,
            ct: ct);

        return new DetectionDetailDto(detection.ToDto(), evidence.Select(e => e.ToDto()).ToList());
    }

    public async Task<DetectionDto> UpdateStatusAsync(Guid id, UpdateDetectionStatusRequest r, CancellationToken ct = default)
    {
        var detection = await LoadAsync(id, ct);
        detection.ChangeStatus(r.Status, r.Notes, r.AssignedTo);
        await uow.SaveChangesAsync(ct);
        return detection.ToDto();
    }

    private async Task<Detection> LoadAsync(Guid id, CancellationToken ct) =>
        await uow.Detections.GetByIdAsync(id, ct: ct) ?? throw new NotFoundException("Detection", id);
}
