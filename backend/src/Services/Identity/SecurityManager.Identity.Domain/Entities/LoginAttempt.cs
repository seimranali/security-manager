using SecurityManager.Shared.Kernel.Entities;

namespace SecurityManager.Identity.Domain.Entities;

/// <summary>Audit record of one sign-in attempt (successful or not).</summary>
public class LoginAttempt : BaseEntity
{
    public DateTime Timestamp { get; set; }
    /// <summary>The user name as typed (normalised); also recorded for unknown accounts.</summary>
    public string UserName { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public bool Succeeded { get; set; }
    /// <summary>Why it failed: BadPassword, UnknownUser, LockedOut, Disabled, MfaRequired, BadMfaCode.</summary>
    public string? FailureReason { get; set; }
    public bool UsedMfa { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

public static class LoginFailureReasons
{
    public const string UnknownUser = "UnknownUser";
    public const string BadPassword = "BadPassword";
    public const string LockedOut = "LockedOut";
    public const string Disabled = "Disabled";
    public const string BadMfaCode = "BadMfaCode";
}
