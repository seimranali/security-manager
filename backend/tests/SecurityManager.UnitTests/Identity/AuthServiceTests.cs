using Microsoft.Extensions.Configuration;
using SecurityManager.Identity.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecurityManager.Identity.Application.DTOs;
using SecurityManager.Identity.Application.Services;
using SecurityManager.Identity.Domain.Entities;
using SecurityManager.Identity.Infrastructure.Persistence;
using SecurityManager.Identity.Infrastructure.Security;
using SecurityManager.Shared.Infrastructure.Security;
using SecurityManager.Shared.Kernel.Exceptions;
using SecurityManager.Shared.Kernel.Security;

namespace SecurityManager.UnitTests.Identity;

public class AuthServiceTests
{
    private const string Password = "Str0ng!Pass";
    private static readonly ClientInfo Client = new("127.0.0.1", "tests");

    [Fact]
    public async Task Login_returns_access_and_refresh_tokens()
    {
        var (service, _) = await CreateAsync();
        var result = await service.LoginAsync(Login(), Client);

        Assert.False(string.IsNullOrEmpty(result.AccessToken));
        Assert.True(result.RefreshToken.Length >= 80);
        Assert.Equal(Roles.Analyst, result.User.Role);
    }

    [Fact]
    public async Task Refresh_rotates_token_and_old_token_stops_working()
    {
        var (service, db) = await CreateAsync(graceSeconds: 0);
        var login = await service.LoginAsync(Login(), Client);

        var refreshed = await service.RefreshAsync(login.RefreshToken, Client);
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);

        var tokens = await db.RefreshTokens.ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.Single(tokens, t => t.RevokedReason == RevocationReasons.Rotated);
        Assert.Single(tokens, t => t.RevokedAt == null);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_the_whole_family()
    {
        var (service, db) = await CreateAsync(graceSeconds: 0);
        var login = await service.LoginAsync(Login(), Client);
        var refreshed = await service.RefreshAsync(login.RefreshToken, Client);

        // Attacker replays the stolen original token.
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(login.RefreshToken, Client));

        // The legitimate (newest) token is now revoked as well.
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(refreshed.RefreshToken, Client));
        Assert.All(await db.RefreshTokens.ToListAsync(), t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task Concurrent_refresh_within_grace_window_does_not_revoke_family()
    {
        var (service, _) = await CreateAsync(graceSeconds: 30);
        var login = await service.LoginAsync(Login(), Client);
        var refreshed = await service.RefreshAsync(login.RefreshToken, Client);

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(login.RefreshToken, Client));

        // The newer token still works.
        var again = await service.RefreshAsync(refreshed.RefreshToken, Client);
        Assert.False(string.IsNullOrEmpty(again.AccessToken));
    }

    [Fact]
    public async Task Logout_revokes_refresh_token()
    {
        var (service, _) = await CreateAsync();
        var login = await service.LoginAsync(Login(), Client);
        await service.LogoutAsync(login.RefreshToken);

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(login.RefreshToken, Client));
    }

    [Fact]
    public async Task Account_locks_after_repeated_failures()
    {
        var (service, _) = await CreateAsync();
        for (var i = 0; i < User.MaxFailedAttempts; i++)
            await Assert.ThrowsAsync<UnauthorizedException>(() => service.LoginAsync(Login("wrong"), Client));

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => service.LoginAsync(Login(), Client));
        Assert.Contains("locked", ex.Message);
    }

    [Fact]
    public async Task Change_password_revokes_other_sessions()
    {
        var (service, _) = await CreateAsync();
        var otherDevice = await service.LoginAsync(Login(), Client);
        var thisDevice = await service.LoginAsync(Login(), Client);
        var userId = thisDevice.User.Id;

        var fresh = await service.ChangePasswordAsync(userId, new ChangePasswordRequest { CurrentPassword = Password, NewPassword = "N3w!Password" }, Client);

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(otherDevice.RefreshToken, Client));
        Assert.False(string.IsNullOrEmpty((await service.RefreshAsync(fresh.RefreshToken, Client)).AccessToken));
    }

    private static LoginRequest Login(string password = Password) => new() { UserName = "Alice", Password = password };

    private static async Task<(AuthService Service, IdentityDbContext Db)> CreateAsync(int graceSeconds = 30)
    {
        var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var options = Options.Create(new JwtOptions
        {
            SigningKey = new string('k', 48),
            RefreshReuseGraceSeconds = graceSeconds
        });
        var hasher = new IdentityPasswordHasher();
        var user = new User { UserName = "alice", Email = "alice@test", FullName = "Alice", Role = Roles.Analyst };
        user.SetPassword(hasher.Hash(user, Password), DateTime.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var uow = new IdentityUnitOfWork(db);
        var service = new AuthService(uow, hasher,
            new TokenService(new JwtTokenFactory(options), options), new MfaService(uow, Protector, hasher), new NullEventSink(), NullLogger<AuthService>.Instance);
        return (service, db);
    }

    internal static readonly AesSecretProtector Protector =
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SigningKey"] = new string('k', 48) }).Build());

    internal sealed class NullEventSink : ISecurityEventSink
    {
        public List<string> Failures { get; } = [];
        public Task ReportLoginFailureAsync(string userName, string? ipAddress, string reason, CancellationToken ct = default) { Failures.Add(reason); return Task.CompletedTask; }
        public Task ReportLockoutAsync(string userName, string? ipAddress, CancellationToken ct = default) => Task.CompletedTask;
    }
}
