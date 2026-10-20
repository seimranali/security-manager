using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Firewall.Domain.Services;
using SecurityManager.Firewall.Domain.ValueObjects;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.UnitTests.Firewall;

public class FirewallEngineTests
{
    [Theory]
    [InlineData("10.0.0.0/8", "10.20.30.40", true)]
    [InlineData("10.0.0.0/8", "11.0.0.1", false)]
    [InlineData("192.168.1.0/25", "192.168.1.127", true)]
    [InlineData("192.168.1.0/25", "192.168.1.128", false)]
    [InlineData("10.0.0.1-10.0.0.50", "10.0.0.25", true)]
    [InlineData("10.0.0.1-10.0.0.50", "10.0.0.51", false)]
    [InlineData("1.1.1.1, 8.8.8.8", "8.8.8.8", true)]
    [InlineData("fd00::/8", "fd12::1", true)]
    [InlineData("any", "203.0.113.9", true)]
    public void AddressMatcher_matches_specs(string spec, string ip, bool expected) =>
        Assert.Equal(expected, AddressMatcher.IsMatch(spec, ip));

    [Theory]
    [InlineData("10.0.0.0/33")]
    [InlineData("not-an-ip")]
    [InlineData("10.0.0.50-10.0.0.1")]
    public void AddressMatcher_rejects_invalid_specs(string spec) => Assert.False(AddressMatcher.IsValid(spec));

    [Theory]
    [InlineData("80,443", 443, true)]
    [InlineData("1000-2000", 1500, true)]
    [InlineData("1000-2000", 2001, false)]
    [InlineData("any", null, true)]
    [InlineData("22", null, false)]
    public void PortSpec_matches(string spec, int? port, bool expected) =>
        Assert.Equal(expected, PortSpec.IsMatch(spec, port));

    [Fact]
    public void First_matching_rule_by_priority_wins()
    {
        var policy = Policy(TrafficDirection.Inbound, RuleAction.Deny,
            Rule("Deny bad range", RuleAction.Deny, 1, src: "203.0.113.0/24"),
            Rule("Allow HTTPS", RuleAction.Allow, 10, dstPort: "443"));

        var fromBadRange = Packet("203.0.113.5", 443);
        var fromGoodRange = Packet("8.8.8.8", 443);

        Assert.Equal(RuleAction.Deny, FirewallEngine.Evaluate([policy], fromBadRange).Action);
        var allowed = FirewallEngine.Evaluate([policy], fromGoodRange);
        Assert.Equal(RuleAction.Allow, allowed.Action);
        Assert.Equal("Allow HTTPS", allowed.Rule?.Name);
    }

    [Fact]
    public void Falls_back_to_policy_default_then_implicit_deny()
    {
        var inbound = Policy(TrafficDirection.Inbound, RuleAction.Deny, Rule("Allow HTTPS", RuleAction.Allow, 10, dstPort: "443"));
        var outbound = Policy(TrafficDirection.Outbound, RuleAction.Allow);

        var unmatchedInbound = FirewallEngine.Evaluate([inbound, outbound], Packet("8.8.8.8", 22));
        Assert.Equal(RuleAction.Deny, unmatchedInbound.Action);
        Assert.Null(unmatchedInbound.Rule);

        var outboundPacket = Packet("10.0.0.5", 443) with { Direction = TrafficDirection.Outbound };
        Assert.Equal(RuleAction.Allow, FirewallEngine.Evaluate([inbound, outbound], outboundPacket).Action);

        var none = FirewallEngine.Evaluate([], Packet("8.8.8.8", 443));
        Assert.Equal(RuleAction.Deny, none.Action);
        Assert.Contains("implicit", none.Reason);
    }

    [Fact]
    public void Disabled_rules_and_protocol_mismatch_are_ignored()
    {
        var rule = Rule("Allow SSH", RuleAction.Allow, 1, dstPort: "22");
        rule.Protocol = NetworkProtocol.Udp;
        var disabled = Rule("Disabled allow", RuleAction.Allow, 2, dstPort: "22");
        disabled.IsEnabled = false;
        var policy = Policy(TrafficDirection.Any, RuleAction.Deny, rule, disabled);

        Assert.Equal(RuleAction.Deny, FirewallEngine.Evaluate([policy], Packet("1.2.3.4", 22)).Action);
    }

    private static FirewallPolicy Policy(TrafficDirection direction, RuleAction defaultAction, params FirewallRule[] rules)
    {
        var policy = new FirewallPolicy { Name = $"P-{direction}", Direction = direction, DefaultAction = defaultAction, Priority = direction == TrafficDirection.Inbound ? 10 : 20 };
        foreach (var r in rules) policy.Rules.Add(r);
        return policy;
    }

    private static FirewallRule Rule(string name, RuleAction action, int priority, string src = "any", string dstPort = "any") => new()
    {
        Name = name, Action = action, Priority = priority, Direction = TrafficDirection.Any,
        SourceAddress = src, DestinationPort = dstPort
    };

    private static TrafficPacket Packet(string src, int dstPort) =>
        new(src, "10.0.0.10", 50000, dstPort, NetworkProtocol.Tcp, TrafficDirection.Inbound);
}
