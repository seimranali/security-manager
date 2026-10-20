using Microsoft.Extensions.Configuration;
using SecurityManager.Identity.Application.Abstractions;
using SecurityManager.Shared.Infrastructure.Security;

namespace SecurityManager.Identity.Infrastructure.Security;

/// <summary>Encrypts MFA secrets at rest (AES-256-GCM, see <see cref="SecretBox"/>).</summary>
public sealed class AesSecretProtector(IConfiguration config) : ISecretProtector
{
    private readonly SecretBox _box = new(config, "Identity.Secrets");

    public string Protect(byte[] plaintext) => _box.Protect(plaintext);
    public byte[] Unprotect(string protectedText) => _box.Unprotect(protectedText);
}
