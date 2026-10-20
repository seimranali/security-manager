using SecurityManager.Shared.Kernel.Enums;

namespace SecurityManager.Shared.Kernel.Contracts;

/// <summary>Payload sent by any service to the Alert Service to raise an alert.</summary>
public sealed record AlertMessage(
    string Title,
    string Message,
    Severity Severity,
    string Source,
    string? SourceReference = null,
    string? Category = null);

/// <summary>Payload sent by any service to the Security Service to record a security event.</summary>
public sealed record SecurityEventMessage(
    string EventType,
    Severity Severity,
    string Source,
    string Message,
    string? SourceIp = null,
    string? TargetHost = null,
    string? UserName = null,
    string? RawData = null);
