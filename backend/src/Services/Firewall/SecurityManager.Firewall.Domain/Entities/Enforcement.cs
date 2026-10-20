using SecurityManager.Shared.Kernel.Entities;

namespace SecurityManager.Firewall.Domain.Entities;

/// <summary>Global switches for host enforcement (single row).</summary>
public class EnforcementSettings : BaseEntity
{
    /// <summary>Master switch; when off, agents remove every rule they manage.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Block active blocklist entries on hosts (inbound and outbound).</summary>
    public bool EnforceBlocklist { get; set; } = true;
    /// <summary>Open all inbound ports to allowlisted addresses. Off by default because it widens access.</summary>
    public bool EnforceAllowlist { get; set; }
}

/// <summary>A host running the enforcement agent, as last reported.</summary>
public class EnforcementAgent : BaseEntity
{
    public string AgentId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
    public string Mode { get; set; } = "Enforce";
    public string Status { get; set; } = "Pending";
    public string? AppliedVersion { get; set; }
    public int ManagedRuleCount { get; set; }
    public int LastCreated { get; set; }
    public int LastUpdated { get; set; }
    public int LastRemoved { get; set; }
    public int IntervalSeconds { get; set; } = 10;
    public DateTime LastSeenAt { get; set; }
    public DateTime? LastChangeAt { get; set; }
    /// <summary>JSON arrays (small, display-only).</summary>
    public string ProfilesJson { get; set; } = "[]";
    public string ErrorsJson { get; set; } = "[]";
    public string WarningsJson { get; set; } = "[]";

    /// <summary>Online while reports keep arriving (3 missed intervals = offline).</summary>
    public bool IsOnline(DateTime now) => now - LastSeenAt <= TimeSpan.FromSeconds(Math.Max(30, IntervalSeconds * 3));
}
