using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecurityManager.Security.Application.Abstractions;
using SecurityManager.Security.Application.DTOs;
using SecurityManager.Security.Application.Services;
using SecurityManager.Security.Domain.Entities;
using SecurityManager.Security.Domain.Enums;
using SecurityManager.Security.Infrastructure.Persistence;
using SecurityManager.Shared.Kernel.Contracts;
using SecurityManager.Shared.Kernel.Enums;

namespace SecurityManager.UnitTests.Security;

/// <summary>Exercises EventService + DetectionEngine through the real Unit of Work on an in-memory database.</summary>
public class DetectionEngineTests
{
    [Fact]
    public async Task Raises_one_detection_when_threshold_reached_then_updates_it()
    {
        await using var db = NewDb();
        db.Rules.Add(new DetectionRule
        {
            Name = "Brute force", EventType = SecurityEventType.AuthenticationFailure,
            Threshold = 3, TimeWindowMinutes = 5, CorrelateBy = CorrelationField.SourceIp, Severity = Severity.High
        });
        await db.SaveChangesAsync();

        var alerts = new RecordingAlertPublisher();
        var uow = new SecurityUnitOfWork(db);
        var service = new EventService(uow, Engine(uow, alerts));

        IngestResultDto? last = null;
        for (var i = 0; i < 5; i++) last = await service.IngestAsync(Failure("185.220.101.4"));

        var detection = Assert.Single(await db.Detections.ToListAsync());
        Assert.Equal("185.220.101.4", detection.CorrelationKey);
        Assert.Equal(5, detection.EventCount);
        Assert.Single(alerts.Messages);                // alert only on creation, not on updates
        Assert.Empty(last!.NewDetections);
    }

    [Fact]
    public async Task Events_from_different_sources_are_correlated_separately()
    {
        await using var db = NewDb();
        db.Rules.Add(new DetectionRule
        {
            Name = "Brute force", EventType = SecurityEventType.AuthenticationFailure,
            Threshold = 2, TimeWindowMinutes = 5, CorrelateBy = CorrelationField.SourceIp
        });
        await db.SaveChangesAsync();

        var uow = new SecurityUnitOfWork(db);
        var service = new EventService(uow, Engine(uow, new RecordingAlertPublisher()));

        await service.IngestAsync(Failure("1.1.1.1"));
        await service.IngestAsync(Failure("2.2.2.2"));
        Assert.Empty(await db.Detections.ToListAsync());

        await service.IngestAsync(Failure("1.1.1.1"));
        Assert.Single(await db.Detections.ToListAsync());
    }

    [Theory]
    [InlineData("185.220.101.4", Severity.High, true)]   // public IP, high severity → blocked
    [InlineData("185.220.101.4", Severity.Medium, false)] // below threshold
    [InlineData("10.0.1.15", Severity.Critical, false)]   // private range never auto-blocked
    [InlineData("127.0.0.1", Severity.Critical, false)]   // loopback
    public async Task Auto_blocks_only_public_ips_from_severe_detections(string ip, Severity severity, bool expectBlocked)
    {
        await using var db = NewDb();
        db.Rules.Add(new DetectionRule
        {
            Name = "Brute force", EventType = SecurityEventType.AuthenticationFailure,
            Threshold = 1, TimeWindowMinutes = 5, CorrelateBy = CorrelationField.SourceIp, Severity = severity
        });
        await db.SaveChangesAsync();

        var firewall = new RecordingFirewall();
        var uow = new SecurityUnitOfWork(db);
        await new EventService(uow, Engine(uow, new RecordingAlertPublisher(), firewall)).IngestAsync(Failure(ip));

        Assert.Equal(expectBlocked, firewall.Blocked.Contains(ip));
    }

    internal static DetectionEngine Engine(SecurityUnitOfWork uow, IAlertPublisher alerts, IFirewallBlocker? firewall = null) =>
        new(uow, alerts, firewall ?? new RecordingFirewall(), Options.Create(new AutoBlockOptions()));

    internal sealed class RecordingFirewall : IFirewallBlocker
    {
        public List<string> Blocked { get; } = [];

        public Task BlockAsync(string ipAddress, string reason, TimeSpan duration, string sourceReference, CancellationToken ct = default)
        {
            Blocked.Add(ipAddress);
            return Task.CompletedTask;
        }
    }

    private static CreateEventRequest Failure(string ip) => new()
    {
        EventType = SecurityEventType.AuthenticationFailure,
        Severity = Severity.Low,
        Source = "test",
        Message = "Failed login",
        SourceIp = ip,
        UserName = "admin"
    };

    private static SecurityDbContext NewDb() =>
        new(new DbContextOptionsBuilder<SecurityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class RecordingAlertPublisher : IAlertPublisher
    {
        public List<AlertMessage> Messages { get; } = [];

        public Task PublishAsync(AlertMessage message, CancellationToken ct = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
