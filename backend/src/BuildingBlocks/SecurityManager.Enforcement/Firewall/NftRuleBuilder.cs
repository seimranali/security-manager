using SecurityManager.Shared.Kernel.Contracts;

namespace SecurityManager.Enforcement.Firewall;

/// <summary>
/// Translates an <see cref="EnforcedRule"/> into nftables rule statements (pure, no I/O, unit-tested).
/// Layout of the managed table:
/// <code>
/// table inet security_manager
///   chain input  { type filter hook input  priority 0; policy accept; jump block_in;  jump allow_in }
///   chain output { type filter hook output priority 0; policy accept; jump block_out; jump allow_out }
/// </code>
/// Block chains run first so a block always beats an allow, matching Windows Firewall semantics.
/// </summary>
public static class NftRuleBuilder
{
    public const string Table = "security_manager";
    public const string Family = "inet";

    /// <summary>One nft rule: the chain it goes into and the rule text (matches + verdict + comment).</summary>
    public sealed record NftRule(string Chain, string Text);

    public static string ChainFor(EnforcedRule r) =>
        $"{(r.Action == "Allow" ? "allow" : "block")}_{(r.Direction == "In" ? "in" : "out")}";

    /// <summary>Script that (re)creates the table skeleton if missing. Safe to run repeatedly.</summary>
    public static string SkeletonScript() => $$"""
        add table {{Family}} {{Table}}
        add chain {{Family}} {{Table}} block_in
        add chain {{Family}} {{Table}} allow_in
        add chain {{Family}} {{Table}} block_out
        add chain {{Family}} {{Table}} allow_out
        add chain {{Family}} {{Table}} input { type filter hook input priority 0; policy accept; }
        add chain {{Family}} {{Table}} output { type filter hook output priority 0; policy accept; }
        flush chain {{Family}} {{Table}} input
        flush chain {{Family}} {{Table}} output
        add rule {{Family}} {{Table}} input ct state established,related accept
        add rule {{Family}} {{Table}} input iif "lo" accept
        add rule {{Family}} {{Table}} input jump block_in
        add rule {{Family}} {{Table}} input jump allow_in
        add rule {{Family}} {{Table}} output ct state established,related accept
        add rule {{Family}} {{Table}} output oif "lo" accept
        add rule {{Family}} {{Table}} output jump block_out
        add rule {{Family}} {{Table}} output jump allow_out
        """;

    /// <summary>
    /// nft comments hold at most 128 bytes, so only the rule key and the reconciler's fingerprint marker are stored.
    /// </summary>
    public static string Comment(string key, string description)
    {
        var at = description.LastIndexOf("[sm:", StringComparison.Ordinal);
        var marker = at < 0 ? string.Empty : " " + description[at..];
        var text = key + marker;
        return text.Length <= 120 ? text : text[..120];
    }

    /// <summary>Key part of a stored comment.</summary>
    public static string KeyFromComment(string comment)
    {
        var space = comment.IndexOf(' ');
        return space < 0 ? comment : comment[..space];
    }

    /// <summary>
    /// Rules for one enforced rule. Mixed IPv4/IPv6 address lists become one rule per family (they share the comment).
    /// </summary>
    public static IReadOnlyList<NftRule> Build(EnforcedRule r, string description)
    {
        var chain = ChainFor(r);
        var inbound = r.Direction == "In";
        var verdict = r.Action == "Allow" ? "accept" : "drop";
        var comment = Comment(r.Key, description).Replace("\"", "'");

        var remote = SplitByFamily(r.RemoteAddresses);
        var local = SplitByFamily(r.LocalAddresses);

        // Families the rule applies to: restricted by whichever side lists specific addresses.
        var families = new List<string>();
        foreach (var fam in new[] { "ip", "ip6" })
        {
            if (remote.Any && local.Any) { families.Add(fam); continue; }
            var remoteOk = remote.Any || remote.For(fam).Count > 0;
            var localOk = local.Any || local.For(fam).Count > 0;
            if (remoteOk && localOk) families.Add(fam);
        }
        // ICMP is family-specific.
        if (r.Protocol == "Icmpv4") families.RemoveAll(f => f != "ip");
        if (r.Protocol == "Icmpv6") families.RemoveAll(f => f != "ip6");

        var result = new List<NftRule>();
        var anyAddress = remote.Any && local.Any;
        foreach (var fam in anyAddress ? [""] : families.ToArray())
        {
            var parts = new List<string>();
            if (fam != "")
            {
                if (!remote.Any) parts.Add($"{fam} {(inbound ? "saddr" : "daddr")} {Set(remote.For(fam))}");
                if (!local.Any) parts.Add($"{fam} {(inbound ? "daddr" : "saddr")} {Set(local.For(fam))}");
            }

            switch (r.Protocol)
            {
                case "Tcp" or "Udp":
                    var proto = r.Protocol.ToLowerInvariant();
                    var localPorts = Ports(r.LocalPorts);
                    var remotePorts = Ports(r.RemotePorts);
                    if (localPorts is null && remotePorts is null) parts.Add($"meta l4proto {proto}");
                    if (localPorts is not null) parts.Add($"{proto} {(inbound ? "dport" : "sport")} {localPorts}");
                    if (remotePorts is not null) parts.Add($"{proto} {(inbound ? "sport" : "dport")} {remotePorts}");
                    break;
                case "Icmpv4": parts.Add("meta l4proto icmp"); break;
                case "Icmpv6": parts.Add("meta l4proto ipv6-icmp"); break;
            }

            parts.Add(verdict);
            parts.Add($"comment \"{comment}\"");
            result.Add(new NftRule(chain, string.Join(' ', parts)));
        }
        return result;
    }

    private sealed record FamilySplit(bool Any, List<string> V4, List<string> V6)
    {
        public List<string> For(string family) => family == "ip" ? V4 : V6;
    }

    private static FamilySplit SplitByFamily(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec) || spec.Trim() is "*" or "Any") return new FamilySplit(true, [], []);
        var tokens = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new FamilySplit(false, tokens.Where(t => !t.Contains(':')).ToList(), tokens.Where(t => t.Contains(':')).ToList());
    }

    private static string Set(List<string> items) => items.Count == 1 ? items[0] : $"{{ {string.Join(", ", items)} }}";

    private static string? Ports(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec) || spec.Trim() is "*" or "Any") return null;
        var items = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return Set(items);
    }
}
