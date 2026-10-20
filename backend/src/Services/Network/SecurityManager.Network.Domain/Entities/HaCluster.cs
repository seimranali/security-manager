using SecurityManager.Shared.Kernel.Entities;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.Network.Domain.Entities;

/// <summary>
/// Active/passive high-availability pair (or group) of gateways sharing virtual IPs via VRRP (keepalived).
/// The enabled member with the highest priority is master; others take over on failure.
/// </summary>
public class HaCluster : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Base VRRP virtual router id; each VIP interface uses base + index (must stay ≤ 255 and unique on the segment).</summary>
    public int VrrpRouterId { get; set; } = 51;
    public int AdvertIntervalSeconds { get; set; } = 1;
    /// <summary>When false, a recovered higher-priority node does not take mastership back automatically.</summary>
    public bool Preempt { get; set; } = true;

    public ICollection<Gateway> Members { get; set; } = new List<Gateway>();
    public ICollection<VirtualIp> VirtualIps { get; set; } = new List<VirtualIp>();

    /// <summary>Highest priority wins; ties are broken by name so the result is deterministic.</summary>
    public static Gateway? ElectMaster(IEnumerable<Gateway> members) =>
        members.OrderByDescending(m => m.HaPriority).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

    public IEnumerable<string> Validate()
    {
        if (Name.Trim().Length is < 2 or > 100) yield return "Name must be 2–100 characters.";
        if (VrrpRouterId is < 1 or > 255) yield return "VRRP router id must be between 1 and 255.";
        if (AdvertIntervalSeconds is < 1 or > 255) yield return "Advertisement interval must be between 1 and 255 seconds.";
    }
}

/// <summary>A floating address held by the current master on the given interface.</summary>
public class VirtualIp : BaseEntity
{
    public Guid ClusterId { get; set; }
    public string Interface { get; set; } = string.Empty;
    /// <summary>Virtual address in CIDR form, e.g. "10.0.0.1/24".</summary>
    public string Address { get; set; } = string.Empty;
    public string? Description { get; set; }

    public IEnumerable<string> Validate()
    {
        if (!NetworkNames.IsInterfaceName(Interface)) yield return $"'{Interface}' is not a valid interface name.";
        if (!Cidr.TryParse(Address, out var cidr)) yield return "Virtual IP must be in CIDR form, e.g. 10.0.0.1/24.";
        else if (cidr.IsNetworkAddress) yield return "Virtual IP must be a host address, not the network address.";
    }
}
