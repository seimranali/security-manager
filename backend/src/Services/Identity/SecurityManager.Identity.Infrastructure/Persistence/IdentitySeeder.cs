using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SecurityManager.Identity.Application.Abstractions;
using SecurityManager.Identity.Domain.Entities;
using SecurityManager.Shared.Kernel.Security;

namespace SecurityManager.Identity.Infrastructure.Persistence;

public static class IdentitySeeder
{
    /// <summary>Creates the administrator account on an empty database; other users are added from the Users page. Passwords come from "Seed:*" configuration.</summary>
    public static Func<IdentityDbContext, CancellationToken, Task> Create(IPasswordHasher hasher, IConfiguration config) =>
        async (db, ct) =>
        {
            await UpgradeSchemaAsync(db, ct);
            if (await db.Users.AnyAsync(ct)) return;

            var now = DateTime.UtcNow;
            (string UserName, string FullName, string Role, string Key, string Default)[] accounts =
            [
                ("admin", "System Administrator", Roles.Admin, "Seed:AdminPassword", "Admin@12345")
            ];

            foreach (var a in accounts)
            {
                var user = new User { UserName = a.UserName, Email = $"{a.UserName}@securitymanager.local", FullName = a.FullName, Role = a.Role };
                user.SetPassword(hasher.Hash(user, config[a.Key] ?? a.Default), now);
                db.Users.Add(user);
            }

            await db.SaveChangesAsync(ct);
        };

    /// <summary>
    /// EnsureCreated does not alter existing databases, so columns/tables added after the first release are applied here
    /// idempotently. (Switching to EF Core migrations would replace this.)
    /// </summary>
    private static async Task UpgradeSchemaAsync(IdentityDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer()) return;
        await db.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('identity.Users', 'MfaEnabled') IS NULL
            BEGIN
                ALTER TABLE [identity].[Users] ADD
                    [MfaEnabled] bit NOT NULL CONSTRAINT [DF_Users_MfaEnabled] DEFAULT 0,
                    [MfaSecret] nvarchar(200) NULL,
                    [MfaRecoveryCodes] nvarchar(1000) NULL,
                    [MfaLastStep] bigint NOT NULL CONSTRAINT [DF_Users_MfaLastStep] DEFAULT 0;
            END
            IF OBJECT_ID('identity.LoginAttempts') IS NULL
            BEGIN
                CREATE TABLE [identity].[LoginAttempts] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_LoginAttempts] PRIMARY KEY,
                    [Timestamp] datetime2 NOT NULL,
                    [UserName] nvarchar(100) NOT NULL,
                    [UserId] uniqueidentifier NULL,
                    [Succeeded] bit NOT NULL,
                    [FailureReason] nvarchar(30) NULL,
                    [UsedMfa] bit NOT NULL,
                    [IpAddress] nvarchar(45) NULL,
                    [UserAgent] nvarchar(300) NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [UpdatedAt] datetime2 NULL);
                CREATE INDEX [IX_LoginAttempts_Timestamp] ON [identity].[LoginAttempts] ([Timestamp]);
                CREATE INDEX [IX_LoginAttempts_UserId_Timestamp] ON [identity].[LoginAttempts] ([UserId], [Timestamp]);
            END
            """, ct);
    }
}
