using Microsoft.EntityFrameworkCore;

namespace SecurityManager.Alert.Infrastructure.Persistence;

/// <summary>
/// EnsureCreated does not alter existing databases, so tables added after the first release are created here
/// idempotently. (Switching to EF Core migrations would replace this.)
/// </summary>
public static class AlertSchema
{
    public static async Task UpgradeAsync(AlertDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer()) return;
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID('alert.NotificationChannels') IS NULL
            CREATE TABLE [alert].[NotificationChannels] (
                [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_NotificationChannels] PRIMARY KEY,
                [Name] nvarchar(100) NOT NULL,
                [Type] nvarchar(16) NOT NULL,
                [IsEnabled] bit NOT NULL,
                [MinSeverity] int NOT NULL,
                [NotifyOnEscalation] bit NOT NULL,
                [SmtpHost] nvarchar(255) NULL,
                [SmtpPort] int NOT NULL,
                [SmtpUseTls] bit NOT NULL,
                [SmtpUserName] nvarchar(200) NULL,
                [SmtpPassword] nvarchar(1000) NULL,
                [FromAddress] nvarchar(200) NULL,
                [Recipients] nvarchar(1000) NULL,
                [WebhookUrl] nvarchar(1000) NULL,
                [WebhookFormat] nvarchar(16) NOT NULL,
                [WebhookSecret] nvarchar(1000) NULL,
                [LastSentAt] datetime2 NULL,
                [LastFailureAt] datetime2 NULL,
                [LastError] nvarchar(1000) NULL,
                [SentCount] int NOT NULL,
                [CreatedAt] datetime2 NOT NULL,
                [UpdatedAt] datetime2 NULL);
            """, ct);
    }
}
