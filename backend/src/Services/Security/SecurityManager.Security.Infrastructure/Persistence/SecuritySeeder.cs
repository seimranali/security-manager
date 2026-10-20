using Microsoft.EntityFrameworkCore;
using SecurityManager.Security.Domain.Entities;
using SecurityManager.Security.Domain.Enums;
using SecurityManager.Shared.Kernel.Enums;

namespace SecurityManager.Security.Infrastructure.Persistence;

public static class SecuritySeeder
{
    public static async Task SeedAsync(SecurityDbContext db, CancellationToken ct)
    {
        if (await db.Rules.AnyAsync(ct)) return;

        var rules = new List<DetectionRule>
        {
            Rule("Brute Force Attack", SecurityEventType.AuthenticationFailure, 5, 5, CorrelationField.SourceIp, Severity.High, "T1110",
                "Multiple failed logins from a single source IP."),
            Rule("Targeted Account Attack", SecurityEventType.AuthenticationFailure, 10, 15, CorrelationField.UserName, Severity.Medium, "T1110.001",
                "Many failed logins against the same account."),
            Rule("Network Reconnaissance", SecurityEventType.PortScan, 1, 10, CorrelationField.SourceIp, Severity.Medium, "T1046",
                "Port scanning activity detected."),
            Rule("Repeated Firewall Blocks", SecurityEventType.FirewallBlock, 10, 5, CorrelationField.SourceIp, Severity.Medium, "T1595",
                "A source is repeatedly hitting blocked ports — likely active scanning."),
            Rule("Malware Detected", SecurityEventType.MalwareDetected, 1, 60, CorrelationField.TargetHost, Severity.Critical, "T1204",
                "Endpoint protection reported malware."),
            Rule("Privilege Escalation", SecurityEventType.PrivilegeEscalation, 1, 60, CorrelationField.UserName, Severity.Critical, "T1068",
                "A user gained elevated privileges unexpectedly."),
            Rule("Possible Data Exfiltration", SecurityEventType.DataExfiltration, 3, 15, CorrelationField.TargetHost, Severity.High, "T1041",
                "Unusual outbound data transfers from a host."),
            Rule("Suspicious Process Execution", SecurityEventType.SuspiciousProcess, 1, 30, CorrelationField.TargetHost, Severity.High, "T1059",
                "Script interpreter or LOLBin launched with suspicious arguments.")
        };
        db.Rules.AddRange(rules);

        await db.SaveChangesAsync(ct);
    }

    private static DetectionRule Rule(string name, SecurityEventType type, int threshold, int window,
        CorrelationField by, Severity severity, string mitre, string description) => new()
    {
        Name = name,
        Description = description,
        EventType = type,
        Threshold = threshold,
        TimeWindowMinutes = window,
        CorrelateBy = by,
        Severity = severity,
        MitreTechnique = mitre
    };
}
