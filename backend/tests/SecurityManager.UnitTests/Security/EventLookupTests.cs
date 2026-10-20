using Microsoft.EntityFrameworkCore;
using SecurityManager.Security.Application.Abstractions;
using SecurityManager.Security.Application.Services;
using SecurityManager.Security.Domain.Entities;
using SecurityManager.Security.Domain.Enums;
using SecurityManager.Security.Infrastructure.Persistence;
using SecurityManager.Shared.Kernel.Contracts;
using SecurityManager.Shared.Kernel.Exceptions;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.UnitTests.Security;

public class EventLookupTests
{
    [Fact]
    public async Task Lookup_returns_distinct_values_by_frequency_with_search_and_paging()
    {
        var (service, _) = await CreateAsync("10.0.0.1", "10.0.0.1", "10.0.0.1", "10.0.0.2", "10.0.0.2", "192.168.1.5", null);

        var all = await service.LookupAsync("sourceIp", new LookupQuery { PageSize = 2 });
        Assert.Equal(3, all.TotalCount);                       // nulls excluded
        Assert.Equal(["10.0.0.1", "10.0.0.2"], all.Items.Select(i => i.Value));
        Assert.Equal(3, all.Items[0].Count);

        var page2 = await service.LookupAsync("sourceIp", new LookupQuery { PageSize = 2, Page = 2 });
        Assert.Equal("192.168.1.5", Assert.Single(page2.Items).Value);

        var searched = await service.LookupAsync("SOURCEIP", new LookupQuery { Search = "192" });
        Assert.Equal("192.168.1.5", Assert.Single(searched.Items).Value);
    }

    [Fact]
    public async Task Lookup_rejects_unknown_fields()
    {
        var (service, _) = await CreateAsync("1.1.1.1");
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.LookupAsync("message", new LookupQuery()));
    }

    private static async Task<(EventService, SecurityDbContext)> CreateAsync(params string?[] ips)
    {
        var db = new SecurityDbContext(new DbContextOptionsBuilder<SecurityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        foreach (var ip in ips)
            db.Events.Add(new SecurityEvent { EventType = SecurityEventType.Other, Source = "test", Message = "m", SourceIp = ip });
        await db.SaveChangesAsync();
        var uow = new SecurityUnitOfWork(db);
        return (new EventService(uow, DetectionEngineTests.Engine(uow, new NoAlerts())), db);
    }

    private sealed class NoAlerts : IAlertPublisher
    {
        public Task PublishAsync(AlertMessage message, CancellationToken ct = default) => Task.CompletedTask;
    }
}
