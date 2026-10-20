using Microsoft.EntityFrameworkCore;
using SecurityManager.Scanner.Domain.Entities;
using SecurityManager.Scanner.Domain.Enums;
using SecurityManager.Shared.Kernel.Enums;

namespace SecurityManager.Scanner.Infrastructure.Persistence;

public sealed class ScannerDbContext(DbContextOptions<ScannerDbContext> options) : DbContext(options)
{
    public DbSet<ScanTarget> Targets => Set<ScanTarget>();
    public DbSet<ScanSchedule> Schedules => Set<ScanSchedule>();
    public DbSet<ScanJob> Jobs => Set<ScanJob>();
    public DbSet<ScanFinding> Findings => Set<ScanFinding>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<Severity>().HaveConversion<int>();
        builder.Properties<ScanType>().HaveConversion<int>();
        builder.Properties<ScanStatus>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<ScanTrigger>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<FindingStatus>().HaveConversion<string>().HaveMaxLength(16);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("scanner");

        b.Entity<ScanTarget>(e =>
        {
            e.ToTable("Targets");
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.Host).IsRequired().HasMaxLength(253);
            e.Property(x => x.Ports).IsRequired().HasMaxLength(1000);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasMany(x => x.Schedules).WithOne(x => x.Target).HasForeignKey(x => x.TargetId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ScanSchedule>(e =>
        {
            e.ToTable("Schedules");
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.HasIndex(x => new { x.IsEnabled, x.NextRunAt });
        });

        b.Entity<ScanJob>(e =>
        {
            e.ToTable("Jobs");
            e.Property(x => x.TargetName).IsRequired().HasMaxLength(100);
            e.Property(x => x.Host).IsRequired().HasMaxLength(253);
            e.Property(x => x.OpenPorts).HasMaxLength(4000);
            e.Property(x => x.Error).HasMaxLength(1000);
            e.Ignore(x => x.IsActive);
            e.HasOne<ScanTarget>().WithMany().HasForeignKey(x => x.TargetId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Findings).WithOne(x => x.Job).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TargetId, x.Status });
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<ScanFinding>(e =>
        {
            e.ToTable("Findings");
            e.Property(x => x.TargetName).IsRequired().HasMaxLength(100);
            e.Property(x => x.Title).IsRequired().HasMaxLength(200);
            e.Property(x => x.Description).IsRequired().HasMaxLength(1000);
            e.Property(x => x.Category).IsRequired().HasMaxLength(30);
            e.Property(x => x.Evidence).HasMaxLength(1000);
            e.Property(x => x.Recommendation).HasMaxLength(500);
            e.HasIndex(x => new { x.Status, x.Severity });
            e.HasIndex(x => x.TargetId);
        });
    }
}
