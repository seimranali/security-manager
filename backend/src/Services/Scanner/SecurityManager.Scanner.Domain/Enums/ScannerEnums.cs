namespace SecurityManager.Scanner.Domain.Enums;

[Flags]
public enum ScanType
{
    None = 0,
    PortScan = 1,
    HttpHeaders = 2,
    SslCertificate = 4,
    All = PortScan | HttpHeaders | SslCertificate
}

public enum ScanStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public enum ScanTrigger
{
    Manual,
    Scheduled
}

public enum FindingStatus
{
    Open,
    Resolved,
    Accepted
}
