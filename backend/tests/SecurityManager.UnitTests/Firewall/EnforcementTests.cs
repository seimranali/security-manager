using SecurityManager.Enforcement.Sync;
using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Firewall.Domain.Services;
using SecurityManager.Shared.Kernel.Contracts;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.UnitTests.Firewall;

public class EnforcementTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    // ---------- IP range arithmetic ----------

    [Fact]
    public void Subtracting_an_address_splits_a_cidr_into_ranges()
    {
        var result = IpRanges.ToText(IpRanges.Subtract(IpRanges.Parse("203.0.113.0/24"), IpRanges.Parse("203.0.113.10")));
        Assert.Equal(["203.0.113.0-203.0.113.9", "203.0.113.11-203.0.113.255"], result);
    }

    [Fact]
    public void Any_minus_one_address_covers_both_families()
    {
        var result = IpRanges.ToText(IpRanges.Subtract(IpRanges.Parse("any"), IpRanges.Parse("192.168.20.51")));
        Assert.Contains("0.0.0.0-192.168.20.50", result);
        Assert.Contains("192.168.20.52-255.255.255.255", result);
        Assert.Contains(result, r => r.StartsWith("::-"));
        Assert.Equal(["*"], IpRanges.ToText(IpRanges.Parse("any")));
    }

    [Fact]
    public void Normalize_merges_adjacent_and_overlapping_ranges()
    {
        var merged = IpRanges.Normalize([.. IpRanges.Parse("10.0.0.0/25"), .. IpRanges.Parse("10.0.0.128-10.0.0.200"), .. IpRanges.Parse("10.0.0.150")]);
        Assert.Equal(["10.0.0.0-10.0.0.200"], IpRanges.ToText(merged));
    }

    // ---------- Planner ----------

    [Fact]
    public void Every_enabled_policy_is_enforced_without_opting_in()
    {
        var thisPc = Policy("This PC", enforce: false, Rule("Allow SQL from Adeel", RuleAction.Allow, "192.168.20.51", "1433"));
        var perimeter = Policy("Perimeter", enforce: false, Rule("Deny RDP", RuleAction.Deny, "any", "3389"));
        var disabled = Policy("Old", enforce: true, Rule("Deny SSH", RuleAction.Deny, "any", "22"));
        disabled.IsEnabled = false;

        var plan = EnforcementPlanner.Plan([thisPc, perimeter, disabled], [], new EnforcementSettings(), Now);

        Assert.Equal(2, plan.Rules.Count);
        Assert.Contains(plan.Rules, r => r.Action == "Block" && r.LocalPorts == "3389");
        Assert.DoesNotContain(plan.Rules, r => r.LocalPorts == "22");
        var rule = Assert.Single(plan.Rules, r => r.Action == "Allow");
        Assert.Equal("Allow", rule.Action);
        Assert.Equal("In", rule.Direction);
        Assert.Equal("Tcp", rule.Protocol);
        Assert.Equal("192.168.20.51", rule.RemoteAddresses);
        Assert.Equal("1433", rule.LocalPorts);
    }

    [Fact]
    public void Allowlisted_addresses_are_carved_out_of_blocks_and_deny_rules()
    {
        var policy = Policy("This PC", enforce: true, Rule("Deny SQL", RuleAction.Deny, "any", "1433"));
        IpListEntry[] lists =
        [
            new() { ListType = IpListType.Block, Address = "192.168.20.0/24" },
            new() { ListType = IpListType.Allow, Address = "192.168.20.51" },
        ];

        var plan = EnforcementPlanner.Plan([policy], lists, new EnforcementSettings(), Now);

        var blocklistIn = plan.Rules.Single(r => r.Key == "SM-BL-In");
        Assert.Equal("192.168.20.0-192.168.20.50,192.168.20.52-192.168.20.255", blocklistIn.RemoteAddresses);
        Assert.Contains(plan.Rules, r => r.Key == "SM-BL-Out");
        var deny = plan.Rules.Single(r => r.Origin == "This PC / Deny SQL");
        Assert.DoesNotContain("192.168.20.51,", deny.RemoteAddresses + ",");
        Assert.Contains("192.168.20.52-", deny.RemoteAddresses);
        Assert.DoesNotContain(plan.Rules, r => r.Key.StartsWith("SM-AL")); // allowlist opening is opt-in
    }

    [Fact]
    public void Expands_protocols_and_directions_and_warns_on_ordering_conflicts()
    {
        var policy = Policy("This PC", enforce: true,
            Rule("Allow web", RuleAction.Allow, "any", "8080", NetworkProtocol.Any, priority: 10),
            Rule("Deny all high ports", RuleAction.Deny, "any", "8000-9000", NetworkProtocol.Tcp, priority: 20, direction: TrafficDirection.Any));

        var plan = EnforcementPlanner.Plan([policy], [], new EnforcementSettings(), Now);

        Assert.Contains(plan.Rules, r => r.Origin == "This PC / Allow web" && r.Protocol == "Tcp");
        Assert.Contains(plan.Rules, r => r.Origin == "This PC / Allow web" && r.Protocol == "Udp");
        Assert.Contains(plan.Rules, r => r.Origin == "This PC / Deny all high ports" && r.Direction == "Out");
        Assert.Contains(plan.Warnings, w => w.Contains("'This PC / Allow web' (Allow) overlaps"));
    }

    [Fact]
    public void Disabled_enforcement_produces_no_rules_and_version_is_stable()
    {
        var policy = Policy("This PC", enforce: true, Rule("Deny SQL", RuleAction.Deny, "any", "1433"));
        var off = EnforcementPlanner.Plan([policy], [], new EnforcementSettings { Enabled = false }, Now);
        Assert.False(off.Enabled);
        Assert.Empty(off.Rules);

        var a = EnforcementPlanner.Plan([policy], [], new EnforcementSettings(), Now);
        var b = EnforcementPlanner.Plan([policy], [], new EnforcementSettings(), Now.AddMinutes(5));
        Assert.Equal(a.Version, b.Version);
        Assert.NotEqual(a.Version, off.Version);
    }

    // ---------- Agent reconciliation ----------

    [Fact]
    public void Reconciler_adds_missing_replaces_changed_and_removes_stale_rules()
    {
        var keep = Enforced("SM-keep", "1433");
        var change = Enforced("SM-change", "1433");
        var add = Enforced("SM-new", "443");
        (string, string)[] installed =
        [
            ("SM-keep", RuleReconciler.DescriptionFor(keep)),
            ("SM-change", RuleReconciler.DescriptionFor(change with { LocalPorts = "1434" })),
            ("SM-stale", "old [sm:deadbeef0000]"),
        ];

        var plan = RuleReconciler.Plan([keep, change, add], installed);

        Assert.Equal(1, plan.Unchanged);
        Assert.Equal(1, plan.Updated);
        Assert.Equal(["SM-change", "SM-new"], plan.ToAdd.Select(a => a.Rule.Key));
        Assert.Equal(["SM-change", "SM-stale"], plan.ToRemove);
    }

    [Fact]
    public void Detects_foreign_block_rules_that_shadow_allows()
    {
        var allow = Enforced("SM-allow", "1433") with { Action = "Allow", Origin = "This PC / Allow SQL" };
        var warnings = RuleReconciler.FindShadowedAllows([allow], "Windows Firewall", [("Block SQL Server (TCP 1433) from network", "Tcp", "1433"), ("Unrelated", "Tcp", "80")]).ToList();
        var warning = Assert.Single(warnings);
        Assert.Contains("Block SQL Server", warning);
    }

    // ---------- Helpers ----------

    private static FirewallPolicy Policy(string name, bool enforce, params FirewallRule[] rules)
    {
        var p = new FirewallPolicy { Name = name, EnforceOnHosts = enforce, Direction = TrafficDirection.Any };
        foreach (var r in rules) p.Rules.Add(r);
        return p;
    }

    private static FirewallRule Rule(string name, RuleAction action, string source, string port,
        NetworkProtocol protocol = NetworkProtocol.Tcp, int priority = 100, TrafficDirection direction = TrafficDirection.Inbound) => new()
    {
        Name = name, Action = action, SourceAddress = source, DestinationPort = port, Protocol = protocol, Priority = priority, Direction = direction
    };

    private static EnforcedRule Enforced(string key, string port) =>
        new(key, key, "test", "In", "Block", "Tcp", "*", "*", port, "*", "test");
}
