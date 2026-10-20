using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Shared.Kernel.Contracts;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.Firewall.Domain.Services;

/// <summary>
/// Translates Security Manager's model into host firewall rules (Windows Firewall semantics: no rule priorities,
/// and a matching block rule always beats an allow rule).
///
/// Precedence is preserved by construction:
///   1. Allowlisted addresses are carved out of every block (blocklist and Deny rules), so they are never blocked.
///   2. The blocklist blocks in both directions and overrides Allow rules — as in Security Manager.
///   3. Rules come from every enabled policy (and enabled rule): what the UI shows is what the host enforces.
/// Where Security Manager's first-match ordering cannot be expressed (an earlier Allow overlapped by a later Deny),
/// a warning is produced: the host will apply the Deny.
/// </summary>
public static class EnforcementPlanner
{
    public const int MaxAddressesPerRule = 1000;

    public static EnforcementDesiredState Plan(
        IEnumerable<FirewallPolicy> policies, IEnumerable<IpListEntry> ipList, EnforcementSettings settings, DateTime now)
    {
        var rules = new List<EnforcedRule>();
        var warnings = new List<string>();

        if (!settings.Enabled)
            return Build(rules, ["Host enforcement is turned off; agents remove all managed rules."], false, now);

        var active = ipList.Where(e => e.IsActive(now)).ToList();
        var exempt = Ranges(active.Where(e => e.ListType == IpListType.Allow).Select(e => e.Address));

        // ---- 1. Blocklist (both directions), minus allowlisted addresses.
        if (settings.EnforceBlocklist)
        {
            var blocked = Ranges(active.Where(e => e.ListType == IpListType.Block).Select(e => e.Address));
            var effective = IpRanges.Subtract(blocked, exempt);
            if (IpRanges.Intersects(blocked, exempt))
                warnings.Add("Some blocklist ranges contain allowlisted addresses; those addresses are exempted from the block.");
            foreach (var dir in new[] { "In", "Out" })
                AddChunked(rules, effective, chunk => new EnforcedRule(
                    $"SM-BL-{dir}", $"Security Manager: IP blocklist ({(dir == "In" ? "inbound" : "outbound")})",
                    "Addresses on the Security Manager IP blocklist.", dir, "Block", "Any", "*", chunk, "*", "*", "IP blocklist"));
        }

        // ---- 2. Allowlist (opt-in): open all inbound traffic from trusted addresses.
        if (settings.EnforceAllowlist && exempt.Count > 0)
            AddChunked(rules, exempt, chunk => new EnforcedRule(
                "SM-AL-In", "Security Manager: IP allowlist (inbound)", "Trusted addresses on the Security Manager IP allowlist.",
                "In", "Allow", "Any", "*", chunk, "*", "*", "IP allowlist"));

        // ---- 3. Rules of every enabled policy.
        var enforced = policies.Where(p => p.IsEnabled).OrderBy(p => p.Priority).ToList();
        var planned = new List<(EnforcedRule Rule, int Order, string Label, List<IpRange> Remote, string Ports)>();
        var order = 0;

        foreach (var policy in enforced)
        {
            foreach (var rule in policy.Rules.Where(r => r.IsEnabled).OrderBy(r => r.Priority).ThenBy(r => r.CreatedAt))
            {
                order++;
                var label = $"{policy.Name} / {rule.Name}";
                foreach (var dir in Directions(policy.Direction, rule.Direction))
                {
                    var inbound = dir == "In";
                    var remoteSpec = inbound ? rule.SourceAddress : rule.DestinationAddress;
                    var localSpec = inbound ? rule.DestinationAddress : rule.SourceAddress;
                    var remotePorts = Ports(inbound ? rule.SourcePort : rule.DestinationPort);
                    var localPorts = Ports(inbound ? rule.DestinationPort : rule.SourcePort);

                    var remote = Ranges([remoteSpec]);
                    if (rule.Action == RuleAction.Deny)
                    {
                        remote = IpRanges.Subtract(remote, exempt);
                        if (remote.Count == 0)
                        {
                            warnings.Add($"'{label}' only matches allowlisted addresses, so it is not enforced.");
                            continue;
                        }
                    }

                    foreach (var protocol in Protocols(rule.Protocol, remotePorts, localPorts))
                    {
                        var ports = protocol is "Tcp" or "Udp";
                        AddChunked(rules, remote, chunk =>
                        {
                            var r = new EnforcedRule(
                                $"SM-R-{rule.Id:N}-{dir}-{protocol}",
                                $"Security Manager: {label} ({dir}, {protocol.ToUpperInvariant()})",
                                rule.Description ?? $"Policy '{policy.Name}', rule '{rule.Name}'.",
                                dir, rule.Action == RuleAction.Allow ? "Allow" : "Block", protocol,
                                AddressText(localSpec), chunk, ports ? localPorts : "*", ports ? remotePorts : "*", label);
                            planned.Add((r, order, label, remote, ports ? localPorts : "*"));
                            return r;
                        });
                    }
                }
            }

            if (policy.DefaultAction == RuleAction.Allow)
                warnings.Add($"Policy '{policy.Name}' defaults to Allow, but hosts keep their own default (Windows blocks unsolicited inbound traffic).");
        }

        // ---- Ordering conflicts that the host cannot express.
        foreach (var allow in planned.Where(p => p.Rule.Action == "Allow"))
        {
            var blocker = planned.FirstOrDefault(d => d.Rule.Action == "Block" && d.Order > allow.Order
                && d.Rule.Direction == allow.Rule.Direction && ProtocolsOverlap(d.Rule.Protocol, allow.Rule.Protocol)
                && PortsOverlap(d.Ports, allow.Ports) && IpRanges.Intersects(d.Remote, allow.Remote));
            if (blocker.Rule is not null)
            {
                var message = $"'{allow.Label}' (Allow) overlaps the later '{blocker.Label}' (Deny). Security Manager lets the Allow win, but the host firewall applies the Deny.";
                if (!warnings.Contains(message)) warnings.Add(message);
            }
        }

        if (enforced.Count == 0 && settings.EnforceBlocklist)
            warnings.Add("No enabled firewall policy; only the IP lists are enforced.");

        return Build(rules, warnings, true, now);
    }

