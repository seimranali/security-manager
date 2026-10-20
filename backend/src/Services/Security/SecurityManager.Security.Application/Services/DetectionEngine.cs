using System.Linq.Expressions;
using Microsoft.Extensions.Options;
using SecurityManager.Shared.Kernel.Networking;
using SecurityManager.Security.Application.Abstractions;
using SecurityManager.Security.Domain.Entities;
using SecurityManager.Security.Domain.Enums;
using SecurityManager.Shared.Kernel.Contracts;

namespace SecurityManager.Security.Application.Services;

public interface IDetectionEngine
{
    /// <summary>Correlates a persisted event against enabled rules; returns detections created by this event.</summary>
    Task<IReadOnlyList<Detection>> ProcessAsync(SecurityEvent securityEvent, CancellationToken ct = default);
}

public sealed class DetectionEngine(
    ISecurityUnitOfWork uow,
    IAlertPublisher alerts,
    IFirewallBlocker firewall,
    IOptions<AutoBlockOptions> autoBlock) : IDetectionEngine
{
    public async Task<IReadOnlyList<Detection>> ProcessAsync(SecurityEvent e, CancellationToken ct = default)
    {
        var created = new List<Detection>();
        var rules = await uow.Rules.ListAsync(r => r.IsEnabled && r.EventType == e.EventType, ct: ct);

        foreach (var rule in rules.Where(r => r.AppliesTo(e)))
        {
            var key = rule.GetCorrelationKey(e);
            if (key is null) continue;

            var windowStart = e.Timestamp.AddMinutes(-rule.TimeWindowMinutes);
            var count = await uow.Events.CountAsync(WindowPredicate(rule, key, windowStart, e.Timestamp), ct);
            if (count < rule.Threshold) continue;

            var ruleId = rule.Id;
            var open = await uow.Detections.FirstOrDefaultAsync(d =>
                d.RuleId == ruleId && d.CorrelationKey == key &&
                (d.Status == DetectionStatus.New || d.Status == DetectionStatus.Investigating), ct: ct);

            if (open is not null)
            {
                open.RegisterOccurrence(count, e.Timestamp);
                continue;
            }

            var detection = Detection.Raise(rule, key, count, windowStart, e.Timestamp);
            rule.RegisterTrigger(e.Timestamp);
            await uow.Detections.AddAsync(detection, ct);
            created.Add(detection);
        }

        e.IsProcessed = true;
        await uow.SaveChangesAsync(ct);

        foreach (var d in created)
        {
            await alerts.PublishAsync(new AlertMessage(
                Title: d.Title,
                Message: d.Description,
                Severity: d.Severity,
                Source: "Security",
                SourceReference: d.Id.ToString(),
                Category: d.MitreTechnique is null ? d.EventType.ToString() : $"{d.EventType} ({d.MitreTechnique})"), ct);

            if (ShouldAutoBlock(d))
            {
                await firewall.BlockAsync(d.CorrelationKey, $"Auto-blocked: {d.RuleName}",
                    TimeSpan.FromMinutes(autoBlock.Value.DurationMinutes), d.Id.ToString(), ct);
            }
        }

        return created;
    }

    /// <summary>Only public source IPs from sufficiently severe detections are blocked automatically.</summary>
    internal bool ShouldAutoBlock(Detection d)
    {
        var o = autoBlock.Value;
        return o.Enabled
               && d.CorrelateBy == CorrelationField.SourceIp
               && d.Severity >= o.MinSeverity
               && System.Net.IPAddress.TryParse(d.CorrelationKey, out _)
               && !(o.ExcludePrivateRanges && IpClassifier.IsPrivateOrReserved(d.CorrelationKey));
    }

    internal static Expression<Func<SecurityEvent, bool>> WindowPredicate(
        DetectionRule rule, string key, DateTime from, DateTime to)
    {
        var type = rule.EventType;
        var minSeverity = rule.MinEventSeverity;
        return rule.CorrelateBy switch
        {
            CorrelationField.SourceIp => x => x.EventType == type && x.Severity >= minSeverity && x.Timestamp >= from && x.Timestamp <= to && x.SourceIp == key,
            CorrelationField.UserName => x => x.EventType == type && x.Severity >= minSeverity && x.Timestamp >= from && x.Timestamp <= to && x.UserName == key,
            CorrelationField.TargetHost => x => x.EventType == type && x.Severity >= minSeverity && x.Timestamp >= from && x.Timestamp <= to && x.TargetHost == key,
            _ => x => x.EventType == type && x.Severity >= minSeverity && x.Timestamp >= from && x.Timestamp <= to
        };
    }
}
