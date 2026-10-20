using SecurityManager.Network.Domain.Enums;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.Network.Domain.Entities;

public class NatRule : GatewayChild
{
    public string Name { get; set; } = string.Empty;
    public NatType Type { get; set; } = NatType.Masquerade;
    public NatProtocol Protocol { get; set; } = NatProtocol.Any;
    public string? InInterface { get; set; }
    public string? OutInterface { get; set; }
    /// <summary>Match source (IP or CIDR).</summary>
    public string? Source { get; set; }
    /// <summary>Match destination (IP or CIDR). For one-to-one NAT this is the public address.</summary>
    public string? Destination { get; set; }
    /// <summary>Port or range ("443", "8000-8100") for port forwards.</summary>
    public string? DestinationPort { get; set; }
    public string? TranslatedAddress { get; set; }
    public int? TranslatedPort { get; set; }
    public int Priority { get; set; } = 100;

    public bool UsesPorts => Protocol != NatProtocol.Any && !string.IsNullOrWhiteSpace(DestinationPort);

    public override IEnumerable<string> Validate()
    {
        if (Name.Trim().Length is < 2 or > 100) yield return "Name must be 2–100 characters.";
        if (!string.IsNullOrWhiteSpace(InInterface) && !NetworkNames.IsInterfaceName(InInterface)) yield return "Invalid input interface name.";
        if (!string.IsNullOrWhiteSpace(OutInterface) && !NetworkNames.IsInterfaceName(OutInterface)) yield return "Invalid output interface name.";

        foreach (var (label, value) in new[] { ("Source", Source), ("Destination", Destination) })
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!Cidr.TryParseHostOrNetwork(value, out var c)) yield return $"{label} must be an IP or CIDR.";
            else if (!c.IsIPv4) yield return "NAT rules support IPv4 only (IPv6 should be routed, not translated).";
        }

        if (!string.IsNullOrWhiteSpace(TranslatedAddress))
        {
            if (!IpText.TryParse(TranslatedAddress, out var t)) yield return "Translated address must be an IP address.";
            else if (t.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) yield return "NAT rules support IPv4 only.";
        }

        if (!string.IsNullOrWhiteSpace(DestinationPort))
        {
            if (Protocol == NatProtocol.Any) yield return "Choose TCP or UDP when matching ports.";
            if (!PortSpec.TryParseRange(DestinationPort.Trim(), out _, out _) || PortSpec.IsAny(DestinationPort))
                yield return "Destination port must be a single port or a range such as 8000-8100.";
        }
        if (TranslatedPort is not null and (< 1 or > 65535)) yield return "Translated port must be between 1 and 65535.";

        switch (Type)
        {
            case NatType.Masquerade:
                if (string.IsNullOrWhiteSpace(OutInterface)) yield return "Masquerade needs an output interface.";
                if (!string.IsNullOrWhiteSpace(TranslatedAddress)) yield return "Masquerade uses the interface address; remove the translated address.";
                break;
            case NatType.SourceNat:
                if (string.IsNullOrWhiteSpace(OutInterface)) yield return "Source NAT needs an output interface.";
                if (string.IsNullOrWhiteSpace(TranslatedAddress)) yield return "Source NAT needs a translated (public) address.";
                break;
            case NatType.PortForward:
                if (string.IsNullOrWhiteSpace(InInterface)) yield return "Port forwards need an input interface.";
                if (Protocol == NatProtocol.Any) yield return "Port forwards need TCP or UDP.";
                if (string.IsNullOrWhiteSpace(DestinationPort)) yield return "Port forwards need a destination port.";
                if (string.IsNullOrWhiteSpace(TranslatedAddress)) yield return "Port forwards need an internal (translated) address.";
                if (TranslatedPort is not null && PortSpec.TryParseRange(DestinationPort?.Trim() ?? "", out var lo, out var hi) && lo != hi)
                    yield return "A port range is forwarded as-is; leave the translated port empty.";
                break;
            case NatType.OneToOne:
                if (!IpText.IsValid(Destination)) yield return "One-to-one NAT needs the public IP as destination.";
                if (string.IsNullOrWhiteSpace(TranslatedAddress)) yield return "One-to-one NAT needs the private (translated) IP.";
                break;
        }
    }
}
