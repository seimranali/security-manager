using SecurityManager.Network.Domain.Enums;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.Network.Domain.Entities;

public class NetworkInterface : GatewayChild
{
    public string Name { get; set; } = string.Empty;
    public InterfaceType Type { get; set; } = InterfaceType.Physical;
    public InterfaceZone Zone { get; set; } = InterfaceZone.Lan;
    public AddressingMode Mode { get; set; } = AddressingMode.Static;
    /// <summary>Interface address in CIDR form, e.g. "10.0.0.1/24" (static mode only).</summary>
    public string? Address { get; set; }
    public int? Mtu { get; set; }
    /// <summary>VLAN id (VLAN interfaces).</summary>
    public int? VlanId { get; set; }
    /// <summary>Parent interface (VLAN interfaces).</summary>
    public string? Parent { get; set; }
    /// <summary>Comma-separated member interfaces (bond/bridge).</summary>
    public string? Members { get; set; }

    public Cidr? AddressCidr => Cidr.TryParse(Address, out var c) ? c : null;

    public override IEnumerable<string> Validate()
    {
        if (!NetworkNames.IsInterfaceName(Name)) yield return $"'{Name}' is not a valid interface name (letters, digits, . _ -; max 15).";

        if (Mode == AddressingMode.Static)
        {
            if (!Cidr.TryParse(Address, out var cidr)) yield return "Static interfaces need an address in CIDR form, e.g. 10.0.0.1/24.";
            else if (cidr.IsNetworkAddress) yield return $"{Address} is the network address of its subnet; use a host address (e.g. the first usable address).";
            else if (Mtu is < 1280 && !cidr.IsIPv4) yield return "IPv6 requires an MTU of at least 1280.";
        }
        else if (!string.IsNullOrEmpty(Address)) yield return $"Address is only used with static addressing ({Mode} selected).";

        if (Mtu is not null and (< 576 or > 9216)) yield return "MTU must be between 576 and 9216.";

        if (Type == InterfaceType.Vlan)
        {
            if (VlanId is null or < 1 or > 4094) yield return "VLAN id must be between 1 and 4094.";
            if (!NetworkNames.IsInterfaceName(Parent)) yield return "VLAN interfaces need a parent interface.";
        }
        else if (VlanId is not null) yield return "VLAN id only applies to VLAN interfaces.";

        if (Type is InterfaceType.Bond or InterfaceType.Bridge)
        {
            foreach (var member in NetworkNames.SplitList(Members).Where(m => !NetworkNames.IsInterfaceName(m)))
                yield return $"'{member}' is not a valid member interface name.";
        }
        else if (!string.IsNullOrWhiteSpace(Members)) yield return "Members only apply to bond and bridge interfaces.";
    }
}
