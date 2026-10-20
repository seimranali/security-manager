namespace SecurityManager.Shared.Kernel.Contracts;

/// <summary>
/// Platform-neutral host firewall rule produced by the Firewall service and applied by enforcement agents.
/// Addresses and ports use "*" for any; lists are comma-separated.
/// </summary>
public sealed record EnforcedRule(
    string Key,
    string DisplayName,
    string Description,
    string Direction,   // In | Out
    string Action,      // Allow | Block
    string Protocol,    // Any | Tcp | Udp | Icmpv4 | Icmpv6
    string LocalAddresses,
    string RemoteAddresses,
    string LocalPorts,
    string RemotePorts,
    string Origin);     // what produced it, e.g. "IP blocklist" or "Policy / Rule"

/// <summary>The complete rule set every enforcement agent should converge to.</summary>
public sealed record EnforcementDesiredState(
    string Version,
    bool Enabled,
    IReadOnlyList<EnforcedRule> Rules,
    IReadOnlyList<string> Warnings,
    DateTime GeneratedAt);

public sealed record AgentFirewallProfile(string Name, bool Enabled);

/// <summary>Status reported by an agent after each sync.</summary>
public sealed record EnforcementAgentReport(
    string AgentId,
    string Hostname,
    string OperatingSystem,
    string AgentVersion,
    string Mode,              // Enforce | Audit
    string Status,            // InSync | Pending | Error | Disabled
    string? AppliedVersion,
    int ManagedRuleCount,
    int Created,
    int Updated,
    int Removed,
    int IntervalSeconds,
    IReadOnlyList<AgentFirewallProfile> Profiles,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
