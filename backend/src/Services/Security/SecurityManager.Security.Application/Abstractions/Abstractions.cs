using SecurityManager.Security.Domain.Entities;
using SecurityManager.Shared.Kernel.Abstractions;
using SecurityManager.Shared.Kernel.Contracts;

namespace SecurityManager.Security.Application.Abstractions;

public interface ISecurityUnitOfWork : IUnitOfWork
{
    IRepository<SecurityEvent> Events { get; }
    IRepository<DetectionRule> Rules { get; }
    IRepository<Detection> Detections { get; }
}

/// <summary>Adds addresses to the Firewall Service blocklist (automated response).</summary>
public interface IFirewallBlocker
{
    Task BlockAsync(string ipAddress, string reason, TimeSpan duration, string sourceReference, CancellationToken ct = default);
}

/// <summary>Automatic blocking of attacking IPs when a detection is raised ("AutoBlock" configuration section).</summary>
public sealed class AutoBlockOptions
{
    public const string Section = "AutoBlock";

    public bool Enabled { get; set; } = true;
    /// <summary>Minimum detection severity that triggers a block.</summary>
    public SecurityManager.Shared.Kernel.Enums.Severity MinSeverity { get; set; } = SecurityManager.Shared.Kernel.Enums.Severity.High;
    public int DurationMinutes { get; set; } = 1440;
    /// <summary>Never auto-block private, loopback, link-local or other reserved addresses.</summary>
    public bool ExcludePrivateRanges { get; set; } = true;
}

/// <summary>Raises alerts in the Alert Service.</summary>
public interface IAlertPublisher
{
    Task PublishAsync(AlertMessage message, CancellationToken ct = default);
}
