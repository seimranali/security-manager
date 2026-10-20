using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using SecurityManager.Agent.Sync;
using SecurityManager.Enforcement;
using SecurityManager.Shared.Kernel.Contracts;

namespace SecurityManager.Agent;

public sealed class EnforcementWorker(
    EnforcementClient client,
    EnforcementSynchronizer synchronizer,
    IOptions<AgentOptions> options,
    ILogger<EnforcementWorker> logger) : BackgroundService
{
    private static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        logger.LogInformation("Security Manager agent {Version} started as {AgentId} in {Mode} mode", Version, o.ResolvedAgentId, o.Mode);
        string? lastVersion = null;
        var lastReconcile = DateTime.MinValue;
        SyncResult? last = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var desired = await client.GetDesiredStateAsync(stoppingToken);
                var due = desired.Version != lastVersion || last?.Status != "InSync" || DateTime.UtcNow - lastReconcile > TimeSpan.FromMinutes(o.ReconcileMinutes);
                if (due)
                {
                    last = synchronizer.Apply(desired);
                    lastVersion = desired.Version;
                    lastReconcile = DateTime.UtcNow;
                }
                await client.ReportAsync(BuildReport(o, last!), stoppingToken);
                // After reporting change counts once, later heartbeats report zero changes.
                last = last! with { Created = 0, Updated = 0, Removed = 0 };
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Sync failed: {Message}", ex.Message);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, o.IntervalSeconds)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public static EnforcementAgentReport BuildReport(AgentOptions o, SyncResult r) => new(
        o.ResolvedAgentId, Environment.MachineName, RuntimeInformation.OSDescription, Version, o.IsAudit ? "Audit" : "Enforce",
        r.Status, r.AppliedVersion, r.ManagedRules, r.Created, r.Updated, r.Removed, o.IntervalSeconds, r.Profiles, r.Errors, r.Warnings);
}
