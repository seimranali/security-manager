using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SecurityManager.Identity.Application.Abstractions;
using SecurityManager.Identity.Domain.Entities;
using SecurityManager.Shared.Infrastructure.Security;
using SecurityManager.Shared.Kernel.Security;
using IAppPasswordHasher = SecurityManager.Identity.Application.Abstractions.IPasswordHasher;

namespace SecurityManager.Identity.Infrastructure.Security;

public sealed class TokenService(IJwtTokenFactory factory, IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public TimeSpan RefreshReuseGrace => TimeSpan.FromSeconds(_options.RefreshReuseGraceSeconds);

    public AccessToken CreateAccessToken(User user)
    {
        var issued = factory.Create(
        [
            new Claim(AppClaims.Subject, user.Id.ToString()),
            new Claim(AppClaims.Name, user.UserName),
            new Claim(AppClaims.FullName, user.FullName),
            new Claim(AppClaims.Role, user.Role)
        ], TimeSpan.FromMinutes(_options.AccessTokenMinutes));
        return new AccessToken(issued.Token, issued.ExpiresAt);
    }

    public GeneratedRefreshToken GenerateRefreshToken(DateTime now)
    {
        // 512 bits of CSPRNG entropy, URL-safe.
        var raw = Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        return new GeneratedRefreshToken(raw, HashRefreshToken(raw), now.AddDays(_options.RefreshTokenDays));
    }

    public string HashRefreshToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Adapter over ASP.NET Core Identity's PBKDF2 password hasher.</summary>
public sealed class IdentityPasswordHasher : IAppPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(User user, string hash, string password) =>
        _hasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed;
}
