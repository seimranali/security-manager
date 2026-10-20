using System.Runtime.Versioning;
using SecurityManager.Shared.Kernel.Contracts;

namespace SecurityManager.Enforcement.Firewall;

/// <summary>
/// Windows Defender Firewall through its COM API (HNetCfg.FwPolicy2). Reading works for any user;
/// changing rules or profiles requires administrator rights (the agent runs as LocalSystem).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHostFirewall : IHostFirewall
{
    private const int DirectionIn = 1, DirectionOut = 2;
    private const int ActionBlock = 0, ActionAllow = 1;
    private const int AllProfiles = 0x7FFFFFFF;

    private static readonly (string Name, int Type)[] Profiles = [("Domain", 1), ("Private", 2), ("Public", 4)];

    public string Name => "Windows Firewall";
    public bool IsSupported => true;

    private static dynamic Policy() =>
        Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!)!;

    public IReadOnlyList<InstalledRule> GetManagedRules(string group)
    {
        var result = new List<InstalledRule>();
        foreach (dynamic rule in Policy().Rules)
        {
            string? grouping = rule.Grouping;
            if (grouping == group) result.Add(new InstalledRule((string)rule.Name, (string?)rule.Description ?? string.Empty));
        }
        return result;
    }

    public void AddRule(EnforcedRule r, string group, string description)
    {
        dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", throwOnError: true)!)!;
        rule.Name = r.Key;
        rule.Description = description;
        rule.Grouping = group;
        rule.Direction = r.Direction == "In" ? DirectionIn : DirectionOut;
        rule.Action = r.Action == "Allow" ? ActionAllow : ActionBlock;
        rule.Protocol = r.Protocol switch { "Tcp" => 6, "Udp" => 17, "Icmpv4" => 1, "Icmpv6" => 58, _ => 256 };
        if (r.Protocol is "Tcp" or "Udp")
        {
            rule.LocalPorts = r.LocalPorts;
            rule.RemotePorts = r.RemotePorts;
        }
        rule.LocalAddresses = r.LocalAddresses;
        rule.RemoteAddresses = r.RemoteAddresses;
        rule.Profiles = AllProfiles;
        rule.Enabled = true;
        Policy().Rules.Add(rule);
    }

    public void RemoveRule(string name) => Policy().Rules.Remove(name);

    public IReadOnlyList<AgentFirewallProfile> GetProfiles()
    {
        var policy = Policy();
        return Profiles.Select(p => new AgentFirewallProfile(p.Name, (bool)policy.FirewallEnabled[p.Type])).ToList();
    }

    public void EnableProfile(string name)
    {
        var type = Profiles.First(p => p.Name == name).Type;
        var policy = Policy();
        policy.FirewallEnabled[type] = true;
    }

    public IReadOnlyList<ForeignBlockRule> GetForeignInboundBlockRules(string managedGroup)
    {
        var result = new List<ForeignBlockRule>();
        foreach (dynamic rule in Policy().Rules)
        {
            if (!(bool)rule.Enabled || (int)rule.Direction != DirectionIn || (int)rule.Action != ActionBlock) continue;
            string? grouping = rule.Grouping;
            if (grouping == managedGroup) continue;
            int protocol = rule.Protocol;
            if (protocol is not (6 or 17 or 256)) continue;
            string ports = protocol == 256 ? "*" : (string?)rule.LocalPorts ?? "*";
            result.Add(new ForeignBlockRule((string)rule.Name, protocol switch { 6 => "Tcp", 17 => "Udp", _ => "Any" }, ports));
        }
        return result;
    }
}
