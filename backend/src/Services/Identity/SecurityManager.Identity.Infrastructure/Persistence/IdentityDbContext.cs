using Microsoft.EntityFrameworkCore;
using SecurityManager.Identity.Domain.Entities;

namespace SecurityManager.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("identity");

        b.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.Property(x => x.UserName).IsRequired().HasMaxLength(50);
            e.Property(x => x.Email).IsRequired().HasMaxLength(200);
            e.Property(x => x.FullName).IsRequired().HasMaxLength(100);
            e.Property(x => x.PasswordHash).IsRequired().HasMaxLength(500);
            e.Property(x => x.Role).IsRequired().HasMaxLength(20);
            e.Property(x => x.MfaSecret).HasMaxLength(200);
            e.Property(x => x.MfaRecoveryCodes).HasMaxLength(1000);
            e.HasIndex(x => x.UserName).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.HasMany(x => x.RefreshTokens).WithOne(x => x.User).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<LoginAttempt>(e =>
        {
            e.ToTable("LoginAttempts");
            e.Property(x => x.UserName).IsRequired().HasMaxLength(100);
            e.Property(x => x.FailureReason).HasMaxLength(30);
            e.Property(x => x.IpAddress).HasMaxLength(45);
            e.Property(x => x.UserAgent).HasMaxLength(300);
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => new { x.UserId, x.Timestamp });
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("RefreshTokens");
            e.Property(x => x.TokenHash).IsRequired().HasMaxLength(64);
            e.Property(x => x.CreatedByIp).HasMaxLength(45);
            e.Property(x => x.UserAgent).HasMaxLength(300);
            e.Property(x => x.RevokedReason).HasMaxLength(30);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.FamilyId);
            e.HasIndex(x => new { x.UserId, x.RevokedAt });
            e.HasIndex(x => x.ExpiresAt);
        });
    }
}
