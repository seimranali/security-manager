using SecurityManager.Firewall.Domain.Enums;

namespace SecurityManager.Firewall.Domain.ValueObjects;

/// <summary>A network connection attempt to be evaluated against firewall policies.</summary>
public sealed record TrafficPacket(
    string SourceIp,
    string DestinationIp,
    int? SourcePort,
    int? DestinationPort,
    NetworkProtocol Protocol,
    TrafficDirection Direction);
