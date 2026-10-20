using System.Security.Cryptography;
using System.Text;
using SecurityManager.Identity.Application.Abstractions;
using SecurityManager.Identity.Application.DTOs;
using SecurityManager.Identity.Domain.Entities;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Identity.Application.Services;

public interface IMfaService
{
    /// <summary>Starts enrolment: generates (or regenerates) a secret the user adds to an authenticator app.</summary>
    Task<MfaSetupDto> BeginSetupAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Confirms enrolment with a code from the app; returns one-time recovery codes (shown once).</summary>
    Task<MfaEnabledDto> EnableAsync(Guid userId, string code, CancellationToken ct = default);
    Task DisableAsync(Guid userId, MfaDisableRequest request, CancellationToken ct = default);
    /// <summary>Administrator reset for a user who lost their device.</summary>
    Task ResetAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Checks a TOTP or unused recovery code during sign-in (marks it used on the entity; the caller saves).</summary>
    bool VerifyLoginCode(User user, string code, DateTime now);
}

public sealed class MfaService(IIdentityUnitOfWork uow, ISecretProtector protector, IPasswordHasher hasher) : IMfaService
{
    private const string Issuer = "Security Manager";
    private const int RecoveryCodeCount = 10;

    public async Task<MfaSetupDto> BeginSetupAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await Get(userId, ct);
        if (user.MfaEnabled) throw new BusinessRuleException("Multi-factor authentication is already enabled. Disable it first to enrol a new device.");

        var secret = Totp.NewSecret();
        user.MfaSecret = protector.Protect(secret);
        await uow.SaveChangesAsync(ct);
        return new MfaSetupDto(Totp.ToBase32(secret), Totp.ProvisioningUri(Issuer, user.UserName, secret));
    }

    public async Task<MfaEnabledDto> EnableAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var user = await Get(userId, ct);
        if (user.MfaEnabled) throw new BusinessRuleException("Multi-factor authentication is already enabled.");
        if (user.MfaSecret is null) throw new BusinessRuleException("Start the setup first.");

        var step = Totp.Verify(protector.Unprotect(user.MfaSecret), code, DateTime.UtcNow, user.MfaLastStep)
                   ?? throw new BusinessRuleException("The code is not valid. Check the time on your phone and try the newest code.");

        var codes = Enumerable.Range(0, RecoveryCodeCount).Select(_ => NewRecoveryCode()).ToList();
        user.MfaEnabled = true;
        user.MfaLastStep = step;
        user.MfaRecoveryCodes = string.Join(';', codes.Select(HashCode));
        await uow.SaveChangesAsync(ct);
        return new MfaEnabledDto(codes);
    }

    public async Task DisableAsync(Guid userId, MfaDisableRequest request, CancellationToken ct = default)
    {
        var user = await Get(userId, ct);
        if (!user.MfaEnabled) return;
        if (!hasher.Verify(user, user.PasswordHash, request.Password)) throw new BusinessRuleException("Password is incorrect.");
        if (!VerifyLoginCode(user, request.Code, DateTime.UtcNow)) throw new BusinessRuleException("The verification code is not valid.");
        Clear(user);
        await uow.SaveChangesAsync(ct);
    }

    public async Task ResetAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await Get(userId, ct);
        Clear(user);
        // Existing sessions were established with the old factor; sign the user out everywhere.
        await AuthService.RevokeAllForUserAsync(uow, userId, DateTime.UtcNow, RevocationReasons.PasswordChanged, ct);
        await uow.SaveChangesAsync(ct);
    }

    public bool VerifyLoginCode(User user, string code, DateTime now)
    {
        if (!user.MfaEnabled || user.MfaSecret is null) return false;

        var step = Totp.Verify(protector.Unprotect(user.MfaSecret), code, now, user.MfaLastStep);
        if (step is not null)
        {
            user.MfaLastStep = step.Value;
            return true;
        }

        // Recovery codes are single-use.
        var hashes = (user.MfaRecoveryCodes ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        var hash = HashCode(code);
        if (!hashes.Remove(hash)) return false;
        user.MfaRecoveryCodes = string.Join(';', hashes);
        return true;
    }

    private static void Clear(User user)
    {
        user.MfaEnabled = false;
        user.MfaSecret = null;
        user.MfaRecoveryCodes = null;
        user.MfaLastStep = 0;
    }

    private async Task<User> Get(Guid id, CancellationToken ct) =>
        await uow.Users.GetByIdAsync(id, ct: ct) ?? throw new NotFoundException("User", id);

    private static string NewRecoveryCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I
        var chars = Enumerable.Range(0, 10).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray();
        return $"{new string(chars, 0, 5)}-{new string(chars, 5, 5)}";
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Replace("-", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant())));
}
