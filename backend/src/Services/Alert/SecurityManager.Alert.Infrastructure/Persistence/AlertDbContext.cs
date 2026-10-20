using Microsoft.EntityFrameworkCore;
using SecurityManager.Alert.Domain.Entities;
using SecurityManager.Alert.Domain.Enums;
using SecurityManager.Shared.Kernel.Enums;

namespace SecurityManager.Alert.Infrastructure.Persistence;

public sealed class AlertDbContext(DbContextOptions<AlertDbContext> options) : DbContext(options)
{
    public DbSet<SecurityAlert> Alerts => Set<SecurityAlert>();
    public DbSet<NotificationChannel> Channels => Set<NotificationChannel>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<Severity>().HaveConversion<int>();
        builder.Properties<AlertStatus>().HaveConversion<string>().HaveMaxLength(16);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("alert");

        b.Entity<SecurityAlert>(e =>
        {
            e.ToTable("Alerts");
            e.Property(x => x.Title).IsRequired().HasMaxLength(300);
            e.Property(x => x.Message).IsRequired().HasMaxLength(2000);
            e.Property(x => x.Source).IsRequired().HasMaxLength(50);
            e.Property(x => x.SourceReference).HasMaxLength(100);
            e.Property(x => x.Category).HasMaxLength(100);
            e.Property(x => x.AcknowledgedBy).HasMaxLength(100);
            e.Property(x => x.ResolvedBy).HasMaxLength(100);
            e.Property(x => x.ResolutionNotes).HasMaxLength(2000);
            e.Ignore(x => x.IsActive);
            e.HasIndex(x => new { x.Status, x.Severity });
            e.HasIndex(x => new { x.Source, x.SourceReference });
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<NotificationChannel>(e =>
        {
            e.ToTable("NotificationChannels");
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.WebhookFormat).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.SmtpHost).HasMaxLength(255);
            e.Property(x => x.SmtpUserName).HasMaxLength(200);
            e.Property(x => x.SmtpPassword).HasMaxLength(1000);
            e.Property(x => x.FromAddress).HasMaxLength(200);
            e.Property(x => x.Recipients).HasMaxLength(1000);
            e.Property(x => x.WebhookUrl).HasMaxLength(1000);
            e.Property(x => x.WebhookSecret).HasMaxLength(1000);
            e.Property(x => x.LastError).HasMaxLength(1000);
        });
    }
}
