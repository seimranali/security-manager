namespace SecurityManager.Security.Domain.Enums;

public enum SecurityEventType
{
    AuthenticationFailure,
    AuthenticationSuccess,
    FirewallBlock,
    MalwareDetected,
    PortScan,
    PrivilegeEscalation,
    DataExfiltration,
    ConfigurationChange,
    SuspiciousProcess,
    Other
}

public enum DetectionStatus
{
    New,
    Investigating,
    Resolved,
    FalsePositive
}

/// <summary>Event attribute used to correlate events into one detection.</summary>
public enum CorrelationField
{
    None,
    SourceIp,
    UserName,
    TargetHost
}
