using Microsoft.EntityFrameworkCore;
using SecurityManager.Network.Domain.Entities;
using SecurityManager.Network.Domain.Enums;

namespace SecurityManager.Network.Infrastructure.Persistence;

public sealed class NetworkDbContext(DbContextOptions<NetworkDbContext> options) : DbContext(options)
{
    public DbSet<Gateway> Gateways => Set<Gateway>();
    public DbSet<NetworkInterface> Interfaces => Set<NetworkInterface>();
    public DbSet<StaticRoute> Routes => Set<StaticRoute>();
    public DbSet<RoutingPolicy> Policies => Set<RoutingPolicy>();
    public DbSet<NatRule> NatRules => Set<NatRule>();
    public DbSet<VpnTunnel> Tunnels => Set<VpnTunnel>();
    public DbSet<VpnPeer> Peers => Set<VpnPeer>();
    public DbSet<HaCluster> Clusters => Set<HaCluster>();
    public DbSet<VirtualIp> VirtualIps => Set<VirtualIp>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<InterfaceType>().HaveConversion<string>().HaveMaxLength(16);
        b.Properties<InterfaceZone>().HaveConversion<string>().HaveMaxLength(16);
        b.Properties<AddressingMode>().HaveConversion<string>().HaveMaxLength(16);
        b.Properties<NatType>().HaveConversion<string>().HaveMaxLength(16);
        b.Properties<NatProtocol>().HaveConversion<string>().HaveMaxLength(8);
        b.Properties<VpnType>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("network");

        b.Entity<Gateway>(e =>
        {
            e.ToTable("Gateways");
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.Hostname).IsRequired().HasMaxLength(253);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.ManagementAddress).HasMaxLength(45);
            e.Property(x => x.DeployedBy).HasMaxLength(100);
            e.Ignore(x => x.HasPendingChanges);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.Hostname).IsUnique();
            e.HasOne(x => x.Cluster).WithMany(c => c.Members).HasForeignKey(x => x.ClusterId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Interfaces).WithOne().HasForeignKey(x => x.GatewayId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Routes).WithOne().HasForeignKey(x => x.GatewayId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Policies).WithOne().HasForeignKey(x => x.GatewayId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.NatRules).WithOne().HasForeignKey(x => x.GatewayId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Tunnels).WithOne().HasForeignKey(x => x.GatewayId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<NetworkInterface>(e =>
        {
            e.ToTable("Interfaces");
            e.Property(x => x.Name).IsRequired().HasMaxLength(15);
            e.Property(x => x.Address).HasMaxLength(50);
            e.Property(x => x.Parent).HasMaxLength(15);
            e.Property(x => x.Members).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Ignore(x => x.AddressCidr);
            e.HasIndex(x => new { x.GatewayId, x.Name }).IsUnique();
        });

        b.Entity<StaticRoute>(e =>
        {
            e.ToTable("Routes");
            e.Property(x => x.Destination).IsRequired().HasMaxLength(50);
            e.Property(x => x.NextHop).HasMaxLength(45);
            e.Property(x => x.Interface).HasMaxLength(15);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Ignore(x => x.IsDefault);
        });

        b.Entity<RoutingPolicy>(e =>
        {
            e.ToTable("RoutingPolicies");
            e.Property(x => x.Source).HasMaxLength(50);
            e.Property(x => x.Destination).HasMaxLength(50);
            e.Property(x => x.InInterface).HasMaxLength(15);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => new { x.GatewayId, x.Priority }).IsUnique();
        });

        b.Entity<NatRule>(e =>
        {
            e.ToTable("NatRules");
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.InInterface).HasMaxLength(15);
            e.Property(x => x.OutInterface).HasMaxLength(15);
            e.Property(x => x.Source).HasMaxLength(50);
            e.Property(x => x.Destination).HasMaxLength(50);
            e.Property(x => x.DestinationPort).HasMaxLength(20);
            e.Property(x => x.TranslatedAddress).HasMaxLength(45);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Ignore(x => x.UsesPorts);
        });

        b.Entity<VpnTunnel>(e =>
        {
            e.ToTable("VpnTunnels");
            e.Property(x => x.Name).IsRequired().HasMaxLength(40);
            e.Property(x => x.InterfaceName).HasMaxLength(15);
            e.Property(x => x.TunnelAddress).HasMaxLength(50);
            e.Property(x => x.PublicKey).HasMaxLength(44);
            e.Property(x => x.RemoteGateway).HasMaxLength(253);
            e.Property(x => x.LocalSubnets).HasMaxLength(500);
            e.Property(x => x.RemoteSubnets).HasMaxLength(500);
            e.Property(x => x.Proposals).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => new { x.GatewayId, x.Name }).IsUnique();
            e.HasMany(x => x.Peers).WithOne().HasForeignKey(x => x.TunnelId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<VpnPeer>(e =>
        {
            e.ToTable("VpnPeers");
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.PublicKey).IsRequired().HasMaxLength(44);
            e.Property(x => x.Endpoint).HasMaxLength(300);
            e.Property(x => x.AllowedIps).IsRequired().HasMaxLength(1000);
            e.HasIndex(x => new { x.TunnelId, x.PublicKey }).IsUnique();
        });

        b.Entity<HaCluster>(e =>
        {
            e.ToTable("HaClusters");
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasMany(x => x.VirtualIps).WithOne().HasForeignKey(x => x.ClusterId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<VirtualIp>(e =>
        {
            e.ToTable("VirtualIps");
            e.Property(x => x.Interface).IsRequired().HasMaxLength(15);
            e.Property(x => x.Address).IsRequired().HasMaxLength(50);
            e.Property(x => x.Description).HasMaxLength(500);
        });
    }
}
