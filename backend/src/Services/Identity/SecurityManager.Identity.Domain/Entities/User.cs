using SecurityManager.Shared.Kernel.Entities;

namespace SecurityManager.Identity.Domain.Entities;

public class User : BaseEntity
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndsAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime? PasswordChangedAt { get; set; }

    // ---- Multi-factor authentication (TOTP, RFC 6238)
    public bool MfaEnabled { get; set; }
    /// <summary>Authenticator secret, encrypted at rest (set during enrolment, kept once enabled).</summary>
    public string? MfaSecret { get; set; }
    /// <summary>SHA-256 hashes of unused one-time recovery codes, separated by ';'.</summary>
    public string? MfaRecoveryCodes { get; set; }
    /// <summary>Last accepted TOTP time step: a code can be used only once.</summary>
    public long MfaLastStep { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public static string Normalize(string userName) => userName.Trim().ToLowerInvariant();

    public bool IsLockedOut(DateTime now) => LockoutEndsAt is { } end && end > now;

    /// <summary>Counts a failed password attempt and locks the account after too many.</summary>
    public void RegisterFailedLogin(DateTime now)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= MaxFailedAttempts)
        {
            LockoutEndsAt = now.Add(LockoutDuration);
            FailedLoginCount = 0;
        }
    }

    public void RegisterSuccessfulLogin(DateTime now)
    {
        FailedLoginCount = 0;
        LockoutEndsAt = null;
        LastLoginAt = now;
    }

    public void SetPassword(string hash, DateTime now)
    {
        PasswordHash = hash;
        PasswordChangedAt = now;
        FailedLoginCount = 0;
        LockoutEndsAt = null;
    }
}
