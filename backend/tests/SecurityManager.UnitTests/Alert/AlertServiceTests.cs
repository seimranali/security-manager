using Microsoft.EntityFrameworkCore;
using SecurityManager.Alert.Application.Abstractions;
using SecurityManager.Alert.Application.DTOs;
using SecurityManager.Alert.Application.Services;
using SecurityManager.Alert.Domain.Enums;
using SecurityManager.Alert.Infrastructure.Persistence;
using SecurityManager.Shared.Kernel.Enums;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.UnitTests.Alert;

public class AlertServiceTests
{
    [Fact]
    public async Task Duplicate_alerts_are_merged_and_escalated()
    {
        await using var db = NewDb();
        var service = new AlertService(new AlertUnitOfWork(db), new NullNotifier(), new NullQueue());

        var first = await service.CreateAsync(Request("ref-1", Severity.Medium));
        var second = await service.CreateAsync(Request("ref-1", Severity.Critical));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(2, second.OccurrenceCount);
        Assert.Equal(Severity.Critical, second.Severity);
        Assert.Equal(1, await db.Alerts.CountAsync());
    }

    [Fact]
    public async Task Lifecycle_acknowledge_resolve_and_reject_ack_after_resolve()
    {
        await using var db = NewDb();
        var service = new AlertService(new AlertUnitOfWork(db), new NullNotifier(), new NullQueue());
        var alert = await service.CreateAsync(Request("ref-2", Severity.High));

        var acked = await service.AcknowledgeAsync(alert.Id, new AlertActionRequest { User = "alice" });
        Assert.Equal(AlertStatus.Acknowledged, acked.Status);

        var resolved = await service.ResolveAsync(alert.Id, new AlertActionRequest { Notes = "patched" });
        Assert.Equal(AlertStatus.Resolved, resolved.Status);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AcknowledgeAsync(alert.Id, new AlertActionRequest()));

        // A new occurrence after resolution creates a fresh alert.
        var fresh = await service.CreateAsync(Request("ref-2", Severity.High));
        Assert.NotEqual(alert.Id, fresh.Id);
    }

    private static CreateAlertRequest Request(string reference, Severity severity) => new()
    {
        Title = "Test alert", Message = "Something happened", Severity = severity, Source = "Test", SourceReference = reference
    };

    private static AlertDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AlertDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class NullNotifier : IAlertNotifier
    {
        public Task AlertCreatedAsync(AlertDto alert, CancellationToken ct = default) => Task.CompletedTask;
        public Task AlertUpdatedAsync(AlertDto alert, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullQueue : SecurityManager.Alert.Application.Abstractions.INotificationQueue
    {
        public void Enqueue(SecurityManager.Alert.Application.DTOs.AlertDto alert, bool escalation) { }
    }
}
