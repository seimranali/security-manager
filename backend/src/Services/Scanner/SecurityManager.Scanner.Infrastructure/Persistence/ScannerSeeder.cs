using Microsoft.EntityFrameworkCore;
using SecurityManager.Scanner.Domain.Entities;
using SecurityManager.Scanner.Domain.Enums;

namespace SecurityManager.Scanner.Infrastructure.Persistence;

public static class ScannerSeeder
{
    public static async Task SeedAsync(ScannerDbContext db, CancellationToken ct)
    {
        await UpgradeSchemaAsync(db, ct);
        // No demo data: scan targets and schedules are created by users.
    }

    /// <summary>
    /// EnsureCreated does not alter existing databases, so columns added after the first release are applied here
    /// idempotently. (Switching to EF Core migrations would replace this.)
    /// </summary>
    private static async Task UpgradeSchemaAsync(ScannerDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer()) return;
        await db.Database.ExecuteSqlRawAsync(
            "IF COL_LENGTH('scanner.Schedules', 'RunAtUtc') IS NULL ALTER TABLE [scanner].[Schedules] ADD [RunAtUtc] time NULL;", ct);
    }
}
