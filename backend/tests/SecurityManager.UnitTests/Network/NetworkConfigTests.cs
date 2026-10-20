using System.Net;
using SecurityManager.Network.Domain.Entities;
using SecurityManager.Network.Domain.Enums;
using SecurityManager.Network.Domain.Services;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.UnitTests.Network;

public class NetworkConfigTests
{
    private const string Key = "xTIBA5rboUvnH4htodjb6e697QjLERt1NAB4mZqp8Dg=";

    // ---------- CIDR ----------

    [Theory]
    [InlineData("10.0.0.1/24", "10.0.0.0/24", true)]
    [InlineData("10.0.0.0/16", "10.0.200.0/24", true)]
    [InlineData("10.0.0.0/24", "10.0.1.0/24", false)]
    [InlineData("2001:db8::1/64", "2001:db8::/48", true)]
    [InlineData("10.0.0.0/8", "2001:db8::/32", false)]
    public void Cidr_overlap(string a, string b, bool overlaps) =>
        Assert.Equal(overlaps, Cidr.Parse(a).Overlaps(Cidr.Parse(b)));

    [Fact]
    public void Cidr_contains_network_and_strict_parsing()
    {
        var lan = Cidr.Parse("192.168.1.10/24");
        Assert.Equal("192.168.1.0/24", lan.NetworkText);
        Assert.True(lan.Contains(IPAddress.Parse("192.168.1.250")));
        Assert.False(lan.Contains(IPAddress.Parse("192.168.2.1")));
        Assert.True(Cidr.Parse("10.0.0.0/24").IsNetworkAddress);
        Assert.False(Cidr.Parse("10.0.0.0/31").IsNetworkAddress); // point-to-point links may use it
        Assert.False(IpText.IsValid("10.1"));                     // IPAddress.TryParse would accept this
        Assert.False(Cidr.TryParse("10.0.0.0/33", out _));
    }

    // ---------- Item validation ----------

    [Fact]
    public void Interface_validation()
    {
        Assert.Empty(new NetworkInterface { Name = "eth0", Address = "10.0.0.1/24" }.Validate());
        Assert.NotEmpty(new NetworkInterface { Name = "eth0", Address = "10.0.0.0/24" }.Validate());              // network address
        Assert.NotEmpty(new NetworkInterface { Name = "this-name-is-way-too-long", Address = "10.0.0.1/24" }.Validate());
        Assert.NotEmpty(new NetworkInterface { Name = "eth0.10", Type = InterfaceType.Vlan, VlanId = 5000, Parent = "eth0", Address = "10.0.0.1/24" }.Validate());
        Assert.NotEmpty(new NetworkInterface { Name = "eth0", Mode = AddressingMode.Dhcp, Address = "10.0.0.1/24" }.Validate());
    }

    [Fact]
    public void Nat_validation_by_type()
    {
        Assert.Empty(new NatRule { Name = "Web", Type = NatType.PortForward, Protocol = NatProtocol.Tcp, InInterface = "eth0", DestinationPort = "443", TranslatedAddress = "10.0.0.10" }.Validate());
        Assert.NotEmpty(new NatRule { Name = "Web", Type = NatType.PortForward, Protocol = NatProtocol.Any, InInterface = "eth0", DestinationPort = "443", TranslatedAddress = "10.0.0.10" }.Validate());
        Assert.NotEmpty(new NatRule { Name = "Masq", Type = NatType.Masquerade }.Validate()); // no output interface
        Assert.NotEmpty(new NatRule { Name = "v6", Type = NatType.Masquerade, OutInterface = "eth0", Source = "2001:db8::/64" }.Validate());
    }

    [Fact]
    public void Vpn_peer_validation()
    {
        Assert.Empty(new VpnPeer { Name = "Branch", PublicKey = Key, AllowedIps = "10.30.0.0/16, 10.200.0.10/32", Endpoint = "branch.example.net:51820" }.Validate());
        Assert.NotEmpty(new VpnPeer { Name = "Bad key", PublicKey = "not-a-key", AllowedIps = "10.0.0.0/24" }.Validate());
        Assert.NotEmpty(new VpnPeer { Name = "Bad endpoint", PublicKey = Key, AllowedIps = "10.0.0.0/24", Endpoint = "host:99999" }.Validate());
    }

