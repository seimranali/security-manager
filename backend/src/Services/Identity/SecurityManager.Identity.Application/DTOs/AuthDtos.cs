using System.ComponentModel.DataAnnotations;

namespace SecurityManager.Identity.Application.DTOs;

public sealed record UserDto(
    Guid Id, string UserName, string Email, string FullName, string Role, bool IsActive,
    bool IsLockedOut, DateTime? LastLoginAt, DateTime CreatedAt, bool MfaEnabled = false);

public sealed class LoginRequest
{
    [Required, StringLength(100)] public string UserName { get; set; } = string.Empty;
    [Required, StringLength(200)] public string Password { get; set; } = string.Empty;
    /// <summary>Authenticator code (or a recovery code) when the account has MFA enabled.</summary>
    [StringLength(20)] public string? Code { get; set; }
}

/// <summary>Result of login/refresh. The raw refresh token is delivered via HttpOnly cookie by the API layer.</summary>
public sealed record AuthResult(
    string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt, UserDto User);

public sealed record AuthResponse(string? AccessToken, DateTime? ExpiresAt, string? TokenType, UserDto? User, bool MfaRequired = false);

public sealed class ChangePasswordRequest
{
    [Required, StringLength(200)] public string CurrentPassword { get; set; } = string.Empty;
    [Required, StringLength(200, MinimumLength = 8)] public string NewPassword { get; set; } = string.Empty;
}

public sealed class CreateUserRequest
{
    [Required, StringLength(50, MinimumLength = 3), RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "User name may contain letters, digits, '.', '_' and '-'.")]
    public string UserName { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(200)] public string Email { get; set; } = string.Empty;
    [Required, StringLength(100)] public string FullName { get; set; } = string.Empty;
    [Required] public string Role { get; set; } = "Viewer";
    [Required, StringLength(200, MinimumLength = 8)] public string Password { get; set; } = string.Empty;
}

public sealed class UpdateUserRequest
{
    [Required, EmailAddress, StringLength(200)] public string Email { get; set; } = string.Empty;
    [Required, StringLength(100)] public string FullName { get; set; } = string.Empty;
    [Required] public string Role { get; set; } = "Viewer";
    public bool IsActive { get; set; } = true;
    /// <summary>Optional admin password reset.</summary>
    [StringLength(200, MinimumLength = 8)] public string? NewPassword { get; set; }
}

public sealed record SessionDto(Guid Id, DateTime CreatedAt, DateTime ExpiresAt, string? CreatedByIp, string? UserAgent);

public sealed record LoginAttemptDto(Guid Id, DateTime Timestamp, string UserName, bool Succeeded, string? FailureReason, bool UsedMfa, string? IpAddress, string? UserAgent);

public sealed record MfaSetupDto(string Secret, string ProvisioningUri);

public sealed record MfaEnabledDto(IReadOnlyList<string> RecoveryCodes);

public sealed class MfaCodeRequest
{
    [Required, StringLength(20)] public string Code { get; set; } = string.Empty;
}

public sealed class MfaDisableRequest
{
    [Required, StringLength(200)] public string Password { get; set; } = string.Empty;
    [Required, StringLength(20)] public string Code { get; set; } = string.Empty;
}
