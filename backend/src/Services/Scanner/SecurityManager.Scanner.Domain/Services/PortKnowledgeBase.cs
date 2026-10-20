using SecurityManager.Shared.Kernel.Enums;

namespace SecurityManager.Scanner.Domain.Services;

public sealed record PortInfo(string Service, Severity Severity, string Risk, string Recommendation);

/// <summary>Risk classification of commonly exposed services.</summary>
public static class PortKnowledgeBase
{
    private static readonly Dictionary<int, PortInfo> Ports = new()
    {
        [21] = new("FTP", Severity.High, "FTP transmits credentials and data in cleartext.", "Disable FTP; use SFTP/FTPS instead."),
        [22] = new("SSH", Severity.Low, "SSH is exposed; it is a frequent brute-force target.", "Restrict by source IP, disable password auth, use keys + MFA."),
        [23] = new("Telnet", Severity.Critical, "Telnet sends credentials in cleartext and is trivially intercepted.", "Disable Telnet immediately and use SSH."),
        [25] = new("SMTP", Severity.Medium, "Mail server exposed; may allow open relay or user enumeration.", "Verify relay restrictions and require STARTTLS."),
        [53] = new("DNS", Severity.Low, "DNS service exposed; may allow zone transfer or amplification.", "Disable recursion for external clients and restrict AXFR."),
        [80] = new("HTTP", Severity.Low, "Unencrypted web service.", "Redirect all HTTP traffic to HTTPS."),
        [110] = new("POP3", Severity.Medium, "POP3 may transmit credentials in cleartext.", "Use POP3S (995) or disable."),
        [135] = new("MS-RPC", Severity.High, "Windows RPC endpoint mapper exposed; historically wormable.", "Block at the perimeter."),
        [139] = new("NetBIOS", Severity.High, "NetBIOS session service leaks host information.", "Block NetBIOS at the perimeter."),
        [143] = new("IMAP", Severity.Medium, "IMAP may transmit credentials in cleartext.", "Use IMAPS (993) or disable."),
        [443] = new("HTTPS", Severity.Info, "Encrypted web service.", "Keep TLS configuration and certificates current."),
        [445] = new("SMB", Severity.Critical, "SMB exposed to the network — a primary ransomware vector (e.g. EternalBlue).", "Never expose SMB externally; block TCP 445."),
        [1433] = new("MS SQL Server", Severity.High, "Database port exposed.", "Restrict to application servers; never expose to the internet."),
        [1521] = new("Oracle DB", Severity.High, "Database port exposed.", "Restrict to application servers."),
        [3306] = new("MySQL", Severity.High, "Database port exposed.", "Bind to localhost or restrict by firewall."),
        [3389] = new("RDP", Severity.High, "Remote Desktop exposed; heavily targeted by brute force and exploits.", "Put RDP behind VPN/gateway and enforce NLA + MFA."),
        [5432] = new("PostgreSQL", Severity.High, "Database port exposed.", "Restrict access via pg_hba.conf and firewall."),
        [5900] = new("VNC", Severity.High, "VNC often has weak or no authentication.", "Tunnel VNC over SSH/VPN."),
        [6379] = new("Redis", Severity.Critical, "Redis is frequently deployed without authentication, allowing RCE.", "Enable AUTH/ACLs, bind to localhost, enable protected-mode."),
        [8080] = new("HTTP-Alt", Severity.Medium, "Alternate web port, often admin consoles or dev servers.", "Verify the service is intended to be exposed."),
        [8443] = new("HTTPS-Alt", Severity.Low, "Alternate HTTPS port, often admin consoles.", "Verify the service is intended to be exposed."),
        [9200] = new("Elasticsearch", Severity.Critical, "Elasticsearch HTTP API exposed; often unauthenticated.", "Enable security features and restrict network access."),
        [27017] = new("MongoDB", Severity.Critical, "MongoDB exposed; unauthenticated instances are mass-exploited.", "Enable authentication and bind to private interfaces.")
    };

    public static PortInfo Lookup(int port) =>
        Ports.TryGetValue(port, out var info)
            ? info
            : new PortInfo("Unknown", Severity.Low, "Unrecognised service listening on this port.", "Confirm the service is required; otherwise close the port.");
}