    // ---------- Cross-item validation ----------

    [Fact]
    public void Detects_overlapping_subnets_unreachable_next_hop_and_missing_table_routes()
    {
        var g = Gateway();
        g.Interfaces.Add(new NetworkInterface { Name = "eth2", Address = "10.0.0.200/16" }); // overlaps eth1
        g.Routes.Add(new StaticRoute { Destination = "10.50.0.0/16", NextHop = "172.31.0.1" });
        g.Policies.Add(new RoutingPolicy { Priority = 900, Source = "10.0.0.0/24", Table = 50 });

        var issues = ConfigValidator.Validate(g);

        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("overlaps"));
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("172.31.0.1"));
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("table 50"));
    }

    [Fact]
    public void Detects_port_forward_and_vpn_port_conflicts()
    {
        var g = Gateway();
        g.NatRules.Add(Forward("A", "8000-8100", NatProtocol.Tcp));
        g.NatRules.Add(Forward("B", "8080", NatProtocol.Tcp));
        g.NatRules.Add(Forward("C", "8080", NatProtocol.Udp));     // different protocol: fine
        g.NatRules.Add(Forward("D", "51820", NatProtocol.Udp));
        g.Tunnels.Add(new VpnTunnel { Name = "wg", InterfaceName = "wg0", ListenPort = 51820, TunnelAddress = "10.200.0.1/24" });

        var errors = ConfigValidator.Validate(g).Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Message).ToList();

        Assert.Contains(errors, m => m.Contains("'A' and 'B'"));
        Assert.DoesNotContain(errors, m => m.Contains("'C'"));
        Assert.Contains(errors, m => m.Contains("UDP 51820"));
    }

    [Fact]
    public void Detects_ha_virtual_ip_problems()
    {
        var a = Gateway("gw-a", 2, 150);
        var b = Gateway("gw-b", 3, 150); // same priority
        var cluster = new HaCluster { Name = "Edge", VrrpRouterId = 51 };
        cluster.VirtualIps.Add(new VirtualIp { Interface = "eth1", Address = "10.0.0.1/24" });   // fine
        cluster.VirtualIps.Add(new VirtualIp { Interface = "eth1", Address = "10.9.9.9/24" });   // outside subnet
        cluster.VirtualIps.Add(new VirtualIp { Interface = "eth1", Address = "10.0.0.3/24" });   // member b's own address

        var issues = ConfigValidator.Validate(a, cluster, [a, b]);

        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("outside"));
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("member interface address"));
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("share priority"));
    }

    [Fact]
    public void Clean_configuration_has_no_errors()
    {
        var issues = ConfigValidator.Validate(Gateway());
        Assert.DoesNotContain(issues, i => i.Severity == IssueSeverity.Error);
    }

    // ---------- Generation ----------

    [Fact]
    public void Generates_iproute2_nftables_and_policy_routing()
    {
        var g = Gateway();
        g.Routes.Add(new StaticRoute { Destination = "0.0.0.0/0", NextHop = "203.0.113.9", Interface = "eth3", Table = 100 });
        g.Policies.Add(new RoutingPolicy { Priority = 1000, Source = "10.20.0.0/24", Table = 100 });
        g.NatRules.Add(Forward("Web", "443", NatProtocol.Tcp, translatedPort: 8443));
        g.NatRules.Add(new NatRule { Name = "LAN", Type = NatType.Masquerade, OutInterface = "eth0", Source = "10.0.0.0/24" });

        var files = LinuxConfigGenerator.Generate(g).ToDictionary(f => f.Path);
        var network = files["network/apply-network.sh"].Content;
        var nft = files["nftables/sm-nat.nft"].Content;

        Assert.Contains("ip addr replace 10.0.0.2/24 dev eth1", network);
        Assert.Contains("ip route replace default via 198.51.100.1 dev eth0 metric 100", network);
        Assert.Contains("ip route replace default via 203.0.113.9 dev eth3 metric 100 table 100", network);
        Assert.Contains("ip rule add priority 1000 from 10.20.0.0/24 lookup 100", network);
        Assert.Contains("iifname \"eth0\" tcp dport 443 dnat to 10.0.0.10:8443 comment \"Web\"", nft);
        Assert.Contains("oifname \"eth0\" ip saddr 10.0.0.0/24 masquerade comment \"LAN\"", nft);
    }

    [Fact]
    public void WireGuard_config_never_contains_a_private_key()
    {
        var g = Gateway();
        var wg = new VpnTunnel { Name = "branch", InterfaceName = "wg0", ListenPort = 51820, TunnelAddress = "10.200.0.1/24" };
        wg.Peers.Add(new VpnPeer { Name = "Branch", PublicKey = Key, AllowedIps = "10.30.0.0/16", Endpoint = "branch.example.net:51820" });
        g.Tunnels.Add(wg);

        var conf = LinuxConfigGenerator.Generate(g).Single(f => f.Path == "wireguard/wg0.conf").Content;

        Assert.DoesNotContain("PrivateKey", conf);
        Assert.Contains("PostUp = wg set %i private-key /etc/wireguard/%i.key", conf);
        Assert.Contains($"PublicKey = {Key}", conf);
        Assert.Contains("AllowedIPs = 10.30.0.0/16", conf);
    }

    [Fact]
    public void Keepalived_master_backup_and_unicast_peers()
    {
        var a = Gateway("gw-a", 2, 150);
        var b = Gateway("gw-b", 3, 100);
        var cluster = new HaCluster { Name = "Edge", VrrpRouterId = 51 };
        cluster.VirtualIps.Add(new VirtualIp { Interface = "eth1", Address = "10.0.0.1/24" });

        var master = LinuxConfigGenerator.Generate(a, cluster, [a, b]).Single(f => f.Path.StartsWith("keepalived")).Content;
        var backup = LinuxConfigGenerator.Generate(b, cluster, [a, b]).Single(f => f.Path.StartsWith("keepalived")).Content;

        Assert.Contains("state MASTER", master);
        Assert.Contains("priority 150", master);
        Assert.Contains("unicast_src_ip 10.0.0.2", master);
        Assert.Contains("10.0.0.3", master); // peer
        Assert.Contains("state BACKUP", backup);
        Assert.Contains("10.0.0.1/24 dev eth1", backup);
        Assert.Contains("virtual_router_id 51", backup);
    }

    // ---------- Helpers ----------

    private static Gateway Gateway(string host = "edge-1", int octet = 2, int priority = 100)
    {
        var g = new Gateway { Name = host, Hostname = host, HaPriority = priority };
        g.Interfaces.Add(new NetworkInterface { Name = "eth0", Zone = InterfaceZone.Wan, Address = $"198.51.100.{octet}/29" });
        g.Interfaces.Add(new NetworkInterface { Name = "eth1", Zone = InterfaceZone.Lan, Address = $"10.0.0.{octet}/24" });
        g.Interfaces.Add(new NetworkInterface { Name = "eth3", Zone = InterfaceZone.Wan, Address = $"203.0.113.{8 + octet}/28" });
        g.Routes.Add(new StaticRoute { Destination = "0.0.0.0/0", NextHop = "198.51.100.1", Interface = "eth0", Metric = 100 });
        return g;
    }

    private static NatRule Forward(string name, string port, NatProtocol protocol, int? translatedPort = null) => new()
    {
        Name = name, Type = NatType.PortForward, Protocol = protocol, InInterface = "eth0",
        DestinationPort = port, TranslatedAddress = "10.0.0.10", TranslatedPort = translatedPort
    };
}
