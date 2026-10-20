using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using SecurityManager.Scanner.Application.Abstractions;
using SecurityManager.Scanner.Domain.Models;

namespace SecurityManager.Scanner.Infrastructure.Scanning;

/// <summary>Performs a TLS handshake and captures certificate and chain validation details.</summary>
public sealed class TlsCertificateProbe : ITlsProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    public async Task<TlsProbeResult> ProbeAsync(string host, int port, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, cts.Token);

            var policyErrors = SslPolicyErrors.None;
            X509Certificate2? certificate = null;
            var chainErrors = new List<string>();

            await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
                (_, cert, chain, errors) =>
                {
                    policyErrors = errors;
                    if (cert is not null)
                        certificate = cert as X509Certificate2 ?? X509CertificateLoader.LoadCertificate(cert.GetRawCertData());
                    if (chain is not null)
                        chainErrors.AddRange(chain.ChainStatus
                            .Where(s => s.Status != X509ChainStatusFlags.NoError)
                            .Select(s => s.StatusInformation.Trim()));
                    return true; // accept everything; we only want to inspect it
                });

            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, cts.Token);

            return new TlsProbeResult(
                host, port,
                ssl.SslProtocol.ToString(),
                certificate?.Subject,
                certificate?.Issuer,
                certificate?.NotBefore.ToUniversalTime(),
                certificate?.NotAfter.ToUniversalTime(),
                ChainTrusted: !policyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors),
                NameMismatch: policyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch),
                ChainErrors: chainErrors.Distinct().ToList());
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return new TlsProbeResult(host, port, null, null, null, null, null, false, false, [],
                ex is OperationCanceledException ? "TLS handshake timed out." : ex.Message);
        }
    }
}
