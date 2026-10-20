using SecurityManager.Scanner.Domain.Entities;
using SecurityManager.Scanner.Domain.Models;
using SecurityManager.Shared.Kernel.Enums;

namespace SecurityManager.Scanner.Domain.Services;

/// <summary>Turns raw probe results into security findings. Pure and deterministic.</summary>
public static class FindingAnalyzer
{
    public const string NetworkCategory = "Network";
    public const string WebCategory = "Web";
    public const string TlsCategory = "TLS";

    public static IEnumerable<ScanFinding> AnalyzePorts(IEnumerable<PortProbeResult> results)
    {
        foreach (var r in results.Where(r => r.IsOpen))
        {
            var info = PortKnowledgeBase.Lookup(r.Port);
            yield return new ScanFinding
            {
                Title = $"Open port {r.Port}/tcp ({info.Service})",
                Description = info.Risk,
                Severity = info.Severity,
                Category = NetworkCategory,
                Port = r.Port,
                Evidence = string.IsNullOrWhiteSpace(r.Banner) ? "TCP connect succeeded" : $"Banner: {r.Banner}",
                Recommendation = info.Recommendation
            };
        }
    }

    public static IEnumerable<ScanFinding> AnalyzeHttp(HttpProbeResult probe)
    {
        if (probe.Error is not null)
        {
            yield return Web("Web service unreachable", $"Could not retrieve {probe.Url}.", Severity.Info,
                probe.Error, "Verify the host exposes a web service if one is expected.");
            yield break;
        }

        var h = new Dictionary<string, string>(probe.Headers, StringComparer.OrdinalIgnoreCase);
        var isHttps = probe.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var csp = h.GetValueOrDefault("Content-Security-Policy");

        if (!isHttps)
            yield return Web("Site served over unencrypted HTTP", "Traffic can be intercepted or modified in transit.",
                Severity.Medium, probe.Url, "Serve the site over HTTPS and redirect HTTP to HTTPS.");

        if (isHttps && !h.ContainsKey("Strict-Transport-Security"))
            yield return Web("Missing HTTP Strict-Transport-Security", "Browsers may be downgraded to HTTP (SSL stripping).",
                Severity.Medium, "Header not present", "Add 'Strict-Transport-Security: max-age=31536000; includeSubDomains'.");

        if (csp is null)
            yield return Web("Missing Content-Security-Policy", "No CSP to mitigate cross-site scripting and data injection.",
                Severity.Medium, "Header not present", "Define a restrictive Content-Security-Policy.");

        if (!h.ContainsKey("X-Frame-Options") && (csp is null || !csp.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase)))
            yield return Web("Clickjacking protection missing", "The page can be embedded in a hostile frame.",
                Severity.Medium, "No X-Frame-Options or CSP frame-ancestors", "Add 'X-Frame-Options: DENY' or CSP 'frame-ancestors'.");

        if (!h.ContainsKey("X-Content-Type-Options"))
            yield return Web("Missing X-Content-Type-Options", "Browsers may MIME-sniff responses.",
                Severity.Low, "Header not present", "Add 'X-Content-Type-Options: nosniff'.");

        if (!h.ContainsKey("Referrer-Policy"))
            yield return Web("Missing Referrer-Policy", "Full URLs may leak to third parties via the Referer header.",
                Severity.Low, "Header not present", "Add 'Referrer-Policy: strict-origin-when-cross-origin'.");

        if (!h.ContainsKey("Permissions-Policy"))
            yield return Web("Missing Permissions-Policy", "Browser features (camera, geolocation...) are not restricted.",
                Severity.Info, "Header not present", "Add a Permissions-Policy limiting unused features.");

        if (h.TryGetValue("Server", out var server) && server.Any(char.IsDigit))
            yield return Web("Server version disclosure", "The Server header reveals software version information.",
                Severity.Low, $"Server: {server}", "Remove version details from the Server header.");

        if (h.TryGetValue("X-Powered-By", out var poweredBy))
            yield return Web("Technology disclosure (X-Powered-By)", "The X-Powered-By header reveals the backend stack.",
                Severity.Low, $"X-Powered-By: {poweredBy}", "Remove the X-Powered-By header.");

