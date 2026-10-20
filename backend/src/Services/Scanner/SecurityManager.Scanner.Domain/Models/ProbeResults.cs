namespace SecurityManager.Scanner.Domain.Models;

/// <summary>Raw result of a TCP connect probe.</summary>
public sealed record PortProbeResult(int Port, bool IsOpen, string? Banner = null);

/// <summary>Raw result of an HTTP(S) request used for header analysis.</summary>
public sealed record HttpProbeResult(
    string Url,
    int? StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    string? Error = null);

/// <summary>Raw result of a TLS handshake.</summary>
public sealed record TlsProbeResult(
    string Host,
    int Port,
    string? Protocol,
    string? Subject,
    string? Issuer,
    DateTime? NotBefore,
    DateTime? NotAfter,
    bool ChainTrusted,
    bool NameMismatch,
    IReadOnlyList<string> ChainErrors,
    string? Error = null);
