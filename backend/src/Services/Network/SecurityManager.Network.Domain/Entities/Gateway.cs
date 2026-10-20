using SecurityManager.Shared.Kernel.Entities;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.Network.Domain.Entities;

/// <summary>A Linux network gateway (router/firewall appliance) whose configuration is managed here.</summary>
public class Gateway : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ManagementAddress { get; set; }

    /// <summary>High-availability cluster membership (keepalived/VRRP).</summary>
    public Guid? ClusterId { get; set; }
    public HaCluster? Cluster { get; set; }
    /// <summary>VRRP priority 1–254; the highest-priority member is master.</summary>
    public int HaPriority { get; set; } = 100;

    /// <summary>Incremented on every configuration change; compared with <see cref="DeployedRevision"/>.</summary>
    public int ConfigRevision { get; set; } = 1;
    public int? DeployedRevision { get; set; }
    public DateTime? DeployedAt { get; set; }
    public string? DeployedBy { get; set; }

    public ICollection<NetworkInterface> Interfaces { get; set; } = new List<NetworkInterface>();
    public ICollection<StaticRoute> Routes { get; set; } = new List<StaticRoute>();
    public ICollection<RoutingPolicy> Policies { get; set; } = new List<RoutingPolicy>();
    public ICollection<NatRule> NatRules { get; set; } = new List<NatRule>();
    public ICollection<VpnTunnel> Tunnels { get; set; } = new List<VpnTunnel>();

    public bool HasPendingChanges => DeployedRevision != ConfigRevision;

    public void Touch() => ConfigRevision++;

    public void MarkDeployed(string? by, DateTime at)
    {
        DeployedRevision = ConfigRevision;
        DeployedAt = at;
        DeployedBy = by;
    }

    public IEnumerable<string> Validate()
    {
        if (Name.Trim().Length is < 2 or > 100) yield return "Name must be 2–100 characters.";
        if (!NetworkNames.IsHostname(Hostname)) yield return $"'{Hostname}' is not a valid hostname.";
        if (!string.IsNullOrEmpty(ManagementAddress) && !IpText.IsValid(ManagementAddress)) yield return "Management address must be an IP address.";
        if (HaPriority is < 1 or > 254) yield return "HA priority must be between 1 and 254.";
    }
}

/// <summary>Base for configuration items owned by a gateway.</summary>
public abstract class GatewayChild : BaseEntity
{
    public Guid GatewayId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? Description { get; set; }

    public abstract IEnumerable<string> Validate();
}