    private static EnforcementDesiredState Build(List<EnforcedRule> rules, List<string> warnings, bool enabled, DateTime now)
    {
        var json = JsonSerializer.Serialize(new { enabled, rules });
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..16].ToLowerInvariant();
        return new EnforcementDesiredState(version, enabled, rules, warnings, now);
    }

    private static List<IpRange> Ranges(IEnumerable<string> specs) => IpRanges.Normalize(specs.SelectMany(IpRanges.Parse));

    private static string AddressText(string spec) => string.Join(",", IpRanges.ToText(IpRanges.Parse(spec)));

    private static string Ports(string spec) => PortSpec.IsAny(spec) ? "*" : string.Join(",", spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>Splits very large address sets over several rules (host firewalls limit list length).</summary>
    private static void AddChunked(List<EnforcedRule> rules, List<IpRange> ranges, Func<string, EnforcedRule> create)
    {
        if (ranges.Count == 0) return;
        var text = IpRanges.ToText(ranges);
        var chunks = text.Chunk(MaxAddressesPerRule).ToList();
        for (var i = 0; i < chunks.Count; i++)
        {
            var rule = create(string.Join(",", chunks[i]));
            rules.Add(chunks.Count == 1 ? rule : rule with { Key = $"{rule.Key}-{i + 1}", DisplayName = $"{rule.DisplayName} [{i + 1}/{chunks.Count}]" });
        }
    }

    private static IEnumerable<string> Directions(TrafficDirection policy, TrafficDirection rule)
    {
        bool Applies(TrafficDirection d) => (policy == TrafficDirection.Any || policy == d) && (rule == TrafficDirection.Any || rule == d);
        if (Applies(TrafficDirection.Inbound)) yield return "In";
        if (Applies(TrafficDirection.Outbound)) yield return "Out";
    }

    private static IEnumerable<string> Protocols(NetworkProtocol protocol, string remotePorts, string localPorts) => protocol switch
    {
        NetworkProtocol.Tcp => ["Tcp"],
        NetworkProtocol.Udp => ["Udp"],
        NetworkProtocol.Icmp => ["Icmpv4", "Icmpv6"],
        // Ports require a concrete protocol on host firewalls.
        _ => remotePorts == "*" && localPorts == "*" ? ["Any"] : ["Tcp", "Udp"]
    };

    private static bool ProtocolsOverlap(string a, string b) => a == "Any" || b == "Any" || a == b;

    private static bool PortsOverlap(string a, string b)
    {
        if (a == "*" || b == "*") return true;
        var ra = a.Split(',').Select(t => PortSpec.TryParseRange(t, out var lo, out var hi) ? (lo, hi) : (0, -1));
        var rb = b.Split(',').Select(t => PortSpec.TryParseRange(t, out var lo, out var hi) ? (lo, hi) : (0, -1)).ToList();
        return ra.Any(x => rb.Any(y => x.Item1 <= y.Item2 && y.Item1 <= x.Item2));
    }
}
