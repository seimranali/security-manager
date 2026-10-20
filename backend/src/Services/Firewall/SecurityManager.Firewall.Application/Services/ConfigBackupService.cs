using SecurityManager.Firewall.Application.Abstractions;
using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Firewall.Domain.Services;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Firewall.Application.Services;

/// <summary>Portable snapshot of the firewall configuration (no traffic history or counters).</summary>
public sealed class FirewallConfigBackup
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public DateTime ExportedAt { get; set; }
    public string? ExportedBy { get; set; }
    public BackupSettings? Enforcement { get; set; }
    public List<BackupPolicy> Policies { get; set; } = [];
    /// <summary>Manually managed entries only: feed and detection entries are regenerated automatically.</summary>
    public List<BackupIpEntry> IpList { get; set; } = [];
    public List<SaveThreatFeedRequest> Feeds { get; set; } = [];
}

public sealed record BackupSettings(bool Enabled, bool EnforceBlocklist, bool EnforceAllowlist);

public sealed class BackupPolicy
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; }
    public int Priority { get; set; }
    public TrafficDirection Direction { get; set; }
    public RuleAction DefaultAction { get; set; }
    public bool EnforceOnHosts { get; set; }
    public List<BackupRule> Rules { get; set; } = [];
}

public sealed class BackupRule
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Priority { get; set; }
    public RuleAction Action { get; set; }
    public TrafficDirection Direction { get; set; }
    public NetworkProtocol Protocol { get; set; }
    public string SourceAddress { get; set; } = "any";
    public string DestinationAddress { get; set; } = "any";
    public string SourcePort { get; set; } = "any";
    public string DestinationPort { get; set; } = "any";
    public bool IsEnabled { get; set; }
}

public sealed record BackupIpEntry(IpListType ListType, string Address, string? Description, DateTime? ExpiresAt, bool IsEnabled);

public sealed record ConfigRestoreResultDto(int Policies, int Rules, int IpEntries, int Feeds, bool Replaced);

public interface IConfigBackupService
{
    Task<FirewallConfigBackup> ExportAsync(string? actor, CancellationToken ct = default);
    /// <summary>
    /// Restores a backup. <paramref name="replace"/> = true deletes current policies, manual IP entries and feeds first;
    /// otherwise items are merged (policies/feeds with the same name/URL are updated, new ones added).
    /// </summary>
    Task<ConfigRestoreResultDto> RestoreAsync(FirewallConfigBackup backup, bool replace, CancellationToken ct = default);
}

public sealed class ConfigBackupService(IFirewallUnitOfWork uow) : IConfigBackupService
{
    public async Task<FirewallConfigBackup> ExportAsync(string? actor, CancellationToken ct = default)
    {
        var policies = await uow.Policies.ListAsync(orderBy: o => o.OrderBy(p => p.Priority), includes: [p => p.Rules], ct: ct);
        var settings = (await uow.EnforcementSettings.ListAsync(take: 1, ct: ct)).FirstOrDefault();
        var manual = await uow.IpList.ListAsync(e => e.Source == IpListSource.Manual || e.Source == IpListSource.Import, o => o.OrderBy(e => e.Address), ct: ct);
        var feeds = await uow.ThreatFeeds.ListAsync(orderBy: o => o.OrderBy(f => f.Name), ct: ct);

        return new FirewallConfigBackup
        {
            ExportedAt = DateTime.UtcNow,
            ExportedBy = actor,
            Enforcement = settings is null ? null : new BackupSettings(settings.Enabled, settings.EnforceBlocklist, settings.EnforceAllowlist),
            Policies = policies.Select(p => new BackupPolicy
            {
                Name = p.Name, Description = p.Description, IsEnabled = p.IsEnabled, Priority = p.Priority, Direction = p.Direction,
                DefaultAction = p.DefaultAction, EnforceOnHosts = p.EnforceOnHosts,
                Rules = p.Rules.OrderBy(r => r.Priority).Select(r => new BackupRule
                {
                    Name = r.Name, Description = r.Description, Priority = r.Priority, Action = r.Action, Direction = r.Direction,
                    Protocol = r.Protocol, SourceAddress = r.SourceAddress, DestinationAddress = r.DestinationAddress,
                    SourcePort = r.SourcePort, DestinationPort = r.DestinationPort, IsEnabled = r.IsEnabled
                }).ToList()
            }).ToList(),
            IpList = manual.Select(e => new BackupIpEntry(e.ListType, e.Address, e.Description, e.ExpiresAt, e.IsEnabled)).ToList(),
            Feeds = feeds.Select(f => new SaveThreatFeedRequest
            {
                Name = f.Name, Url = f.Url, Kind = f.Kind, CountryCode = f.CountryCode, ListType = f.ListType,
                IsEnabled = f.IsEnabled, RefreshMinutes = f.RefreshMinutes, MaxEntries = f.MaxEntries
            }).ToList()
        };
    }

