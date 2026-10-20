namespace SecurityManager.Agent;

public sealed class AgentOptions
{
    public const string Section = "Agent";

    /// <summary>Unique id of this host (defaults to the machine name).</summary>
    public string? AgentId { get; set; }

    /// <summary>Firewall service base URL (direct, not through the gateway).</summary>
    public string FirewallServiceUrl { get; set; } = "http://localhost:5101/";

    public int IntervalSeconds { get; set; } = 10;

    /// <summary>Full re-check of installed rules even when the desired version is unchanged (detects manual edits).</summary>
    public int ReconcileMinutes { get; set; } = 5;

    /// <summary>"Enforce" applies changes; "Audit" only reports what would change.</summary>
    public string Mode { get; set; } = "Enforce";

    /// <summary>Linux: path of the nft command.</summary>
    public string NftPath { get; set; } = "nft";

    /// <summary>Turn the host firewall on (Windows profiles; on Linux, create the managed nftables table) for profiles where it is off (rules have no effect otherwise).</summary>
    public bool EnsureFirewallEnabled { get; set; } = true;

    public bool IsAudit => string.Equals(Mode, "Audit", StringComparison.OrdinalIgnoreCase);
    public string ResolvedAgentId => string.IsNullOrWhiteSpace(AgentId) ? Environment.MachineName : AgentId.Trim();
}
