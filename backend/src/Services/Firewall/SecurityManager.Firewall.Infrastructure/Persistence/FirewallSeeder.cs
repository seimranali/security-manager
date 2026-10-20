using Microsoft.EntityFrameworkCore;
using SecurityManager.Firewall.Domain.Entities;
using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Firewall.Domain.Services;
using SecurityManager.Firewall.Domain.ValueObjects;

namespace SecurityManager.Firewall.Infrastructure.Persistence;

public static class FirewallSeeder
{
    public static async Task SeedAsync(FirewallDbContext db, CancellationToken ct)
    {
        await UpgradeSchemaAsync(db, ct);
        // No demo data: policies, rules, IP lists and traffic are created by users.
    }

    /// <summary>
    /// EnsureCreated does not alter existing databases, so tables added after the first release are created here
    /// idempotently. (Switching to EF Core migrations would replace this.)
    /// </summary>
    private static async Task UpgradeSchemaAsync(FirewallDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer()) return;
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'[firewall].[IpListEntries]', N'U') IS NULL
            BEGIN
                CREATE TABLE [firewall].[IpListEntries] (
                    [Id] uniqueidentifier NOT NULL,
                    [ListType] nvarchar(16) NOT NULL,
                    [Address] nvarchar(100) NOT NULL,
                    [Description] nvarchar(500) NULL,
                    [Source] nvarchar(16) NOT NULL,
                    [SourceReference] nvarchar(100) NULL,
                    [ExpiresAt] datetime2 NULL,
                    [IsEnabled] bit NOT NULL,
                    [HitCount] bigint NOT NULL,
                    [LastHitAt] datetime2 NULL,
                    [CreatedBy] nvarchar(100) NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [UpdatedAt] datetime2 NULL,
                    CONSTRAINT [PK_IpListEntries] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_IpListEntries_ListType_Address] ON [firewall].[IpListEntries] ([ListType], [Address]);
                CREATE INDEX [IX_IpListEntries_IsEnabled_ExpiresAt] ON [firewall].[IpListEntries] ([IsEnabled], [ExpiresAt]);
            END

            IF OBJECT_ID(N'[firewall].[ThreatFeeds]', N'U') IS NULL
            BEGIN
                CREATE TABLE [firewall].[ThreatFeeds] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_ThreatFeeds] PRIMARY KEY,
                    [Name] nvarchar(100) NOT NULL,
                    [Url] nvarchar(1000) NOT NULL,
                    [Kind] nvarchar(16) NOT NULL,
                    [CountryCode] nvarchar(2) NULL,
                    [ListType] nvarchar(16) NOT NULL,
                    [IsEnabled] bit NOT NULL,
                    [RefreshMinutes] int NOT NULL,
                    [MaxEntries] int NOT NULL,
                    [LastAttemptAt] datetime2 NULL,
                    [LastSuccessAt] datetime2 NULL,
                    [LastError] nvarchar(1000) NULL,
                    [EntryCount] int NOT NULL,
                    [SkippedCount] int NOT NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [UpdatedAt] datetime2 NULL);
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IpListEntries_SourceReference')
                CREATE INDEX [IX_IpListEntries_SourceReference] ON [firewall].[IpListEntries] ([SourceReference]);

            IF COL_LENGTH('firewall.Policies', 'EnforceOnHosts') IS NULL
                ALTER TABLE [firewall].[Policies] ADD [EnforceOnHosts] bit NOT NULL CONSTRAINT [DF_Policies_EnforceOnHosts] DEFAULT (0);

            IF OBJECT_ID(N'[firewall].[EnforcementSettings]', N'U') IS NULL
                CREATE TABLE [firewall].[EnforcementSettings] (
                    [Id] uniqueidentifier NOT NULL,
                    [Enabled] bit NOT NULL,
                    [EnforceBlocklist] bit NOT NULL,
                    [EnforceAllowlist] bit NOT NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [UpdatedAt] datetime2 NULL,
                    CONSTRAINT [PK_EnforcementSettings] PRIMARY KEY ([Id])
                );

            IF OBJECT_ID(N'[firewall].[EnforcementAgents]', N'U') IS NULL
            BEGIN
                CREATE TABLE [firewall].[EnforcementAgents] (
                    [Id] uniqueidentifier NOT NULL,
                    [AgentId] nvarchar(100) NOT NULL,
                    [Hostname] nvarchar(253) NOT NULL,
                    [OperatingSystem] nvarchar(200) NOT NULL,
                    [AgentVersion] nvarchar(50) NOT NULL,
                    [Mode] nvarchar(20) NOT NULL,
                    [Status] nvarchar(20) NOT NULL,
                    [AppliedVersion] nvarchar(64) NULL,
                    [ManagedRuleCount] int NOT NULL,
                    [LastCreated] int NOT NULL,
                    [LastUpdated] int NOT NULL,
                    [LastRemoved] int NOT NULL,
                    [IntervalSeconds] int NOT NULL,
                    [LastSeenAt] datetime2 NOT NULL,
                    [LastChangeAt] datetime2 NULL,
                    [ProfilesJson] nvarchar(max) NOT NULL,
                    [ErrorsJson] nvarchar(max) NOT NULL,
                    [WarningsJson] nvarchar(max) NOT NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [UpdatedAt] datetime2 NULL,
                    CONSTRAINT [PK_EnforcementAgents] PRIMARY KEY ([Id])
                );
                CREATE UNIQUE INDEX [IX_EnforcementAgents_AgentId] ON [firewall].[EnforcementAgents] ([AgentId]);
            END
            """, ct);
    }
}
