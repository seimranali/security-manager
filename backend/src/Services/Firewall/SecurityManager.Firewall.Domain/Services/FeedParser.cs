using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.Firewall.Domain.Services;

public sealed record ParsedFeed(IReadOnlyList<string> Addresses, int Skipped, IReadOnlyList<string> SkippedExamples);

/// <summary>
/// Parses common blocklist formats: one address per line with "#" or ";" comments
/// (FireHOL .netset, Spamhaus DROP "1.2.3.0/24 ; SBL123", ipdeny .zone, abuse.ch, Emerging Threats).
/// </summary>
public static class FeedParser
{
    /// <summary>
    /// Never put these into a feed-driven blocklist: many public lists include bogons (10/8, 192.168/16, ...) and
    /// applying them would cut the host off from its own LAN.
    /// </summary>
    private static readonly IReadOnlyList<IpRange> Reserved = IpRanges.Normalize(IpRanges.Parse(
        "0.0.0.0/8,10.0.0.0/8,100.64.0.0/10,127.0.0.0/8,169.254.0.0/16,172.16.0.0/12,192.0.0.0/24,192.168.0.0/16," +
        "198.18.0.0/15,224.0.0.0/3,::/128,::1/128,fc00::/7,fe80::/10,ff00::/8"));

    public static ParsedFeed Parse(string text, IpListType listType)
    {
        var result = new List<string>();
        var seen = new HashSet<string>();
        var skipped = 0;
        var examples = new List<string>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine;
            var comment = line.IndexOfAny(['#', ';']);
            if (comment >= 0) line = line[..comment];
            line = line.Trim();
            if (line.Length == 0) continue;

            // First token only: some lists append scores or labels after whitespace/commas.
            var token = IpListEntry.Normalize(line.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries)[0]);
            if (!seen.Add(token)) continue;

            string? reason = IpListEntry.Validate(token, listType);
            if (reason is null && listType == IpListType.Block && IpRanges.Intersects(IpRanges.Parse(token), Reserved))
                reason = "private or reserved";
            if (reason is not null)
            {
                skipped++;
                if (examples.Count < 20) examples.Add(token);
                continue;
            }
            result.Add(token);
        }
        return new ParsedFeed(result, skipped, examples);
    }
}
