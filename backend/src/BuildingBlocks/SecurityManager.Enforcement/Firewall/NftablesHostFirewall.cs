using System.Diagnostics;
using System.Text.Json;
using SecurityManager.Shared.Kernel.Contracts;

namespace SecurityManager.Enforcement.Firewall;

/// <summary>
/// Linux host firewall through nftables (the <c>nft</c> command). Security Manager owns a dedicated table,
/// <c>inet security_manager</c>, and never touches other tables (ufw, firewalld, Docker, ...).
/// Requires root (or CAP_NET_ADMIN); the agent runs as a root systemd service.
/// </summary>
/// <remarks>
/// nftables evaluates every table: a drop anywhere is final, while an accept only ends the current table.
/// So Security Manager's blocks always take effect, but its allows cannot open ports another table drops.
/// Unlike Windows, the host's default stays "accept" so that enabling enforcement can never lock you out over SSH.
/// </remarks>
public sealed class NftablesHostFirewall(string nftPath = "nft") : IHostFirewall
{
    private const string ProfileName = "nftables";
    private static readonly string[] ManagedChains = ["block_in", "allow_in", "block_out", "allow_out"];

    public string Name => "nftables";

    public bool IsSupported => OperatingSystem.IsLinux() && Run("--version", null, throwOnError: false).ExitCode == 0;

    public IReadOnlyList<InstalledRule> GetManagedRules(string group)
    {
        // One enforced rule may be installed as two nft rules (IPv4 + IPv6); report each key once and
        // flag a mismatch by joining comments so the reconciler replaces the set.
        return ListRules()
            .GroupBy(r => NftRuleBuilder.KeyFromComment(r.Comment))
            .Select(g => new InstalledRule(g.Key, g.Select(r => r.Comment).Distinct().Count() == 1 ? g.First().Comment : string.Empty))
            .ToList();
    }

    public void AddRule(EnforcedRule rule, string group, string description)
    {
        EnsureSkeleton();
        var lines = NftRuleBuilder.Build(rule, description)
            .Select(r => $"add rule {NftRuleBuilder.Family} {NftRuleBuilder.Table} {r.Chain} {r.Text}");
        RunScript(string.Join('\n', lines));
    }

    public void RemoveRule(string name)
    {
        var lines = ListRules()
            .Where(r => NftRuleBuilder.KeyFromComment(r.Comment) == name)
            .Select(r => $"delete rule {NftRuleBuilder.Family} {NftRuleBuilder.Table} {r.Chain} handle {r.Handle}")
            .ToList();
        if (lines.Count > 0) RunScript(string.Join('\n', lines));
    }

    /// <summary>Linux has no firewall profiles; one pseudo-profile reports whether the managed table is installed.</summary>
    public IReadOnlyList<AgentFirewallProfile> GetProfiles() =>
        [new AgentFirewallProfile(ProfileName, Run($"list table {NftRuleBuilder.Family} {NftRuleBuilder.Table}", null, throwOnError: false).ExitCode == 0)];

    public void EnableProfile(string name) => EnsureSkeleton();

    /// <summary>Inbound drop/reject rules in tables Security Manager does not own (e.g. ufw, firewalld).</summary>
    public IReadOnlyList<ForeignBlockRule> GetForeignInboundBlockRules(string managedGroup)
    {
        var result = new List<ForeignBlockRule>();
        var output = Run("-j list ruleset", null, throwOnError: false);
        if (output.ExitCode != 0) return result;

        using var doc = JsonDocument.Parse(output.StdOut);
        var inputChains = new HashSet<(string Family, string Table, string Chain)>();
        foreach (var item in doc.RootElement.GetProperty("nftables").EnumerateArray())
        {
            if (item.TryGetProperty("chain", out var c) && c.TryGetProperty("hook", out var hook) && hook.GetString() == "input")
                inputChains.Add((c.GetProperty("family").GetString()!, c.GetProperty("table").GetString()!, c.GetProperty("name").GetString()!));
        }
        foreach (var item in doc.RootElement.GetProperty("nftables").EnumerateArray())
        {
            if (!item.TryGetProperty("rule", out var r)) continue;
            var table = r.GetProperty("table").GetString()!;
            if (table == NftRuleBuilder.Table) continue;
            if (!inputChains.Contains((r.GetProperty("family").GetString()!, table, r.GetProperty("chain").GetString()!))) continue;
            var text = r.GetProperty("expr").GetRawText();
            if (!text.Contains("\"drop\"") && !text.Contains("\"reject\"")) continue;
            // Only unconditional drops are reported; anything with a match is too varied to interpret reliably.
            if (text.Contains("\"match\"")) continue;
            result.Add(new ForeignBlockRule($"{table}/{r.GetProperty("chain").GetString()}", "Any", "*"));
        }
        return result;
    }

    // ------------------------------------------------------------------ helpers

    private sealed record ListedRule(string Chain, long Handle, string Comment);

    private List<ListedRule> ListRules()
    {
        var output = Run($"-j list table {NftRuleBuilder.Family} {NftRuleBuilder.Table}", null, throwOnError: false);
        if (output.ExitCode != 0) return []; // table not created yet

        var rules = new List<ListedRule>();
        using var doc = JsonDocument.Parse(output.StdOut);
        foreach (var item in doc.RootElement.GetProperty("nftables").EnumerateArray())
        {
            if (!item.TryGetProperty("rule", out var r)) continue;
            var chain = r.GetProperty("chain").GetString()!;
            if (!ManagedChains.Contains(chain) || !r.TryGetProperty("comment", out var comment)) continue;
            rules.Add(new ListedRule(chain, r.GetProperty("handle").GetInt64(), comment.GetString() ?? string.Empty));
        }
        return rules;
    }

    private void EnsureSkeleton()
    {
        // "add" is idempotent for tables/chains; the base chains are re-flushed and rebuilt so their jump order is always right.
        RunScript(NftRuleBuilder.SkeletonScript());
    }

    private void RunScript(string script) => Run("-f -", script, throwOnError: true);

    private (int ExitCode, string StdOut) Run(string arguments, string? stdin, bool throwOnError)
    {
        var psi = new ProcessStartInfo(nftPath, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false
        };
        Process process;
        try { process = Process.Start(psi)!; }
        catch (Exception ex) when (!throwOnError) { _ = ex; return (-1, string.Empty); }
        catch (Exception ex) { throw new InvalidOperationException($"Could not run '{nftPath}': {ex.Message}. Install the nftables package.", ex); }

        using (process)
        {
            if (stdin is not null)
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 && throwOnError)
            {
                var message = stderr.Trim();
                if (message.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException(message);
                throw new InvalidOperationException($"nft failed: {message}");
            }
            return (process.ExitCode, stdout.Result);
        }
    }
}