    public async Task<ConfigRestoreResultDto> RestoreAsync(FirewallConfigBackup backup, bool replace, CancellationToken ct = default)
    {
        if (backup.Version is < 1 or > FirewallConfigBackup.CurrentVersion)
            throw new BusinessRuleException($"Unsupported backup version {backup.Version}.");
        Validate(backup);

        var policies = (await uow.Policies.ListAsync(includes: [p => p.Rules], ct: ct)).ToList();
        var feeds = (await uow.ThreatFeeds.ListAsync(ct: ct)).ToList();
        if (replace)
        {
            uow.Policies.RemoveRange(policies);
            uow.IpList.RemoveRange(await uow.IpList.ListAsync(e => e.Source == IpListSource.Manual || e.Source == IpListSource.Import
                                                                   || e.SourceReference != null && e.SourceReference.StartsWith("feed:"), ct: ct));
            uow.ThreatFeeds.RemoveRange(feeds);
            policies.Clear();
            feeds.Clear();
        }

        var ruleCount = 0;
        foreach (var bp in backup.Policies)
        {
            var policy = policies.FirstOrDefault(p => string.Equals(p.Name, bp.Name, StringComparison.OrdinalIgnoreCase));
            if (policy is null)
            {
                policy = new FirewallPolicy();
                await uow.Policies.AddAsync(policy, ct);
            }
            else
            {
                uow.Rules.RemoveRange(policy.Rules);
                policy.Rules.Clear();
            }
            policy.Name = bp.Name.Trim();
            policy.Description = bp.Description;
            policy.IsEnabled = bp.IsEnabled;
            policy.Priority = bp.Priority;
            policy.Direction = bp.Direction;
            policy.DefaultAction = bp.DefaultAction;
            policy.EnforceOnHosts = bp.EnforceOnHosts;
            foreach (var br in bp.Rules)
            {
                policy.Rules.Add(new FirewallRule
                {
                    Name = br.Name.Trim(), Description = br.Description, Priority = br.Priority, Action = br.Action, Direction = br.Direction,
                    Protocol = br.Protocol, SourceAddress = br.SourceAddress, DestinationAddress = br.DestinationAddress,
                    SourcePort = br.SourcePort, DestinationPort = br.DestinationPort, IsEnabled = br.IsEnabled
                });
                ruleCount++;
            }
        }

        var listed = (await uow.IpList.ListAsync(ct: ct)).Select(e => (e.ListType, e.Address)).ToHashSet();
        var ipCount = 0;
        foreach (var e in backup.IpList)
        {
            var address = IpListEntry.Normalize(e.Address);
            if (!listed.Add((e.ListType, address))) continue;
            await uow.IpList.AddAsync(new IpListEntry
            {
                ListType = e.ListType, Address = address, Description = e.Description, ExpiresAt = e.ExpiresAt,
                IsEnabled = e.IsEnabled, Source = IpListSource.Import, CreatedBy = "restore"
            }, ct);
            ipCount++;
        }

        foreach (var bf in backup.Feeds)
        {
            var feed = feeds.FirstOrDefault(f => f.Url == bf.Url && f.ListType == bf.ListType);
            if (feed is null)
            {
                feed = new ThreatFeed();
                await uow.ThreatFeeds.AddAsync(feed, ct);
            }
            feed.Name = bf.Name; feed.Url = bf.Url; feed.Kind = bf.Kind; feed.CountryCode = bf.CountryCode; feed.ListType = bf.ListType;
            feed.IsEnabled = bf.IsEnabled; feed.RefreshMinutes = bf.RefreshMinutes; feed.MaxEntries = bf.MaxEntries;
            feed.LastAttemptAt = null; // refreshed by the background worker shortly
        }

        if (backup.Enforcement is { } s)
        {
            var settings = (await uow.EnforcementSettings.ListAsync(take: 1, ct: ct)).FirstOrDefault();
            if (settings is null) await uow.EnforcementSettings.AddAsync(settings = new EnforcementSettings(), ct);
            settings.Enabled = s.Enabled;
            settings.EnforceBlocklist = s.EnforceBlocklist;
            settings.EnforceAllowlist = s.EnforceAllowlist;
        }

        await uow.SaveChangesAsync(ct);
        return new ConfigRestoreResultDto(backup.Policies.Count, ruleCount, ipCount, backup.Feeds.Count, replace);
    }

    private static void Validate(FirewallConfigBackup b)
    {
        var duplicate = b.Policies.GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new BusinessRuleException($"The backup contains two policies named '{duplicate.Key}'.");
        foreach (var p in b.Policies)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) throw new BusinessRuleException("Every policy needs a name.");
            foreach (var r in p.Rules)
            {
                foreach (var (label, value) in new[] { ("source", r.SourceAddress), ("destination", r.DestinationAddress) })
                    if (!AddressMatcher.IsValid(value)) throw new BusinessRuleException($"Rule '{r.Name}' in '{p.Name}' has an invalid {label} address '{value}'.");
            }
        }
        foreach (var e in b.IpList)
        {
            var error = IpListEntry.Validate(e.Address, e.ListType);
            if (error is not null) throw new BusinessRuleException($"IP list entry '{e.Address}': {error}");
        }
    }
}
