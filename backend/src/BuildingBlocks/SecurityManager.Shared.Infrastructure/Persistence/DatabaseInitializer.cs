using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SecurityManager.Shared.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>
    /// Creates the service database (with retries while the database server starts up), optionally imports existing
    /// data from SQL Server into a new SQLite file, then runs the seeder.
    /// </summary>
    public static async Task InitializeDatabaseAsync<TContext>(
        this IServiceProvider services,
        Func<TContext, CancellationToken, Task>? seed = null,
        CancellationToken ct = default) where TContext : DbContext
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        var info = scope.ServiceProvider.GetService<ServiceDatabaseInfo<TContext>>();

        if (info?.SqliteFile is { } file) Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await context.Database.EnsureCreatedAsync(ct);
                if (context.Database.IsSqlite())
                {
                    // WAL lets readers and a writer work at the same time (persists in the file).
                    await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
                    await ImportFromSqlServerIfRequestedAsync(scope.ServiceProvider, context, info, logger, ct);
                }
                if (seed is not null) await seed(context, ct);
                logger.LogInformation("Database for {Context} is ready ({Provider})", typeof(TContext).Name, context.Database.ProviderName);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex, "Database not ready for {Context} (attempt {Attempt}/{Max}); retrying",
                    typeof(TContext).Name, attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }

    /// <summary>
    /// One-time migration: when "Database:ImportFromSqlServer" is set and the SQLite database is still empty, copies every
    /// table from the service's old SQL Server database. Re-running is harmless (a non-empty database is never touched).
    /// </summary>
    private static async Task ImportFromSqlServerIfRequestedAsync<TContext>(IServiceProvider sp, TContext target,
        ServiceDatabaseInfo<TContext>? info, ILogger logger, CancellationToken ct) where TContext : DbContext
    {
        var server = sp.GetRequiredService<IConfiguration>()["Database:ImportFromSqlServer"];
        if (string.IsNullOrWhiteSpace(server) || info?.SqlServerDatabase is null) return;

        var entityTypes = target.Model.GetEntityTypes().Where(e => !e.IsOwned() && e.ClrType != typeof(Dictionary<string, object>)).ToList();
        foreach (var type in entityTypes)
        {
            if (await CountAsync(target, type.ClrType, ct) > 0)
            {
                logger.LogInformation("SQLite database for {Context} already has data; skipping the SQL Server import", typeof(TContext).Name);
                return;
            }
        }

        var connection = $"{server.TrimEnd(';')};Database={info.SqlServerDatabase}";
        var options = new DbContextOptionsBuilder<TContext>().UseSqlServer(connection).Options;
        await using var source = (TContext)Activator.CreateInstance(typeof(TContext), options)!;
        if (!await source.Database.CanConnectAsync(ct))
        {
            logger.LogWarning("Cannot reach SQL Server database {Database} for import; starting with an empty SQLite database", info.SqlServerDatabase);
            return;
        }

        // Copy with foreign keys off so table order does not matter, all in one transaction.
        await target.Database.OpenConnectionAsync(ct);
        try
        {
            await target.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF;", ct);
            await using var tx = await target.Database.BeginTransactionAsync(ct);
            var total = 0;
            foreach (var type in entityTypes)
            {
                var copied = await CopyAsync(source, target, type.ClrType, ct);
                if (copied > 0) logger.LogInformation("Imported {Count} {Table} row(s) from SQL Server", copied, type.GetTableName());
                total += copied;
            }
            await tx.CommitAsync(ct);
            logger.LogWarning("Imported {Total} row(s) for {Context} from SQL Server database {Database}", total, typeof(TContext).Name, info.SqlServerDatabase);
        }
        finally
        {
            await target.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", ct);
            await target.Database.CloseConnectionAsync();
            target.ChangeTracker.Clear();
        }
    }

    private static readonly MethodInfo CountMethod = typeof(DatabaseInitializer).GetMethod(nameof(CountGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo CopyMethod = typeof(DatabaseInitializer).GetMethod(nameof(CopyGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static Task<int> CountAsync(DbContext db, Type type, CancellationToken ct) =>
        (Task<int>)CountMethod.MakeGenericMethod(type).Invoke(null, [db, ct])!;

    private static Task<int> CopyAsync(DbContext source, DbContext target, Type type, CancellationToken ct) =>
        (Task<int>)CopyMethod.MakeGenericMethod(type).Invoke(null, [source, target, ct])!;

    private static Task<int> CountGeneric<T>(DbContext db, CancellationToken ct) where T : class => db.Set<T>().CountAsync(ct);

    private static async Task<int> CopyGeneric<T>(DbContext source, DbContext target, CancellationToken ct) where T : class
    {
        var rows = await source.Set<T>().AsNoTracking().ToListAsync(ct);
        if (rows.Count == 0) return 0;
        // Batches keep memory and statement size reasonable for large tables (traffic logs, events).
        foreach (var batch in rows.Chunk(2000))
        {
            target.Set<T>().AddRange(batch);
            await target.SaveChangesAsync(ct);
            target.ChangeTracker.Clear();
        }
        return rows.Count;
    }
}
