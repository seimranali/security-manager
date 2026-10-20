using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Firewall.Domain.Services;
using SecurityManager.Firewall.Domain.ValueObjects;
using SecurityManager.Shared.Kernel.Entities;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.Firewall.Domain.Entities;

public class FirewallRule : BaseEntity
{
    public Guid PolicyId { get; set; }
    public FirewallPolicy? Policy { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Priority { get; set; } = 100;
    public RuleAction Action { get; set; } = RuleAction.Deny;
    public TrafficDirection Direction { get; set; } = TrafficDirection.Inbound;
    public NetworkProtocol Protocol { get; set; } = NetworkProtocol.Any;

    public string SourceAddress { get; set; } = "any";
    public string DestinationAddress { get; set; } = "any";
    public string SourcePort { get; set; } = "any";
    public string DestinationPort { get; set; } = "any";

    public bool IsEnabled { get; set; } = true;
    public long HitCount { get; set; }
    public DateTime? LastHitAt { get; set; }

    public bool Matches(TrafficPacket packet)
    {
        if (Direction != TrafficDirection.Any
            && packet.Direction != TrafficDirection.Any
            && Direction != packet.Direction)
            return false;

        if (Protocol != NetworkProtocol.Any && Protocol != packet.Protocol) return false;
        if (!AddressMatcher.IsMatch(SourceAddress, packet.SourceIp)) return false;
        if (!AddressMatcher.IsMatch(DestinationAddress, packet.DestinationIp)) return false;

        // ICMP has no ports.
        if (packet.Protocol == NetworkProtocol.Icmp) return true;

        return PortSpec.IsMatch(SourcePort, packet.SourcePort)
               && PortSpec.IsMatch(DestinationPort, packet.DestinationPort);
    }

    public void RegisterHit(DateTime at)
    {
        HitCount++;
        LastHitAt = at;
    }
}
