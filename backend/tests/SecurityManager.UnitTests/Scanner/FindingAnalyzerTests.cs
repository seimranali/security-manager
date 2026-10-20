using SecurityManager.Scanner.Domain.Entities;
using SecurityManager.Scanner.Domain.Models;
using SecurityManager.Scanner.Domain.Services;
using SecurityManager.Shared.Kernel.Enums;

namespace SecurityManager.UnitTests.Scanner;

public class FindingAnalyzerTests
{
    [Fact]
    public void Open_risky_ports_produce_findings_with_knowledge_base_severity()
    {
        var findings = FindingAnalyzer.AnalyzePorts([
            new PortProbeResult(23, true),
            new PortProbeResult(443, true),
            new PortProbeResult(3389, false)
        ]).ToList();

        Assert.Equal(2, findings.Count);
        Assert.Equal(Severity.Critical, findings.Single(f => f.Port == 23).Severity);
        Assert.DoesNotContain(findings, f => f.Port == 3389);
    }

    [Fact]
    public void Missing_security_headers_are_reported()
    {
        var probe = new HttpProbeResult("https://example.test/", 200, new Dictionary<string, string>
        {
            ["Server"] = "nginx/1.18.0",
            ["X-Content-Type-Options"] = "nosniff",
            ["Set-Cookie"] = "session=abc; Path=/"
        });

        var titles = FindingAnalyzer.AnalyzeHttp(probe).Select(f => f.Title).ToList();

        Assert.Contains("Missing HTTP Strict-Transport-Security", titles);
        Assert.Contains("Missing Content-Security-Policy", titles);
        Assert.Contains("Server version disclosure", titles);
        Assert.Contains("Insecure cookie 'session'", titles);
        Assert.DoesNotContain("Missing X-Content-Type-Options", titles);
    }

    [Fact]
    public void Expired_certificate_is_critical()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tls = new TlsProbeResult("example.test", 443, "Tls13", "CN=example.test", "CN=CA",
            now.AddYears(-1), now.AddDays(-1), ChainTrusted: true, NameMismatch: false, ChainErrors: []);

        var findings = FindingAnalyzer.AnalyzeTls(tls, now).ToList();

        Assert.Contains(findings, f => f.Severity == Severity.Critical && f.Title.Contains("expired"));
    }

    [Fact]
    public void Risk_score_is_capped_at_100()
    {
        var findings = Enumerable.Range(0, 5).Select(_ => new ScanFinding { Severity = Severity.Critical });
        Assert.Equal(100, RiskCalculator.Score(findings));
    }
}
