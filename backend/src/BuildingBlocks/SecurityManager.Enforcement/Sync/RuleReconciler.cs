using System.Security.Cryptography;
using System.Text;
using SecurityManager.Shared.Kernel.Contracts;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.Enforcement.Sync;

public sealed record ReconcilePlan(IReadOnlyList<(EnforcedRule Rule, string Description)> ToAdd, IReadOnlyList<string> ToRemove, int Updated, int Unchanged);

/// <summary>Pure diff between the desired rule set and the rules installed in the managed group.</summary>
public static class RuleReconciler
{
    public const string ManagedGroup = "Security Manager (managed)";
    private const string Marker = "[sm:";

    /// <summary>Fingerprint of everything that affects enforcement; stored in the rule description.</summary>
    public static string Fingerprint(EnforcedRule r)
    {
        var text = string.Join('|', r.Direction, r.Action, r.Protocol, r.LocalAddresses, r.RemoteAddresses, r.LocalPorts, r.RemotePorts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12].ToLowerInvariant();
    }

    public static string DescriptionFor(EnforcedRule r)
    {
        var text = $"{r.DisplayName} — {r.Description}";
        if (text.Length > 900) text = text[..900];
        return $"{text} {Marker}{Fingerprint(r)}]";
    }

    public static string? ReadFingerprint(string description)
    {
        var at = description.LastIndexOf(Marker, StringComparison.Ordinal);
        return at < 0 ? null : description[(at + Marker.Length)..].TrimEnd(']', ' ');
    }

    public static ReconcilePlan Plan(IReadOnlyList<EnforcedRule> desired, IReadOnlyList<(string Name, string Description)> installed)
    {
        var installedByName = installed.GroupBy(i => i.Name).ToDictionary(g => g.Key, g => g.ToList());
        var toAdd = new List<(EnforcedRule, string)>();
        var toRemove = new List<string>();
        int updated = 0, unchanged = 0;

        foreach (var rule in desired)
        {
            if (installedByName.TryGetValue(rule.Key, out var existing))
            {
                if (existing.Count == 1 && ReadFingerprint(existing[0].Description) == Fingerprint(rule))
                {
                    unchanged++;
                    continue;
                }
                toRemove.AddRange(existing.Select(_ => rule.Key)); // duplicates or changed: replace
                updated++;
            }
            toAdd.Add((rule, DescriptionFor(rule)));
        }

        var desiredKeys = desired.Select(d => d.Key).ToHashSet();
        toRemove.AddRange(installed.Where(i => !desiredKeys.Contains(i.Name)).Select(i => i.Name));
        return new ReconcilePlan(toAdd, toRemove, updated, unchanged);
    }

    /// <summary>Inbound Allow rules whose ports are also blocked by a host rule outside Security Manager (block wins).</summary>
    public static IEnumerable<string> FindShadowedAllows(IReadOnlyList<EnforcedRule> desired, string firewallName, IEnumerable<(string Name, string Protocol, string LocalPorts)> foreignBlocks)
    {
        var blocks = foreignBlocks.ToList();
        foreach (var allow in desired.Where(r => r.Direction == "In" && r.Action == "Allow"))
        {
            foreach (var block in blocks)
            {
                if ((block.Protocol == "Any" || allow.Protocol == "Any" || block.Protocol == allow.Protocol) && PortsOverlap(block.LocalPorts, allow.LocalPorts))
                {
                    yield return $"{firewallName} rule '{block.Name}' (not managed by Security Manager) blocks {block.Protocol.ToUpperInvariant()} port(s) {block.LocalPorts} and overrides '{allow.Origin}'. Remove or disable it to let the Allow take effect.";
                }
            }
        }
    }

    private static bool PortsOverlap(string a, string b)
    {
        if (a is "*" or "Any" || b is "*" or "Any") return true;
        static List<(int Lo, int Hi)> Parse(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => PortSpec.TryParseRange(t, out var lo, out var hi) ? (lo, hi) : (-1, -2)).Where(x => x.Item1 >= 0).ToList();
        var pb = Parse(b);
        return Parse(a).Any(x => pb.Any(y => x.Lo <= y.Hi && y.Lo <= x.Hi));
    }
}
