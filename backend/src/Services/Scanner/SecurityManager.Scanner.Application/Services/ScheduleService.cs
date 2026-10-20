using SecurityManager.Scanner.Application.Abstractions;
using SecurityManager.Scanner.Application.DTOs;
using SecurityManager.Scanner.Application.Mapping;
using SecurityManager.Scanner.Domain.Entities;
using SecurityManager.Scanner.Domain.Enums;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Scanner.Application.Services;

public interface IScheduleService
{
    Task<IReadOnlyList<ScheduleDto>> GetAllAsync(CancellationToken ct = default);
    Task<ScheduleDto> CreateAsync(UpsertScheduleRequest request, CancellationToken ct = default);
    Task<ScheduleDto> UpdateAsync(Guid id, UpsertScheduleRequest request, CancellationToken ct = default);
    Task<ScheduleDto> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class ScheduleService(IScannerUnitOfWork uow) : IScheduleService
{
    public async Task<IReadOnlyList<ScheduleDto>> GetAllAsync(CancellationToken ct = default)
    {
        var schedules = await uow.Schedules.ListAsync(
            orderBy: o => o.OrderBy(s => s.NextRunAt), includes: [s => s.Target!], ct: ct);
        return schedules.Select(s => s.ToDto()).ToList();
    }

    public async Task<ScheduleDto> CreateAsync(UpsertScheduleRequest request, CancellationToken ct = default)
    {
        var schedule = new ScanSchedule();
        Apply(schedule, request);
        schedule.Target = await LoadTargetAsync(request.TargetId, ct);
        schedule.Reschedule(DateTime.UtcNow);
        await uow.Schedules.AddAsync(schedule, ct);
        await uow.SaveChangesAsync(ct);
        return schedule.ToDto();
    }

    public async Task<ScheduleDto> UpdateAsync(Guid id, UpsertScheduleRequest request, CancellationToken ct = default)
    {
        var schedule = await LoadAsync(id, ct);
        if (schedule.TargetId != request.TargetId) schedule.Target = await LoadTargetAsync(request.TargetId, ct);
        Apply(schedule, request);
        schedule.Reschedule(DateTime.UtcNow);
        await uow.SaveChangesAsync(ct);
        return schedule.ToDto();
    }

    public async Task<ScheduleDto> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
    {
        var schedule = await LoadAsync(id, ct);
        schedule.IsEnabled = enabled;
        schedule.Reschedule(DateTime.UtcNow);
        await uow.SaveChangesAsync(ct);
        return schedule.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var schedule = await LoadAsync(id, ct);
        uow.Schedules.Remove(schedule);
        await uow.SaveChangesAsync(ct);
    }

    private async Task<ScanSchedule> LoadAsync(Guid id, CancellationToken ct) =>
        await uow.Schedules.GetByIdAsync(id, [s => s.Target!], ct) ?? throw new NotFoundException("Schedule", id);

    private async Task<ScanTarget> LoadTargetAsync(Guid id, CancellationToken ct) =>
        await uow.Targets.GetByIdAsync(id, ct: ct) ?? throw new BusinessRuleException($"Target '{id}' does not exist.");

    private static void Apply(ScanSchedule s, UpsertScheduleRequest r)
    {
        if (r.ScanTypes == ScanType.None) throw new BusinessRuleException("Select at least one scan type.");
        s.Name = r.Name.Trim();
        s.ScanTypes = r.ScanTypes;
        s.IntervalMinutes = r.IntervalMinutes;
        s.SetRunAt(string.IsNullOrEmpty(r.RunAtUtc) ? null : TimeOnly.ParseExact(r.RunAtUtc, "HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        s.IsEnabled = r.IsEnabled;
    }
}
