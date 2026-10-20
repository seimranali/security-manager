using SecurityManager.Shared.Kernel.Entities;

namespace SecurityManager.Identity.Domain.Entities;

/// <summary>
/// An opaque, single-use refresh token. Only its SHA-256 hash is stored. Tokens issued from the same
/// login share a <see cref="FamilyId"/>; each refresh rotates the token, and presenting an already-rotated
/// token (outside a short grace window) is treated as theft and revokes the whole family.
/// </summary>
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public Guid FamilyId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public Guid? ReplacedByTokenId { get; set; }

    public bool IsExpired(DateTime now) => ExpiresAt <= now;
    public bool IsRevoked => RevokedAt is not null;
    public bool IsActive(DateTime now) => !IsRevoked && !IsExpired(now);

    public void Revoke(DateTime now, string reason, Guid? replacedBy = null)
    {
        if (IsRevoked) return;
        RevokedAt = now;
        RevokedReason = reason;
        ReplacedByTokenId = replacedBy;
    }
}

public static class RevocationReasons
{
    public const string Rotated = "Rotated";
    public const string Logout = "Logout";
    public const string ReuseDetected = "ReuseDetected";
    public const string PasswordChanged = "PasswordChanged";
    public const string AdminRevoked = "AdminRevoked";
    public const string UserDisabled = "UserDisabled";
}
