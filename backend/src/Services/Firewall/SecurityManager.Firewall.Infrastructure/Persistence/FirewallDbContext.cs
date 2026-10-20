using Microsoft.EntityFrameworkCore;
using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Firewall.Domain.Enums;

namespace SecurityManager.Firewall.Infrastructure.Persistence;

public sealed class FirewallDbContext(DbContextOptions<FirewallDbContext> options) : DbContext(options)
{
    public DbSet<FirewallPolicy> Policies => Set<FirewallPolicy>();
    public DbSet<FirewallRule> Rules => Set<FirewallRule>();
    public DbSet<TrafficLog> TrafficLogs => Set<TrafficLog>();
    public DbSet<IpListEntry> IpList => Set<IpListEntry>();
    public DbSet<EnforcementSettings> EnforcementSettings => Set<EnforcementSettings>();
    public DbSet<EnforcementAgent> EnforcementAgents => Set<EnforcementAgent>();
    public DbSet<ThreatFeed> ThreatFeeds => Set<ThreatFeed>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<RuleAction>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<NetworkProtocol>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<TrafficDirection>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<IpListType>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<IpListSource>().HaveConversion<string>().HaveMaxLength(16);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("firewall");

        b.Entity<FirewallPolicy>(e =>
        {
            e.ToTable("Policies");
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.Priority);
            e.HasMany(x => x.Rules)
             .WithOne(x => x.Policy)
             .HasForeignKey(x => x.PolicyId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FirewallRule>(e =>
        {
            e.ToTable("Rules");
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.SourceAddress).IsRequired().HasMaxLength(500);
            e.Property(x => x.DestinationAddress).IsRequired().HasMaxLength(500);
            e.Property(x => x.SourcePort).IsRequired().HasMaxLength(200);
            e.Property(x => x.DestinationPort).IsRequired().HasMaxLength(200);
            e.HasIndex(x => new { x.PolicyId, x.Priority });
        });

        b.Entity<TrafficLog>(e =>
        {
            e.ToTable("TrafficLogs");
            e.Property(x => x.SourceIp).IsRequired().HasMaxLength(45);
            e.Property(x => x.DestinationIp).IsRequired().HasMaxLength(45);
            e.Property(x => x.MatchedRuleName).HasMaxLength(100);
            e.Property(x => x.PolicyName).HasMaxLength(100);
            e.Property(x => x.Reason).HasMaxLength(300);
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => new { x.Action, x.Timestamp });
        });

        // Keep in sync with FirewallSeeder.UpgradeSchemaAsync (used for databases created before this table existed).
        b.Entity<IpListEntry>(e =>
        {
            e.ToTable("IpListEntries");
            e.Property(x => x.Address).IsRequired().HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.SourceReference).HasMaxLength(100);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.ListType, x.Address }).IsUnique();
            e.HasIndex(x => new { x.IsEnabled, x.ExpiresAt });
            e.HasIndex(x => x.SourceReference);
        });

        // Keep the following in sync with FirewallSeeder.UpgradeSchemaAsync.
        b.Entity<EnforcementSettings>(e => e.ToTable("EnforcementSettings"));

        b.Entity<ThreatFeed>(e =>
        {
            e.ToTable("ThreatFeeds");
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.Url).IsRequired().HasMaxLength(1000);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.CountryCode).HasMaxLength(2);
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.Ignore(x => x.Reference);
        });

        b.Entity<EnforcementAgent>(e =>
        {
            e.ToTable("EnforcementAgents");
            e.Property(x => x.AgentId).IsRequired().HasMaxLength(100);
            e.Property(x => x.Hostname).IsRequired().HasMaxLength(253);
            e.Property(x => x.OperatingSystem).HasMaxLength(200);
            e.Property(x => x.AgentVersion).HasMaxLength(50);
            e.Property(x => x.Mode).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.AppliedVersion).HasMaxLength(64);
            e.HasIndex(x => x.AgentId).IsUnique();
        });
    }
}
