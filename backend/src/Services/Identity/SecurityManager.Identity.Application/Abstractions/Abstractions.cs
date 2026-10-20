using SecurityManager.Identity.Domain.Entities;
using SecurityManager.Shared.Kernel.Abstractions;

namespace SecurityManager.Identity.Application.Abstractions;

public interface IIdentityUnitOfWork : IUnitOfWork
{
    IRepository<User> Users { get; }
    IRepository<RefreshToken> RefreshTokens { get; }
    IRepository<LoginAttempt> LoginAttempts { get; }
}

/// <summary>Encrypts small secrets (e.g. MFA seeds) before they are stored.</summary>
public interface ISecretProtector
{
    string Protect(byte[] plaintext);
    byte[] Unprotect(string protectedText);
}

/// <summary>Forwards authentication events to the Security service (feeds brute-force detection and alerts).</summary>
public interface ISecurityEventSink
{
    Task ReportLoginFailureAsync(string userName, string? ipAddress, string reason, CancellationToken ct = default);
    Task ReportLockoutAsync(string userName, string? ipAddress, CancellationToken ct = default);
}

public interface IPasswordHasher
{
    string Hash(User user, string password);
    bool Verify(User user, string hash, string password);
}

public sealed record AccessToken(string Token, DateTime ExpiresAt);

public sealed record GeneratedRefreshToken(string RawToken, string Hash, DateTime ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);
    GeneratedRefreshToken GenerateRefreshToken(DateTime now);
    string HashRefreshToken(string rawToken);

    /// <summary>Presenting an already-rotated token within this window is treated as a concurrent-request race.</summary>
    TimeSpan RefreshReuseGrace { get; }
}
