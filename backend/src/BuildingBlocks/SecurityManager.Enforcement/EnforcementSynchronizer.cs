using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecurityManager.Enforcement.Firewall;
using SecurityManager.Enforcement.Sync;
using SecurityManager.Shared.Kernel.Contracts;

namespace SecurityManager.Enforcement;

/// <summary>How the synchronizer applies the desired state.</summary>
public sealed class EnforcementOptions
{
    /// <summary>"Enforce" applies changes; "Audit" only reports what would change.</summary>
    public string Mode { get; set; } = "Enforce";

    /// <summary>Turn the host firewall on (Windows profiles; on Linux, create the managed nftables table).</summary>
    public bool EnsureFirewallEnabled { get; set; } = true;

    /// <summary>Linux: path of the nft command.</summary>
    public string NftPath { get; set; } = "nft";

    public bool IsAudit => string.Equals(Mode, "Audit", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Picks the firewall backend for this operating system.</summary>
public static class HostFirewallFactory
{
    public static IHostFirewall Create(string nftPath = "nft") =>
        OperatingSystem.IsWindows() ? new WindowsHostFirewall()
        : OperatingSystem.IsLinux() ? new NftablesHostFirewall(nftPath)
        : new UnsupportedHostFirewall();
}

public sealed record SyncResult(string Status, string? AppliedVersion, int ManagedRules, int Created, int Updated, int Removed,
    IReadOnlyList<AgentFirewallProfile> Profiles, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>Brings the host firewall in line with Security Manager's desired state.</summary>
public sealed class EnforcementSynchronizer(IHostFirewall firewall, IOptions<EnforcementOptions> options, ILogger<EnforcementSynchronizer> logger)
{
    private readonly EnforcementOptions _options = options.Value;

    public SyncResult Apply(EnforcementDesiredState desired)
    {
        var errors = new List<string>();
        var warnings = new List<string>(desired.Warnings);

        if (!firewall.IsSupported)
            return new SyncResult("Error", null, 0, 0, 0, 0, [], [$"No supported firewall on this host ({System.Runtime.InteropServices.RuntimeInformation.OSDescription}). Windows uses Windows Firewall; Linux needs nftables (the nft command) installed."], warnings);

        var profiles = firewall.GetProfiles().ToList();
        foreach (var off in profiles.Where(p => !p.Enabled).ToList())
        {
            if (_options.EnsureFirewallEnabled && !_options.IsAudit && desired.Enabled)
            {
                try
                {
                    firewall.EnableProfile(off.Name);
                    logger.LogWarning("Enabled {Firewall} for the {Profile} profile", firewall.Name, off.Name);
                    warnings.Add($"{firewall.Name} was off for the {off.Name} profile and has been turned on.");
                }
                catch (Exception ex) { errors.Add($"Could not enable the {off.Name} firewall profile: {Describe(ex)}"); }
            }
            else warnings.Add($"{firewall.Name} is OFF for the {off.Name} profile: rules are not enforced there.");
        }
        profiles = firewall.GetProfiles().ToList();

        var desiredRules = desired.Enabled ? desired.Rules : [];
        var installed = firewall.GetManagedRules(RuleReconciler.ManagedGroup);
        var plan = RuleReconciler.Plan(desiredRules, installed.Select(i => (i.Name, i.Description)).ToList());

        int created = 0, removed = 0;
        if (_options.IsAudit)
        {
            if (plan.ToAdd.Count + plan.ToRemove.Count > 0)
                warnings.Add($"Audit mode: would add {plan.ToAdd.Count - plan.Updated}, update {plan.Updated} and remove {plan.ToRemove.Count - plan.Updated} rule(s). No changes were made.");
        }
        else
        {
            foreach (var name in plan.ToRemove)
            {
                try { firewall.RemoveRule(name); removed++; }
                catch (Exception ex) { errors.Add($"Remove '{name}': {Describe(ex)}"); }
            }
            foreach (var (rule, description) in plan.ToAdd)
            {
                try { firewall.AddRule(rule, RuleReconciler.ManagedGroup, description); created++; }
                catch (Exception ex) { errors.Add($"Add '{rule.DisplayName}': {Describe(ex)}"); }
            }
            if (created + removed > 0)
                logger.LogInformation("Applied desired state {Version}: {Created} added, {Removed} removed", desired.Version, created, removed);
        }

        try
        {
            warnings.AddRange(RuleReconciler.FindShadowedAllows(desiredRules, firewall.Name,
                firewall.GetForeignInboundBlockRules(RuleReconciler.ManagedGroup).Select(f => (f.Name, f.Protocol, f.LocalPorts))).Distinct());
        }
        catch (Exception ex) { logger.LogDebug(ex, "Could not inspect foreign rules"); }

        var managed = firewall.GetManagedRules(RuleReconciler.ManagedGroup).Count;
        var pending = _options.IsAudit && plan.ToAdd.Count + plan.ToRemove.Count > 0;
        var status = errors.Count > 0 ? "Error" : !desired.Enabled ? "Disabled" : pending ? "Pending" : "InSync";
        var updatedCount = Math.Min(plan.Updated, created);
        return new SyncResult(status, errors.Count == 0 && !pending ? desired.Version : null, managed,
            created - updatedCount, updatedCount, removed - updatedCount, profiles, errors, warnings);
    }

    private static string Describe(Exception ex) => ex is UnauthorizedAccessException || (ex is COMException com && com.HResult == unchecked((int)0x80070005))
        ? OperatingSystem.IsWindows()
            ? "access denied - changing Windows Firewall needs administrator rights (install Security Manager as Windows services: Install-Services.ps1)"
            : "permission denied - changing nftables needs root / CAP_NET_ADMIN (install with install.sh)"
        : ex.Message;
}
