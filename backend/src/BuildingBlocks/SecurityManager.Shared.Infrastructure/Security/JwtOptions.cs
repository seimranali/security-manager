namespace SecurityManager.Shared.Infrastructure.Security;

/// <summary>JWT settings shared by every service (bound from the "Jwt" configuration section).</summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "SecurityManager.Identity";
    public string Audience { get; set; } = "SecurityManager";

    /// <summary>HMAC-SHA256 key; must be at least 32 bytes. Supply via environment/secret store in production.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
    public int ServiceTokenMinutes { get; set; } = 5;

    /// <summary>Window in which presenting a just-rotated refresh token is treated as a benign race, not theft.</summary>
    public int RefreshReuseGraceSeconds { get; set; } = 30;
}
