using System.Text;
using Microsoft.EntityFrameworkCore;
using SecurityManager.Identity.Application.DTOs;
using SecurityManager.Identity.Application.Services;
using SecurityManager.Identity.Domain.Entities;
using SecurityManager.Identity.Infrastructure.Persistence;
using SecurityManager.Identity.Infrastructure.Security;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.UnitTests.Identity;

public class MfaTests
{
    // RFC 6238 Appendix B test secret (ASCII "12345678901234567890"), SHA-1, truncated to 6 digits.
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Totp_matches_the_rfc_6238_test_vectors(long unixTime, string expected) =>
        Assert.Equal(expected, Totp.Code(RfcSecret, unixTime / 30));

    [Fact]
    public void Base32_round_trips()
    {
        var secret = Totp.NewSecret();
        Assert.Equal(secret, Totp.FromBase32(Totp.ToBase32(secret)));
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Totp.ToBase32(RfcSecret));
    }

    [Fact]
    public void A_code_is_accepted_once_within_clock_drift()
    {
        var now = DateTime.UtcNow;
        var code = Totp.Code(RfcSecret, Totp.StepAt(now) - 1);
        var step = Totp.Verify(RfcSecret, code, now, lastUsedStep: 0);
        Assert.NotNull(step);
        Assert.Null(Totp.Verify(RfcSecret, code, now, lastUsedStep: step!.Value)); // replay rejected
        Assert.Null(Totp.Verify(RfcSecret, "000000x", now, 0));
    }

    [Fact]
    public void Secrets_are_encrypted_at_rest()
    {
        var secret = Totp.NewSecret();
        var stored = AuthServiceTests.Protector.Protect(secret);
        Assert.DoesNotContain(Totp.ToBase32(secret), stored);
        Assert.Equal(secret, AuthServiceTests.Protector.Unprotect(stored));
    }

    [Fact]
    public async Task Login_requires_a_code_once_mfa_is_enabled_and_recovery_codes_work_once()
    {
        var (auth, mfa, db, userId) = await CreateAsync();
        var setup = await mfa.BeginSetupAsync(userId);
        var secret = Totp.FromBase32(setup.Secret);
        Assert.StartsWith("otpauth://totp/", setup.ProvisioningUri);

        var enabled = await mfa.EnableAsync(userId, Totp.Code(secret, Totp.StepAt(DateTime.UtcNow)));
        Assert.Equal(10, enabled.RecoveryCodes.Count);

        await Assert.ThrowsAsync<MfaRequiredException>(() => auth.LoginAsync(Login(), Client));
        await Assert.ThrowsAsync<UnauthorizedException>(() => auth.LoginAsync(Login("123456"), Client));

        var recovery = enabled.RecoveryCodes[0];
        var ok = await auth.LoginAsync(Login(recovery), Client);
        Assert.True(ok.User.MfaEnabled);
        await Assert.ThrowsAsync<UnauthorizedException>(() => auth.LoginAsync(Login(recovery), Client)); // single use

        var history = await auth.GetLoginHistoryAsync(userId, 10);
        Assert.Equal(2, history.Count(h => h.FailureReason == LoginFailureReasons.BadMfaCode));
        Assert.Single(history, h => h.Succeeded && h.UsedMfa);
    }

    [Fact]
    public async Task Failed_logins_are_audited_and_reported_including_unknown_users()
    {
        var sink = new AuthServiceTests.NullEventSink();
        var (auth, _, db, _) = await CreateAsync(sink);
        await Assert.ThrowsAsync<UnauthorizedException>(() => auth.LoginAsync(new LoginRequest { UserName = "alice", Password = "wrong" }, Client));
        await Assert.ThrowsAsync<UnauthorizedException>(() => auth.LoginAsync(new LoginRequest { UserName = "mallory", Password = "x" }, Client));

        Assert.Equal([LoginFailureReasons.BadPassword, LoginFailureReasons.UnknownUser], sink.Failures);
        Assert.Equal(2, await db.LoginAttempts.CountAsync(a => !a.Succeeded && a.IpAddress == "10.0.0.9"));
    }

    private const string Password = "S3cure!Pass";
    private static readonly ClientInfo Client = new("10.0.0.9", "tests");
    private static LoginRequest Login(string? code = null) => new() { UserName = "alice", Password = Password, Code = code };

    private static async Task<(AuthService, MfaService, IdentityDbContext, Guid)> CreateAsync(AuthServiceTests.NullEventSink? sink = null)
    {
        var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var options = Microsoft.Extensions.Options.Options.Create(new SecurityManager.Shared.Infrastructure.Security.JwtOptions { SigningKey = new string('k', 48) });
        var hasher = new IdentityPasswordHasher();
        var user = new User { UserName = "alice", Email = "alice@test", FullName = "Alice", Role = "Analyst" };
        user.SetPassword(hasher.Hash(user, Password), DateTime.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var uow = new IdentityUnitOfWork(db);
        var mfa = new MfaService(uow, AuthServiceTests.Protector, hasher);
        var auth = new AuthService(uow, hasher, new TokenService(new SecurityManager.Shared.Infrastructure.Security.JwtTokenFactory(options), options),
            mfa, sink ?? new AuthServiceTests.NullEventSink(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthService>.Instance);
        return (auth, mfa, db, user.Id);
    }
}
