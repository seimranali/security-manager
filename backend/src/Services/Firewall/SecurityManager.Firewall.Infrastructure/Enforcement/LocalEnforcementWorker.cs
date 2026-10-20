using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecurityManager.Enforcement;
using SecurityManager.Enforcement.Firewall;
using SecurityManager.Firewall.Application.Abstractions;
using SecurityManager.Firewall.Application.Services;
using SecurityManager.Shared.Kernel.Contracts;

namespace SecurityManager.Firewall.Infrastructure.Enforcement;

public sealed class LocalEnforcementOptions
{
    public const string Section = "Enforcement:Local";

    /// <summary>Apply the rules to this machine's firewall from inside the Firewall service (no agent needed).</summary>
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 10;
    /// <summary>Full re-check of installed rules even when nothing changed (detects manual edits in the OS firewall).</summary>
    public int ReconcileMinutes { get; set; } = 5;

    public static string AgentId => $"local:{Environment.MachineName}";
}

public static class LocalEnforcementRegistration
{
    public static IServiceCollection AddLocalEnforcement(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<LocalEnforcementOptions>(config.GetSection(LocalEnforcementOptions.Section));
        services.Configure<EnforcementOptions>(config.GetSection(LocalEnforcementOptions.Section));
        services.AddSingleton<IHostFirewall>(sp => HostFirewallFactory.Create(sp.GetRequiredService<IOptions<EnforcementOptions>>().Value.NftPath));
        services.AddSingleton<EnforcementSynchronizer>();
        services.AddHostedService<LocalEnforcementWorker>();
        return services;
    }
}

/// <summary>
/// Keeps this machine's own firewall (Windows Firewall or nftables) in line with Security Manager's rules and IP lists.
/// Reports its status like an agent, so the Rules page can show whether enforcement is working.
/// </summary>
public sealed class LocalEnforcementWorker(
    IServiceScopeFactory scopes,
    EnforcementSynchronizer synchronizer,
    IOptions<LocalEnforcementOptions> options,
    IOptions<EnforcementOptions> enforcementOptions,
    ILogger<LocalEnforcementWorker> logger) : BackgroundService
{
    private static readonly string Version = "built-in " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.Enabled)
        {
            logger.LogInformation("Built-in firewall enforcement is disabled (Enforcement:Local:Enabled=false)");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // database initialisation runs first
        string? lastVersion = null;
        string? lastStatus = null;
        var lastReconcile = DateTime.MinValue;
        SyncResult? last = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var enforcement = scope.ServiceProvider.GetRequiredService<IEnforcementService>();

                if (await SeparateAgentActiveAsync(scope.ServiceProvider, stoppingToken))
                {
                    // An installed agent already manages this machine; two writers would just fight over the same rules.
                    if (lastStatus != "Delegated") logger.LogInformation("A Security Manager agent manages this host; built-in enforcement is idle");
                    lastStatus = "Delegated";
                }
                else
                {
                    var desired = await enforcement.GetDesiredStateAsync(stoppingToken);
                    var due = desired.Version != lastVersion || last?.Status != "InSync" || DateTime.UtcNow - lastReconcile > TimeSpan.FromMinutes(o.ReconcileMinutes);
                    if (due)
                    {
                        last = synchronizer.Apply(desired);
                        lastVersion = desired.Version;
                        lastReconcile = DateTime.UtcNow;
                        if (last.Status != lastStatus)
                        {
                            if (last.Errors.Count > 0) logger.LogWarning("Firewall enforcement: {Status} - {Error}", last.Status, last.Errors[0]);
                            else logger.LogInformation("Firewall enforcement: {Status} ({Rules} managed rules)", last.Status, last.ManagedRules);
                        }
                        lastStatus = last.Status;
                    }
                    await enforcement.ReportAsync(Report(last!), stoppingToken);
                    last = last! with { Created = 0, Updated = 0, Removed = 0 };
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Firewall enforcement cycle failed: {Message}", ex.Message);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, o.IntervalSeconds)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static async Task<bool> SeparateAgentActiveAsync(IServiceProvider sp, CancellationToken ct)
    {
        var uow = sp.GetRequiredService<IFirewallUnitOfWork>();
        var host = Environment.MachineName;
        var localId = LocalEnforcementOptions.AgentId;
        var recent = DateTime.UtcNow.AddMinutes(-2);
        return await uow.EnforcementAgents.AnyAsync(a => a.Hostname == host && a.AgentId != localId && a.LastSeenAt > recent, ct);
    }

    private EnforcementAgentReport Report(SyncResult r) => new(
        LocalEnforcementOptions.AgentId, Environment.MachineName, RuntimeInformation.OSDescription, Version,
        enforcementOptions.Value.IsAudit ? "Audit" : "Enforce",
        r.Status, r.AppliedVersion, r.ManagedRules, r.Created, r.Updated, r.Removed, options.Value.IntervalSeconds, r.Profiles, r.Errors, r.Warnings);
}
