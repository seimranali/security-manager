using SecurityManager.Scanner.Domain.Enums;
using SecurityManager.Shared.Kernel.Entities;
using SecurityManager.Shared.Kernel.Enums;

namespace SecurityManager.Scanner.Domain.Entities;

public class ScanFinding : BaseEntity
{
    public Guid JobId { get; set; }
    public ScanJob? Job { get; set; }
    public Guid? TargetId { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public string Category { get; set; } = string.Empty;
    public int? Port { get; set; }
    public string? Evidence { get; set; }
    public string? Recommendation { get; set; }
    public FindingStatus Status { get; set; } = FindingStatus.Open;
}
