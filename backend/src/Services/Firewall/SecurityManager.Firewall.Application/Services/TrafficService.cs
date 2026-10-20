using System.Net;
using SecurityManager.Firewall.Application.Abstractions;
using SecurityManager.Firewall.Application.DTOs;
using SecurityManager.Firewall.Application.Mapping;
using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Firewall.Domain.Services;
using SecurityManager.Firewall.Domain.ValueObjects;
using SecurityManager.Shared.Kernel.Contracts;
using SecurityManager.Shared.Kernel.Enums;
using SecurityManager.Shared.Kernel.Exceptions;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Firewall.Application.Services;

public interface ITrafficService
{
    Task<EvaluationResultDto> EvaluateAsync(EvaluateTrafficRequest request, CancellationToken ct = default);
    Task<PagedResult<TrafficLogDto>> GetLogsAsync(TrafficLogQuery query, CancellationToken ct = default);
    Task<FirewallStatsDto> GetStatsAsync(CancellationToken ct = default);
}

public sealed class TrafficService(IFirewallUnitOfWork uow, ISecurityEventPublisher publisher) : ITrafficService
{
    public async Task<EvaluationResultDto> EvaluateAsync(EvaluateTrafficRequest r, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(r.SourceIp, out _)) throw new BusinessRuleException($"Invalid source IP '{r.SourceIp}'.");
        if (!IPAddress.TryParse(r.DestinationIp, out _)) throw new BusinessRuleException($"Invalid destination IP '{r.DestinationIp}'.");

        var packet = new TrafficPacket(r.SourceIp.Trim(), r.DestinationIp.Trim(), r.SourcePort, r.DestinationPort, r.Protocol, r.Direction);
        var now = DateTime.UtcNow;
        var policies = await uow.Policies.ListAsync(p => p.IsEnabled, includes: [p => p.Rules], ct: ct);
        var ipList = await uow.IpList.ListAsync(e => e.IsEnabled && (e.ExpiresAt == null || e.ExpiresAt > now), ct: ct);

        var decision = FirewallEngine.Evaluate(policies, packet, ipList, now);
        decision.Rule?.RegisterHit(now);
        decision.ListEntry?.RegisterHit(now);
        var listName = decision.ListEntry is null ? null : decision.ListEntry.ListType == IpListType.Allow ? "IP Allowlist" : "IP Blocklist";

        await uow.TrafficLogs.AddAsync(new TrafficLog
        {
            Timestamp = now,
            SourceIp = packet.SourceIp,
            DestinationIp = packet.DestinationIp,
            SourcePort = packet.SourcePort,
            DestinationPort = packet.DestinationPort,
            Protocol = packet.Protocol,
            Direction = packet.Direction,
            Action = decision.Action,
            MatchedRuleId = decision.Rule?.Id ?? decision.ListEntry?.Id,
            MatchedRuleName = decision.Rule?.Name ?? decision.ListEntry?.Address,
            PolicyName = listName ?? decision.Policy?.Name,
            Reason = decision.Reason.Length > 300 ? decision.Reason[..300] : decision.Reason
        }, ct);

        // Rule hit counters and the traffic log are committed together.
        await uow.SaveChangesAsync(ct);

        if (decision.Action == RuleAction.Deny)
        {
            await publisher.PublishAsync(new SecurityEventMessage(
                EventType: "FirewallBlock",
                Severity: Severity.Low,
                Source: "Firewall",
                Message: $"Blocked {packet.Protocol} {packet.SourceIp}:{packet.SourcePort} -> {packet.DestinationIp}:{packet.DestinationPort} ({decision.Reason})",
                SourceIp: packet.SourceIp,
                TargetHost: packet.DestinationIp), ct);
        }

        return new EvaluationResultDto(decision.Action, decision.Rule?.Id ?? decision.ListEntry?.Id,
            decision.Rule?.Name ?? decision.ListEntry?.Address, listName ?? decision.Policy?.Name, decision.Reason, now);
    }

    public async Task<PagedResult<TrafficLogDto>> GetLogsAsync(TrafficLogQuery q, CancellationToken ct = default)
    {
        var ip = q.Ip?.Trim();
        // Model binding yields server-local DateTimes for "...Z" input; timestamps are stored in UTC.
        var from = q.From?.ToUniversalTime();
        var to = q.To?.ToUniversalTime();
        var result = await uow.TrafficLogs.GetPagedAsync(
            q.Page, q.PageSize,
            l => (q.Action == null || l.Action == q.Action)
                 && (string.IsNullOrEmpty(ip) || l.SourceIp == ip || l.DestinationIp == ip)
                 && (from == null || l.Timestamp >= from)
                 && (to == null || l.Timestamp <= to),
            orderBy: o => o.OrderByDescending(l => l.Timestamp),
            ct: ct);
        return result.Map(l => l.ToDto());
    }

    public async Task<FirewallStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddHours(-23);
        since = new DateTime(since.Year, since.Month, since.Day, since.Hour, 0, 0, DateTimeKind.Utc);

        var totalPolicies = await uow.Policies.CountAsync(ct: ct);
        var activePolicies = await uow.Policies.CountAsync(p => p.IsEnabled, ct);
        var totalRules = await uow.Rules.CountAsync(ct: ct);
        var activeRules = await uow.Rules.CountAsync(r => r.IsEnabled, ct);
        var allowed = await uow.TrafficLogs.CountAsync(l => l.Timestamp >= since && l.Action == RuleAction.Allow, ct);
        var denied = await uow.TrafficLogs.CountAsync(l => l.Timestamp >= since && l.Action == RuleAction.Deny, ct);

        var topRules = await uow.Rules.ListAsync(r => r.HitCount > 0,
            o => o.OrderByDescending(r => r.HitCount), take: 6, ct: ct);

        var logs = await uow.TrafficLogs.ListAsync(l => l.Timestamp >= since, take: 20000, ct: ct);
        var byHour = logs.GroupBy(l => new DateTime(l.Timestamp.Year, l.Timestamp.Month, l.Timestamp.Day, l.Timestamp.Hour, 0, 0, DateTimeKind.Utc))
                         .ToDictionary(g => g.Key, g => g.ToList());

        var timeline = Enumerable.Range(0, 24).Select(i =>
        {
            var hour = since.AddHours(i);
            var bucket = byHour.GetValueOrDefault(hour) ?? [];
            return new TimelinePoint(hour,
                bucket.Count(l => l.Action == RuleAction.Allow),
                bucket.Count(l => l.Action == RuleAction.Deny));
        }).ToList();

        return new FirewallStatsDto(totalPolicies, activePolicies, totalRules, activeRules, allowed, denied,
            topRules.Select(r => new TopRuleDto(r.Id, r.Name, r.Action, r.HitCount)).ToList(), timeline);
    }
}
