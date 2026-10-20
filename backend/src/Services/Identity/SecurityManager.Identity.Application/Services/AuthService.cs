using Microsoft.Extensions.Logging;
using SecurityManager.Identity.Application.Abstractions;
using SecurityManager.Identity.Application.DTOs;
using SecurityManager.Identity.Domain.Entities;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Identity.Application.Services;

public sealed record ClientInfo(string? IpAddress, string? UserAgent);

/// <summary>Password was correct but the account needs an authenticator code: the client should ask for it and retry.</summary>
public sealed class MfaRequiredException() : Exception("A verification code is required.");

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, ClientInfo client, CancellationToken ct = default);
    Task<AuthResult> RefreshAsync(string? rawRefreshToken, ClientInfo client, CancellationToken ct = default);
    Task LogoutAsync(string? rawRefreshToken, CancellationToken ct = default);
    Task<AuthResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, ClientInfo client, CancellationToken ct = default);
    Task<UserDto> GetMeAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<SessionDto>> GetSessionsAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<LoginAttemptDto>> GetLoginHistoryAsync(Guid? userId, int take, CancellationToken ct = default);
}

public sealed class AuthService(
    IIdentityUnitOfWork uow,
    IPasswordHasher hasher,
    ITokenService tokens,
    IMfaService mfa,
    ISecurityEventSink events,
    ILogger<AuthService> logger) : IAuthService
{
    private const string InvalidCredentials = "Invalid user name or password.";
    private static readonly User DummyUser = new() { UserName = "dummy" };
    private static string? _dummyHash;

    public async Task<AuthResult> LoginAsync(LoginRequest request, ClientInfo client, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var userName = User.Normalize(request.UserName);
        var user = await uow.Users.FirstOrDefaultAsync(u => u.UserName == userName, ct: ct);

        if (user is null)
        {
            // Equalise timing so attackers cannot discover valid user names.
            _dummyHash ??= hasher.Hash(DummyUser, Guid.NewGuid().ToString());
            hasher.Verify(DummyUser, _dummyHash, request.Password);
            logger.LogWarning("Failed login for unknown user {UserName} from {Ip}", userName, client.IpAddress);
            await FailAsync(null, userName, client, LoginFailureReasons.UnknownUser, now, ct);
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (user.IsLockedOut(now))
        {
            await FailAsync(user, userName, client, LoginFailureReasons.LockedOut, now, ct);
            throw new UnauthorizedException("Account is temporarily locked after repeated failed sign-ins. Try again later.");
        }

        if (!hasher.Verify(user, user.PasswordHash, request.Password))
        {
            user.RegisterFailedLogin(now);
            var locked = user.IsLockedOut(now);
            logger.LogWarning("Failed login for {UserName} from {Ip}", userName, client.IpAddress);
            await FailAsync(user, userName, client, LoginFailureReasons.BadPassword, now, ct);
            if (locked) await events.ReportLockoutAsync(userName, client.IpAddress, ct);
            throw new UnauthorizedException(locked ? "Too many failed attempts. The account is locked for 15 minutes." : InvalidCredentials);
        }

        if (!user.IsActive)
        {
            await FailAsync(user, userName, client, LoginFailureReasons.Disabled, now, ct);
            throw new UnauthorizedException("This account is disabled.");
        }

        if (user.MfaEnabled)
        {
            // Not an attempt yet: the client asks for the code and resubmits.
            if (string.IsNullOrWhiteSpace(request.Code)) throw new MfaRequiredException();
            if (!mfa.VerifyLoginCode(user, request.Code, now))
            {
                user.RegisterFailedLogin(now);
                var locked = user.IsLockedOut(now);
                await FailAsync(user, userName, client, LoginFailureReasons.BadMfaCode, now, ct);
                if (locked) await events.ReportLockoutAsync(userName, client.IpAddress, ct);
                throw new UnauthorizedException(locked ? "Too many failed attempts. The account is locked for 15 minutes." : "Invalid verification code.");
            }
        }

        user.RegisterSuccessfulLogin(now);
        await uow.LoginAttempts.AddAsync(Attempt(user, userName, client, now, succeeded: true, reason: null), ct);
        logger.LogInformation("User {UserName} signed in from {Ip}", userName, client.IpAddress);
        return await IssueAsync(user, Guid.NewGuid(), client, now, ct);
    }

    public async Task<IReadOnlyList<LoginAttemptDto>> GetLoginHistoryAsync(Guid? userId, int take, CancellationToken ct = default)
    {
        var items = await uow.LoginAttempts.ListAsync(
            userId is { } id ? a => a.UserId == id : null,
            o => o.OrderByDescending(a => a.Timestamp), Math.Clamp(take, 1, 1000), ct: ct);
        return items.Select(a => new LoginAttemptDto(a.Id, a.Timestamp, a.UserName, a.Succeeded, a.FailureReason, a.UsedMfa, a.IpAddress, a.UserAgent)).ToList();
    }

    private async Task FailAsync(User? user, string userName, ClientInfo client, string reason, DateTime now, CancellationToken ct)
    {
        await uow.LoginAttempts.AddAsync(Attempt(user, userName, client, now, succeeded: false, reason), ct);
        await uow.SaveChangesAsync(ct);
        // Feeds the Security service's brute-force / targeted-account detection rules.
        await events.ReportLoginFailureAsync(userName, client.IpAddress, reason, ct);
    }

    private static LoginAttempt Attempt(User? user, string userName, ClientInfo client, DateTime now, bool succeeded, string? reason) => new()
    {
        Timestamp = now,
        UserName = Truncate(userName, 100)!,
        UserId = user?.Id,
        Succeeded = succeeded,
        FailureReason = reason,
        UsedMfa = user?.MfaEnabled == true && succeeded,
        IpAddress = Truncate(client.IpAddress, 45),
        UserAgent = Truncate(client.UserAgent, 300)
    };

    public async Task<AuthResult> RefreshAsync(string? rawRefreshToken, ClientInfo client, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken)) throw new UnauthorizedException("Refresh token is missing.");

        var now = DateTime.UtcNow;
        var hash = tokens.HashRefreshToken(rawRefreshToken);
        var current = await uow.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, [t => t.User!], ct)
                      ?? throw new UnauthorizedException("Invalid refresh token.");

        if (current.IsRevoked)
        {
            // A just-rotated token is usually two tabs refreshing at once — reject without punishing the session.
            if (current.RevokedReason == RevocationReasons.Rotated && current.RevokedAt >= now - tokens.RefreshReuseGrace)
                throw new UnauthorizedException("Refresh token was already used.");

            // Otherwise a revoked token is being replayed: assume theft and kill the whole token family.
            var revoked = await RevokeFamilyAsync(current.FamilyId, now, RevocationReasons.ReuseDetected, ct);
            await uow.SaveChangesAsync(ct);
            logger.LogWarning("Refresh token reuse detected for user {UserId} from {Ip}; revoked {Count} token(s)",
                current.UserId, client.IpAddress, revoked);
            throw new UnauthorizedException("Refresh token reuse detected. Please sign in again.");
        }

        if (current.IsExpired(now)) throw new UnauthorizedException("Session expired. Please sign in again.");

        var user = current.User!;
        if (!user.IsActive || user.IsLockedOut(now))
        {
            await RevokeFamilyAsync(current.FamilyId, now, RevocationReasons.UserDisabled, ct);
            await uow.SaveChangesAsync(ct);
            throw new UnauthorizedException("This account is disabled or locked.");
        }

        // Rotate: the presented token is revoked and replaced in the same unit of work.
        return await IssueAsync(user, current.FamilyId, client, now, ct, replacing: current);
    }

    public async Task LogoutAsync(string? rawRefreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken)) return;
        var hash = tokens.HashRefreshToken(rawRefreshToken);
        var token = await uow.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct: ct);
        if (token is null) return;

        await RevokeFamilyAsync(token.FamilyId, DateTime.UtcNow, RevocationReasons.Logout, ct);
        await uow.SaveChangesAsync(ct);
    }

    public async Task<AuthResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, ClientInfo client, CancellationToken ct = default)
    {
        var user = await uow.Users.GetByIdAsync(userId, ct: ct) ?? throw new UnauthorizedException("User not found.");
        if (!hasher.Verify(user, user.PasswordHash, request.CurrentPassword))
            throw new BusinessRuleException("Current password is incorrect.");
        if (request.CurrentPassword == request.NewPassword)
            throw new BusinessRuleException("The new password must be different from the current one.");
        PasswordPolicy.Validate(request.NewPassword);

        var now = DateTime.UtcNow;
        user.SetPassword(hasher.Hash(user, request.NewPassword), now);

        // Sign out every other device, then start a fresh session for this one.
        await RevokeAllForUserAsync(uow, userId, now, RevocationReasons.PasswordChanged, ct);
        return await IssueAsync(user, Guid.NewGuid(), client, now, ct);
    }

    public async Task<UserDto> GetMeAsync(Guid userId, CancellationToken ct = default) =>
        UserMapping.ToDto(await uow.Users.GetByIdAsync(userId, ct: ct) ?? throw new UnauthorizedException("User not found."));

    public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var active = await uow.RefreshTokens.ListAsync(
            t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now,
            o => o.OrderByDescending(t => t.CreatedAt), ct: ct);
        return active.Select(t => new SessionDto(t.Id, t.CreatedAt, t.ExpiresAt, t.CreatedByIp, t.UserAgent)).ToList();
    }

    internal static async Task<int> RevokeAllForUserAsync(IIdentityUnitOfWork uow, Guid userId, DateTime now, string reason, CancellationToken ct)
    {
        var active = await uow.RefreshTokens.ListAsync(t => t.UserId == userId && t.RevokedAt == null, ct: ct);
        foreach (var token in active) token.Revoke(now, reason);
        return active.Count;
    }

    private async Task<AuthResult> IssueAsync(User user, Guid familyId, ClientInfo client, DateTime now, CancellationToken ct, RefreshToken? replacing = null)
    {
        var generated = tokens.GenerateRefreshToken(now);
        var entity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = generated.Hash,
            FamilyId = familyId,
            ExpiresAt = generated.ExpiresAt,
            CreatedByIp = Truncate(client.IpAddress, 45),
            UserAgent = Truncate(client.UserAgent, 300)
        };

        replacing?.Revoke(now, RevocationReasons.Rotated, entity.Id);
        await uow.RefreshTokens.AddAsync(entity, ct);
        await uow.SaveChangesAsync(ct);

        var access = tokens.CreateAccessToken(user);
        return new AuthResult(access.Token, access.ExpiresAt, generated.RawToken, generated.ExpiresAt, UserMapping.ToDto(user));
    }

    private async Task<int> RevokeFamilyAsync(Guid familyId, DateTime now, string reason, CancellationToken ct)
    {
        var family = await uow.RefreshTokens.ListAsync(t => t.FamilyId == familyId && t.RevokedAt == null, ct: ct);
        foreach (var token in family) token.Revoke(now, reason);
        return family.Count;
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}

internal static class UserMapping
{
    public static UserDto ToDto(User u) => new(
        u.Id, u.UserName, u.Email, u.FullName, u.Role, u.IsActive, u.IsLockedOut(DateTime.UtcNow), u.LastLoginAt, u.CreatedAt, u.MfaEnabled);
}
