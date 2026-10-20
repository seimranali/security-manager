using Microsoft.EntityFrameworkCore;
using SecurityManager.Firewall.Application.DTOs;
using SecurityManager.Firewall.Application.Services;
using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Firewall.Domain.Services;
using SecurityManager.Firewall.Domain.ValueObjects;
using SecurityManager.Firewall.Infrastructure.Persistence;
using SecurityManager.Shared.Kernel.Exceptions;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.UnitTests.Firewall;

public class IpListTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    // ---------- Engine precedence ----------

    [Fact]
    public void Blocklist_overrides_allow_rules_and_allowlist_overrides_blocklist()
    {
        var allowAll = new FirewallPolicy { Name = "Open", Direction = TrafficDirection.Any, DefaultAction = RuleAction.Allow };
        var block = Entry(IpListType.Block, "203.0.113.0/24");
        var trusted = Entry(IpListType.Allow, "203.0.113.10");

        var blocked = FirewallEngine.Evaluate([allowAll], Inbound("203.0.113.50"), [block, trusted], Now);
        Assert.Equal(RuleAction.Deny, blocked.Action);
        Assert.Same(block, blocked.ListEntry);

        var allowed = FirewallEngine.Evaluate([], Inbound("203.0.113.10"), [block, trusted], Now);
        Assert.Equal(RuleAction.Allow, allowed.Action); // allowlisted even with no policy (would be implicit deny)
        Assert.Same(trusted, allowed.ListEntry);
    }

    [Fact]
    public void Expired_or_disabled_entries_are_ignored()
    {
        var expired = Entry(IpListType.Block, "198.51.100.1");
        expired.ExpiresAt = Now.AddMinutes(-1);
        var disabled = Entry(IpListType.Block, "198.51.100.2");
        disabled.IsEnabled = false;
        var open = new FirewallPolicy { Name = "Open", DefaultAction = RuleAction.Allow };

        Assert.Equal(RuleAction.Allow, FirewallEngine.Evaluate([open], Inbound("198.51.100.1"), [expired, disabled], Now).Action);
        Assert.Equal(RuleAction.Allow, FirewallEngine.Evaluate([open], Inbound("198.51.100.2"), [expired, disabled], Now).Action);
    }

    [Fact]
    public void Matches_the_remote_party_of_the_connection()
    {
        var c2 = Entry(IpListType.Block, "198.51.100.0/24");
        var open = new FirewallPolicy { Name = "Open", DefaultAction = RuleAction.Allow };
        var outbound = new TrafficPacket("10.0.0.5", "198.51.100.9", 50000, 443, NetworkProtocol.Tcp, TrafficDirection.Outbound);
        var inboundFromInternal = new TrafficPacket("10.0.0.5", "198.51.100.9", 50000, 443, NetworkProtocol.Tcp, TrafficDirection.Inbound);

        Assert.Equal(RuleAction.Deny, FirewallEngine.Evaluate([open], outbound, [c2], Now).Action);            // destination checked
        Assert.Equal(RuleAction.Allow, FirewallEngine.Evaluate([open], inboundFromInternal, [c2], Now).Action); // only source checked
    }

    // ---------- Validation ----------

    [Theory]
    [InlineData("10.0.0.1", IpListType.Block, true)]
    [InlineData("10.0.0.0/24", IpListType.Block, true)]
    [InlineData("10.0.0.1-10.0.0.9", IpListType.Block, true)]
    [InlineData("2001:db8::/48", IpListType.Block, true)]
    [InlineData("0.0.0.0/0", IpListType.Block, false)]   // too broad
    [InlineData("10.0.0.0/7", IpListType.Block, false)]  // too broad
    [InlineData("10.0.0.0/7", IpListType.Allow, true)]   // breadth limit only applies to blocks
    [InlineData("any", IpListType.Allow, false)]
    [InlineData("1.2.3.4,5.6.7.8", IpListType.Block, false)] // one per entry
    [InlineData("999.1.1.1", IpListType.Block, false)]
    public void Validates_addresses(string address, IpListType list, bool valid) =>
        Assert.Equal(valid, IpListEntry.Validate(address, list) is null);

    [Theory]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.20.0.1", true)]
    [InlineData("192.168.5.5", true)]
    [InlineData("100.64.1.1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("172.32.0.1", false)]
    public void Classifies_private_and_reserved_addresses(string ip, bool reserved) =>
        Assert.Equal(reserved, IpClassifier.IsPrivateOrReserved(ip));

    // ---------- Service ----------

    [Fact]
    public async Task Rejects_duplicates_across_both_lists_and_past_expiry()
    {
        var (service, _) = NewService();
        await service.CreateAsync(new UpsertIpListEntryRequest { ListType = IpListType.Allow, Address = " 10.0.0.5 " }, "alice");

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(new UpsertIpListEntryRequest { ListType = IpListType.Block, Address = "10.0.0.5" }, "alice"));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CreateAsync(new UpsertIpListEntryRequest { Address = "8.8.8.8", ExpiresAt = DateTime.UtcNow.AddMinutes(-5) }, "alice"));
    }

    [Fact]
    public async Task Import_parses_mixed_separators_and_comments_and_reports_results()
    {
        var (service, db) = NewService();
        db.IpList.Add(Entry(IpListType.Block, "192.0.2.1"));
        await db.SaveChangesAsync();

        var result = await service.ImportAsync(new ImportIpListRequest
        {
            ListType = IpListType.Block,
            Addresses = "# threat feed\n192.0.2.1\n192.0.2.2, 192.0.2.3; 198.51.100.0/24\nnot-an-ip\n0.0.0.0/0 # too broad\n192.0.2.2"
        }, "importer");

        Assert.Equal(3, result.Added);   // .2, .3 and the /24 (duplicate .2 de-duplicated)
        Assert.Equal(1, result.Skipped); // .1 already listed
        Assert.Equal(["not-an-ip", "0.0.0.0/0"], result.Invalid);
        Assert.All(await db.IpList.Where(e => e.CreatedBy == "importer").ToListAsync(), e => Assert.Equal(IpListSource.Import, e.Source));
    }

    [Fact]
    public async Task Auto_block_creates_then_extends_and_respects_allowlist()
    {
        var (service, db) = NewService();

        var first = await service.AutoBlockAsync(new AutoBlockRequest { Address = "203.0.113.7", Reason = "Brute force", DurationMinutes = 60 }, "service");
        var second = await service.AutoBlockAsync(new AutoBlockRequest { Address = "203.0.113.7", Reason = "Brute force", DurationMinutes = 600 }, "service");

        Assert.Equal(first.Id, second.Id);
        Assert.True(second.ExpiresAt > first.ExpiresAt);
        Assert.Equal(IpListSource.Detection, second.Source);
        Assert.Equal(1, await db.IpList.CountAsync());

        await service.CreateAsync(new UpsertIpListEntryRequest { ListType = IpListType.Allow, Address = "203.0.113.99" }, "alice");
        await Assert.ThrowsAsync<ConflictException>(() =>
            service.AutoBlockAsync(new AutoBlockRequest { Address = "203.0.113.99", Reason = "x" }, "service"));
    }

    [Fact]
    public async Task Check_reports_verdict_and_all_matching_entries()
    {
        var (service, db) = NewService();
        db.IpList.AddRange(Entry(IpListType.Block, "203.0.113.0/24"), Entry(IpListType.Allow, "203.0.113.10"));
        await db.SaveChangesAsync();

        var trusted = await service.CheckAsync("203.0.113.10");
        Assert.Equal("Allowed", trusted.Verdict);
        Assert.Equal(2, trusted.Matches.Count);

        Assert.Equal("Blocked", (await service.CheckAsync("203.0.113.11")).Verdict);
        Assert.Equal("NotListed", (await service.CheckAsync("8.8.8.8")).Verdict);
    }

    private static IpListEntry Entry(IpListType type, string address) => new() { ListType = type, Address = address };

    private static TrafficPacket Inbound(string src) =>
        new(src, "10.0.0.10", 40000, 443, NetworkProtocol.Tcp, TrafficDirection.Inbound);

    private static (IpListService, FirewallDbContext) NewService()
    {
        var db = new FirewallDbContext(new DbContextOptionsBuilder<FirewallDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new IpListService(new FirewallUnitOfWork(db)), db);
    }
}
