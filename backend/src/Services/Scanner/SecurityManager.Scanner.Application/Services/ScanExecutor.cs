using Microsoft.Extensions.Logging;
using SecurityManager.Scanner.Application.Abstractions;
using SecurityManager.Scanner.Domain.Entities;
using SecurityManager.Scanner.Domain.Enums;
using SecurityManager.Scanner.Domain.Models;
using SecurityManager.Scanner.Domain.Services;
using SecurityManager.Shared.Kernel.Contracts;
using SecurityManager.Shared.Kernel.Enums;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.Scanner.Application.Services;

public interface IScanExecutor
{
    Task ExecuteAsync(Guid jobId, CancellationToken ct = default);
}

/// <summary>Runs a queued scan job end-to-end: probes, analysis, persistence and alerting.</summary>
public sealed class ScanExecutor(
    IScannerUnitOfWork uow,
    IPortScanner portScanner,
    IHttpProbe httpProbe,
    ITlsProbe tlsProbe,
    IAlertPublisher alerts,
    ILogger<ScanExecutor> logger) : IScanExecutor
{
    public async Task ExecuteAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await uow.Jobs.GetByIdAsync(jobId, ct: ct);
        if (job is null || job.Status != ScanStatus.Queued) return;

        var target = job.TargetId is { } targetId ? await uow.Targets.GetByIdAsync(targetId, ct: ct) : null;
        if (target is null)
        {
            job.Fail("Target no longer exists.", DateTime.UtcNow);
            await uow.SaveChangesAsync(ct);
            return;
        }

        job.Start(DateTime.UtcNow);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Scan {JobId} started for {Host} ({Types})", job.Id, job.Host, job.ScanTypes);

        try
        {
            var findings = new List<ScanFinding>();
            var openPorts = new HashSet<int>();
            var portScanned = job.ScanTypes.HasFlag(ScanType.PortScan);

            if (portScanned)
            {
                var results = await portScanner.ScanAsync(job.Host, PortSpec.Expand(target.Ports, TargetService.MaxPortsPerTarget), ct);
                var open = results.Where(r => r.IsOpen).ToList();
                openPorts.UnionWith(open.Select(r => r.Port));
                findings.AddRange(FindingAnalyzer.AnalyzePorts(open));
                job.OpenPorts = string.Join(',', openPorts.Order());
                await CheckpointAsync(job, 50, ct);
            }

            if (job.ScanTypes.HasFlag(ScanType.HttpHeaders))
            {
                HttpProbeResult? probe = null;
                if (!portScanned || openPorts.Contains(443))
                    probe = await httpProbe.ProbeAsync($"https://{job.Host}/", ct);
                if ((probe is null || probe.Error is not null) && (!portScanned || openPorts.Contains(80)))
                    probe = await httpProbe.ProbeAsync($"http://{job.Host}/", ct);
                if (probe is not null) findings.AddRange(FindingAnalyzer.AnalyzeHttp(probe));
                await CheckpointAsync(job, 75, ct);
            }

            if (job.ScanTypes.HasFlag(ScanType.SslCertificate) && (!portScanned || openPorts.Contains(443)))
            {
                var tls = await tlsProbe.ProbeAsync(job.Host, 443, ct);
                findings.AddRange(FindingAnalyzer.AnalyzeTls(tls, DateTime.UtcNow));
            }

            foreach (var finding in findings)
            {
                finding.JobId = job.Id;
                finding.TargetId = target.Id;
                finding.TargetName = target.Name;
            }

            var now = DateTime.UtcNow;
            await uow.Findings.AddRangeAsync(findings, ct);
            job.Complete(now, findings);
            target.LastScannedAt = now;
            target.RiskScore = RiskCalculator.Score(findings);

            // Findings, job result and target risk are committed atomically.
            await uow.SaveChangesAsync(ct);
            logger.LogInformation("Scan {JobId} completed with {Count} findings", job.Id, findings.Count);

            await RaiseAlertAsync(job, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Scan {JobId} failed", job.Id);
            job.Fail(ex.Message, DateTime.UtcNow);
            await uow.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task CheckpointAsync(ScanJob job, int progress, CancellationToken ct)
    {
        job.ReportProgress(progress);
        await uow.SaveChangesAsync(ct);
    }

    private Task RaiseAlertAsync(ScanJob job, CancellationToken ct)
    {
        if (job.CriticalCount == 0 && job.HighCount == 0) return Task.CompletedTask;

        var severity = job.CriticalCount > 0 ? Severity.Critical : Severity.High;
        return alerts.PublishAsync(new AlertMessage(
            Title: $"Vulnerabilities found on {job.TargetName}",
            Message: $"{job.Trigger} scan of {job.Host} found {job.CriticalCount} critical and {job.HighCount} high severity issue(s) ({job.FindingsCount} total).",
            Severity: severity,
            Source: "Scanner",
            SourceReference: job.Id.ToString(),
            Category: "Vulnerability"), ct);
    }
}
