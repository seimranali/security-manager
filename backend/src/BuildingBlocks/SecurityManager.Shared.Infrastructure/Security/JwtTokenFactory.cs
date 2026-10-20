using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SecurityManager.Shared.Kernel.Security;

namespace SecurityManager.Shared.Infrastructure.Security;

public sealed record IssuedToken(string Token, DateTime ExpiresAt);

public interface IJwtTokenFactory
{
    IssuedToken Create(IEnumerable<Claim> claims, TimeSpan lifetime);
}

public sealed class JwtTokenFactory(IOptions<JwtOptions> options) : IJwtTokenFactory
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly JwtOptions _options = options.Value;

    public IssuedToken Create(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        var now = DateTime.UtcNow;
        var expires = now.Add(lifetime);
        var identity = new ClaimsIdentity(claims);
        identity.AddClaim(new Claim(AppClaims.TokenId, Guid.NewGuid().ToString("N")));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = identity,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(CreateKey(_options.SigningKey), SecurityAlgorithms.HmacSha256)
        });

        return new IssuedToken(token, expires);
    }

    public static SymmetricSecurityKey CreateKey(string signingKey)
    {
        var bytes = Encoding.UTF8.GetBytes(signingKey);
        if (bytes.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes (256 bits).");
        return new SymmetricSecurityKey(bytes);
    }
}
