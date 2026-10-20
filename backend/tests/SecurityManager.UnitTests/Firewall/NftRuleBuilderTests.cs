using SecurityManager.Enforcement.Firewall;
using SecurityManager.Enforcement.Sync;
using SecurityManager.Shared.Kernel.Contracts;

namespace SecurityManager.UnitTests.Firewall;

public class NftRuleBuilderTests
{
    private static EnforcedRule Rule(string direction = "In", string action = "Allow", string protocol = "Tcp",
        string local = "*", string remote = "*", string localPorts = "*", string remotePorts = "*") =>
        new("SM-R-abc-In-Tcp", "Display", "Desc", direction, action, protocol, local, remote, localPorts, remotePorts, "Policy / Rule");

    private static IReadOnlyList<NftRuleBuilder.NftRule> Build(EnforcedRule r) => NftRuleBuilder.Build(r, RuleReconciler.DescriptionFor(r));

    [Fact]
    public void Inbound_allow_from_one_host_to_a_port()
    {
        var rule = Assert.Single(Build(Rule(remote: "192.168.20.51", localPorts: "1433")));
        Assert.Equal("allow_in", rule.Chain);
        Assert.StartsWith("ip saddr 192.168.20.51 tcp dport 1433 accept comment \"SM-R-abc-In-Tcp [sm:", rule.Text);
    }

    [Fact]
    public void Block_goes_to_the_block_chain_and_drops()
    {
        var rule = Assert.Single(Build(Rule(action: "Block", protocol: "Any", remote: "203.0.113.0-203.0.113.255")));
        Assert.Equal("block_in", rule.Chain);
        Assert.StartsWith("ip saddr 203.0.113.0-203.0.113.255 drop", rule.Text);
    }

    [Fact]
    public void Mixed_address_families_become_one_rule_per_family()
    {
        var rules = Build(Rule(remote: "10.0.0.1,2001:db8::1", localPorts: "80,443"));
        Assert.Equal(2, rules.Count);
        Assert.Contains(rules, r => r.Text.StartsWith("ip saddr 10.0.0.1 tcp dport { 80, 443 } accept"));
        Assert.Contains(rules, r => r.Text.StartsWith("ip6 saddr 2001:db8::1 tcp dport { 80, 443 } accept"));
    }

    [Fact]
    public void Outbound_rules_match_the_destination()
    {
        var rule = Assert.Single(Build(Rule(direction: "Out", action: "Block", protocol: "Udp", remote: "198.51.100.7", remotePorts: "53")));
        Assert.Equal("block_out", rule.Chain);
        Assert.StartsWith("ip daddr 198.51.100.7 udp dport 53 drop", rule.Text);
    }

    [Fact]
    public void Any_address_any_port_matches_the_protocol_only()
    {
        var rule = Assert.Single(Build(Rule()));
        Assert.StartsWith("meta l4proto tcp accept", rule.Text);
    }

    [Fact]
    public void Icmpv4_is_limited_to_ipv4()
    {
        var rule = Assert.Single(Build(Rule(protocol: "Icmpv4", remote: "10.0.0.0-10.255.255.255,fd00::-fd00::ffff")));
        Assert.StartsWith("ip saddr 10.0.0.0-10.255.255.255 meta l4proto icmp accept", rule.Text);
    }

    [Fact]
    public void Comment_keeps_the_key_and_the_reconciler_fingerprint()
    {
        var r = Rule(remote: "10.0.0.1");
        var comment = NftRuleBuilder.Comment(r.Key, RuleReconciler.DescriptionFor(r));
        Assert.Equal(r.Key, NftRuleBuilder.KeyFromComment(comment));
        Assert.Equal(RuleReconciler.Fingerprint(r), RuleReconciler.ReadFingerprint(comment));
        Assert.True(comment.Length <= 120);
    }
}
