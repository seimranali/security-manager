namespace SecurityManager.Network.Domain.Enums;

public enum InterfaceType { Physical, Vlan, Bridge, Bond, Loopback }

public enum InterfaceZone { Wan, Lan, Dmz, Management, Guest, Other }

public enum AddressingMode { Static, Dhcp, None }

public enum NatType
{
    /// <summary>Source NAT to the outgoing interface's address (typical LAN → internet).</summary>
    Masquerade,
    /// <summary>Source NAT to a fixed address.</summary>
    SourceNat,
    /// <summary>Destination NAT of a port (or range) to an internal host.</summary>
    PortForward,
    /// <summary>Bidirectional static mapping public IP ↔ private IP.</summary>
    OneToOne
}

public enum NatProtocol { Any, Tcp, Udp }

public enum VpnType { WireGuard, IpsecSiteToSite }

public enum IssueSeverity { Error, Warning, Info }
