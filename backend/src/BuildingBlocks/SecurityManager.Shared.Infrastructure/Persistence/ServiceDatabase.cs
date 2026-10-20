using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SecurityManager.Shared.Infrastructure.Persistence;

/// <summary>Where a service keeps its data. Registered per DbContext so the initializer knows what to create or import.</summary>
public sealed record ServiceDatabaseInfo<TContext>(string Provider, string? SqliteFile, string? SqlServerDatabase) where TContext : DbContext;

public static class ServiceDatabase
{
    public const string Sqlite = "Sqlite";
    public const string SqlServer = "SqlServer";

    /// <summary>
    /// Registers the service's DbContext. Configuration:
    /// <code>
    /// "Database": {
    ///   "Provider": "Sqlite",                 // default; or "SqlServer" (uses ConnectionStrings:{name})
    ///   "DataDirectory": "data",              // SQLite files: {DataDirectory}/{fileName}.db (relative = next to the exe)
    ///   "ImportFromSqlServer": "Server=...;"  // optional one-time copy from the old SQL Server databases into empty SQLite files
    /// }
    /// </code>
    /// </summary>
    public static IServiceCollection AddServiceDbContext<TContext>(this IServiceCollection services, IConfiguration config,
        string connectionName, string fileName, string sqlServerDatabase) where TContext : DbContext
    {
        var provider = config["Database:Provider"] ?? Sqlite;
        if (string.Equals(provider, SqlServer, StringComparison.OrdinalIgnoreCase))
        {
            var connection = config.GetConnectionString(connectionName)
                             ?? throw new InvalidOperationException($"ConnectionStrings:{connectionName} is required when Database:Provider is SqlServer.");
            services.AddDbContext<TContext>(o => o.UseSqlServer(connection));
            services.AddSingleton(new ServiceDatabaseInfo<TContext>(SqlServer, null, sqlServerDatabase));
            return services;
        }

        var file = Path.Combine(DataDirectory(config), fileName + ".db");
        // Default Timeout = busy timeout (seconds): background workers and requests share the file.
        var sqlite = $"Data Source={file};Default Timeout=30;Pooling=True";
        services.AddDbContext<TContext>(o => o.UseSqlite(sqlite));
        services.AddSingleton(new ServiceDatabaseInfo<TContext>(Sqlite, file, sqlServerDatabase));
        return services;
    }

    public static string DataDirectory(IConfiguration config)
    {
        var dir = config["Database:DataDirectory"] ?? "data";
        return Path.GetFullPath(Path.IsPathRooted(dir) ? dir : Path.Combine(AppContext.BaseDirectory, dir));
    }
}