        if (h.TryGetValue("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var name = cookie.Split('=', 2)[0];
                var missing = new List<string>();
                if (isHttps && !cookie.Contains("secure", StringComparison.OrdinalIgnoreCase)) missing.Add("Secure");
                if (!cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase)) missing.Add("HttpOnly");
                if (!cookie.Contains("samesite", StringComparison.OrdinalIgnoreCase)) missing.Add("SameSite");
                if (missing.Count > 0)
                    yield return Web($"Insecure cookie '{name}'", $"Cookie is missing the {string.Join(", ", missing)} attribute(s).",
                        Severity.Medium, cookie.Length > 200 ? cookie[..200] : cookie, "Set Secure, HttpOnly and SameSite on session cookies.");
            }
        }
    }

    public static IEnumerable<ScanFinding> AnalyzeTls(TlsProbeResult tls, DateTime now)
    {
        if (tls.Error is not null)
        {
            yield return Tls(tls, "TLS handshake failed", "A TLS connection could not be established.", Severity.Medium,
                tls.Error, "Ensure the service supports TLS 1.2+ with a valid certificate.");
            yield break;
        }

        if (tls.NotAfter is { } notAfter)
        {
            var days = (notAfter - now).TotalDays;
            if (days < 0)
                yield return Tls(tls, "TLS certificate expired", $"The certificate expired on {notAfter:yyyy-MM-dd}.", Severity.Critical,
                    $"NotAfter: {notAfter:u}", "Renew the certificate immediately.");
            else if (days < 14)
                yield return Tls(tls, "TLS certificate expires within 14 days", $"Expires on {notAfter:yyyy-MM-dd}.", Severity.High,
                    $"NotAfter: {notAfter:u}", "Renew the certificate now and automate renewal.");
            else if (days < 30)
                yield return Tls(tls, "TLS certificate expires within 30 days", $"Expires on {notAfter:yyyy-MM-dd}.", Severity.Medium,
                    $"NotAfter: {notAfter:u}", "Plan certificate renewal.");
        }

        if (tls.NotBefore is { } notBefore && notBefore > now)
            yield return Tls(tls, "TLS certificate not yet valid", $"Valid from {notBefore:yyyy-MM-dd}.", Severity.High,
                $"NotBefore: {notBefore:u}", "Check certificate dates and server clock.");

        if (tls.NameMismatch)
            yield return Tls(tls, "TLS certificate hostname mismatch", $"Certificate does not match host '{tls.Host}'.", Severity.High,
                $"Subject: {tls.Subject}", "Issue a certificate that covers this hostname (SAN).");

        if (!tls.ChainTrusted)
            yield return Tls(tls, "Untrusted TLS certificate chain", "The certificate chain is not trusted (self-signed or missing intermediates).",
                Severity.High, string.Join("; ", tls.ChainErrors.DefaultIfEmpty("Chain validation failed")),
                "Use a certificate from a trusted CA and serve the full chain.");

        if (tls.Protocol is "Tls" or "Tls11" or "Ssl3" or "Ssl2")
            yield return Tls(tls, $"Weak TLS protocol negotiated ({tls.Protocol})", "Legacy protocol versions are vulnerable.", Severity.High,
                $"Protocol: {tls.Protocol}", "Disable TLS 1.0/1.1; allow only TLS 1.2 and 1.3.");

        yield return Tls(tls, "TLS certificate details", $"Negotiated {tls.Protocol}.", Severity.Info,
            $"Subject: {tls.Subject} | Issuer: {tls.Issuer} | Valid: {tls.NotBefore:yyyy-MM-dd} → {tls.NotAfter:yyyy-MM-dd}", null);
    }

    private static ScanFinding Web(string title, string description, Severity severity, string? evidence, string? recommendation) => new()
    {
        Title = title, Description = description, Severity = severity, Category = WebCategory,
        Evidence = evidence, Recommendation = recommendation
    };

    private static ScanFinding Tls(TlsProbeResult tls, string title, string description, Severity severity, string? evidence, string? recommendation) => new()
    {
        Title = title, Description = description, Severity = severity, Category = TlsCategory, Port = tls.Port,
        Evidence = evidence, Recommendation = recommendation
    };
}
