using Microsoft.EntityFrameworkCore;
using SecurityManager.Security.Domain.Entities;
using SecurityManager.Security.Domain.Enums;
using SecurityManager.Shared.Kernel.Enums;

namespace SecurityManager.Security.Infrastructure.Persistence;

public sealed class SecurityDbContext(DbContextOptions<SecurityDbContext> options) : DbContext(options)
{
    public DbSet<SecurityEvent> Events => Set<SecurityEvent>();
    public DbSet<DetectionRule> Rules => Set<DetectionRule>();
    public DbSet<Detection> Detections => Set<Detection>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Severity is stored as int so ">=" comparisons translate correctly in SQL.
        builder.Properties<Severity>().HaveConversion<int>();
        builder.Properties<SecurityEventType>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<DetectionStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<CorrelationField>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("security");

        b.Entity<SecurityEvent>(e =>
        {
            e.ToTable("Events");
            e.Property(x => x.Source).IsRequired().HasMaxLength(100);
            e.Property(x => x.Message).IsRequired().HasMaxLength(1000);
            e.Property(x => x.SourceIp).HasMaxLength(45);
            e.Property(x => x.TargetHost).HasMaxLength(255);
            e.Property(x => x.UserName).HasMaxLength(100);
            e.Property(x => x.RawData).HasMaxLength(4000);
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => new { x.EventType, x.Timestamp });
            e.HasIndex(x => x.SourceIp);
        });

        b.Entity<DetectionRule>(e =>
        {
            e.ToTable("DetectionRules");
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.MitreTechnique).HasMaxLength(20);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<Detection>(e =>
        {
            e.ToTable("Detections");
            e.Property(x => x.RuleName).IsRequired().HasMaxLength(120);
            e.Property(x => x.Title).IsRequired().HasMaxLength(300);
            e.Property(x => x.Description).IsRequired().HasMaxLength(1000);
            e.Property(x => x.CorrelationKey).IsRequired().HasMaxLength(255);
            e.Property(x => x.MitreTechnique).HasMaxLength(20);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.AssignedTo).HasMaxLength(100);
            e.Ignore(x => x.IsOpen);
            e.HasIndex(x => new { x.RuleId, x.CorrelationKey, x.Status });
            e.HasIndex(x => x.LastSeen);
        });
    }
}
