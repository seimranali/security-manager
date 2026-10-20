using System.Net;
using System.Text.RegularExpressions;
using SecurityManager.Shared.Kernel.Entities;

namespace SecurityManager.Scanner.Domain.Entities;

public partial class ScanTarget : BaseEntity
{
    public const string DefaultPorts =
        "21,22,23,25,53,80,110,135,139,143,443,445,1433,1521,3306,3389,5432,5900,6379,8080,8443,9200,27017";

    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Ports { get; set; } = DefaultPorts;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastScannedAt { get; set; }
    public int? RiskScore { get; set; }

    public ICollection<ScanSchedule> Schedules { get; set; } = new List<ScanSchedule>();

    public static bool IsValidHost(string? host) =>
        !string.IsNullOrWhiteSpace(host) && (IPAddress.TryParse(host, out _) || HostnameRegex().IsMatch(host));

    [GeneratedRegex(@"^(?=.{1,253}$)[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$")]
    private static partial Regex HostnameRegex();
}
