using SecurityManager.Security.Domain.Enums;
using SecurityManager.Shared.Kernel.Entities;
using SecurityManager.Shared.Kernel.Enums;

namespace SecurityManager.Security.Domain.Entities;

public class SecurityEvent : BaseEntity
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public SecurityEventType EventType { get; set; }
    public Severity Severity { get; set; } = Severity.Info;
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? SourceIp { get; set; }
    public string? TargetHost { get; set; }
    public string? UserName { get; set; }
    public string? RawData { get; set; }
    public bool IsProcessed { get; set; }
}
