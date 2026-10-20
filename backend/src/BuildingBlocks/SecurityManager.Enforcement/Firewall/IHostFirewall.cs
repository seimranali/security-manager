using SecurityManager.Shared.Kernel.Contracts;

namespace SecurityManager.Enforcement.Firewall;

/// <summary>A rule installed on the host that belongs to Security Manager's managed group.</summary>
public sealed record InstalledRule(string Name, string Description);

/// <summary>An enabled inbound block rule that Security Manager does not manage.</summary>
public sealed record ForeignBlockRule(string Name, string Protocol, string LocalPorts);

public interface IHostFirewall
{
    /// <summary>Human-readable name used in status messages, e.g. "Windows Firewall" or "nftables".</summary>
    string Name { get; }
    bool IsSupported { get; }
    IReadOnlyList<InstalledRule> GetManagedRules(string group);
    void AddRule(EnforcedRule rule, string group, string description);
    void RemoveRule(string name);
    IReadOnlyList<AgentFirewallProfile> GetProfiles();
    void EnableProfile(string name);
    IReadOnlyList<ForeignBlockRule> GetForeignInboundBlockRules(string managedGroup);
}

/// <summary>Fallback for operating systems without a supported firewall backend (e.g. macOS).</summary>
public sealed class UnsupportedHostFirewall : IHostFirewall
{
    public string Name => "host firewall";
    public bool IsSupported => false;
    public IReadOnlyList<InstalledRule> GetManagedRules(string group) => [];
    public void AddRule(EnforcedRule rule, string group, string description) => throw new PlatformNotSupportedException();
    public void RemoveRule(string name) => throw new PlatformNotSupportedException();
    public IReadOnlyList<AgentFirewallProfile> GetProfiles() => [];
    public void EnableProfile(string name) => throw new PlatformNotSupportedException();
    public IReadOnlyList<ForeignBlockRule> GetForeignInboundBlockRules(string managedGroup) => [];
}
