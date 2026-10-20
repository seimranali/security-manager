using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace SecurityManager.Shared.Infrastructure.Security;

/// <summary>
/// AES-256-GCM encryption for small secrets stored in the database (MFA seeds, SMTP passwords, webhook secrets).
/// The key is derived (HKDF) from "Security:EncryptionKey", falling back to the JWT signing key so no extra setup is
/// needed; each <paramref name="purpose"/> gets its own key. Changing the key material makes stored secrets unreadable.
/// </summary>
public sealed class SecretBox
{
    private const string Prefix = "v1:";
    private readonly byte[] _key;

    public SecretBox(IConfiguration config, string purpose)
    {
        var material = config["Security:EncryptionKey"] ?? config["Jwt:SigningKey"]
                       ?? throw new InvalidOperationException("No key configured for secret encryption (Security:EncryptionKey or Jwt:SigningKey).");
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(material), 32, info: Encoding.UTF8.GetBytes($"SecurityManager.{purpose}.v1"));
    }

    public string Protect(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintext, cipher, tag);
        return Prefix + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public byte[] Unprotect(string protectedText)
    {
        if (!protectedText.StartsWith(Prefix, StringComparison.Ordinal)) throw new CryptographicException("Unknown secret format.");
        var data = Convert.FromBase64String(protectedText[Prefix.Length..]);
        var plaintext = new byte[data.Length - 28];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plaintext);
        return plaintext;
    }

    public string ProtectString(string value) => Protect(Encoding.UTF8.GetBytes(value));
    public string UnprotectString(string value) => Encoding.UTF8.GetString(Unprotect(value));
}
