namespace SecurityManager.Firewall.Domain.Enums;

public enum RuleAction
{
    Allow,
    Deny
}

public enum NetworkProtocol
{
    Any,
    Tcp,
    Udp,
    Icmp
}

public enum TrafficDirection
{
    Inbound,
    Outbound,
    Any
}

public enum IpListType
{
    /// <summary>Trusted: always allowed, overriding the blocklist and all rules.</summary>
    Allow,
    /// <summary>Always denied (unless also allowlisted).</summary>
    Block
}

public enum IpListSource
{
    Manual,
    Import,
    ThreatIntel,
    /// <summary>Added automatically from a Security Service detection.</summary>
    Detection,
    /// <summary>Country address blocks from a geo feed.</summary>
    Country
}
