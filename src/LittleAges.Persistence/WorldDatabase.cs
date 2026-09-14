using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LittleAges.Persistence;

public sealed record SqlitePragmas(string JournalMode, int ForeignKeys, int BusyTimeoutMilliseconds);

internal sealed record WorldDatabaseOpenOptions(
    DateTime? LegacyUpgradeCheckpointUtc = null,
    LegacyUpgradeFailurePoint? LegacyUpgradeFailurePoint = null,
    M2UpgradeFailurePoint? M2UpgradeFailurePoint = null,
    M3UpgradeFailurePoint? M3UpgradeFailurePoint = null,
    M4UpgradeFailurePoint? M4UpgradeFailurePoint = null);

/// <summary>Opens one world database, applies migrations, and configures connection-level SQLite safety.</summary>
public sealed class WorldDatabase : IAsyncDisposable
{
    private const int BusyTimeoutMilliseconds = 5_000;
    private readonly LittleAgesDbContext _context;

    private WorldDatabase(string databasePath, LittleAgesDbContext context)
    {
        DatabasePath = databasePath;
        _context = context;
    }

    public string DatabasePath { get; }
    internal LittleAgesDbContext Context => _context;

    public static Task<WorldDatabase> OpenAsync(string databasePath, CancellationToken cancellationToken = default) =>
        OpenCoreAsync(databasePath, openOptions: null, cancellationToken);

    internal static Task<WorldDatabase> OpenAsync(string databasePath, WorldDatabaseOpenOptions? openOptions, CancellationToken cancellationToken = default) =>
        OpenCoreAsync(databasePath, openOptions, cancellationToken);

    private static async Task<WorldDatabase> OpenCoreAsync(string databasePath, WorldDatabaseOpenOptions? openOptions, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("A database path is required.", nameof(databasePath));
        }

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 5,
            ForeignKeys = true
        }.ToString();
        var dbOptions = new DbContextOptionsBuilder<LittleAgesDbContext>()
            .UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(typeof(WorldDatabase).Assembly.GetName().Name))
            .Options;
        var context = new LittleAgesDbContext(dbOptions);
        var database = new WorldDatabase(fullPath, context);

        try
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
            await ConfigureConnectionAsync(context.Database.GetDbConnection(), cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);
            await database.VerifyConnectionPragmasAsync(cancellationToken);
            await database.CreateCheckpointStore().UpgradeLegacyM0IfNeededAsync(
                openOptions?.LegacyUpgradeCheckpointUtc,
                openOptions?.LegacyUpgradeFailurePoint,
                cancellationToken);
            var upgradedM1ToM2 = await database.CreateCheckpointStore().UpgradeM1ToM2IfNeededAsync(openOptions?.M2UpgradeFailurePoint, cancellationToken);
            var m3FailurePoint = openOptions?.M3UpgradeFailurePoint ?? (upgradedM1ToM2 ? null : openOptions?.M2UpgradeFailurePoint switch { M2UpgradeFailurePoint.AfterRowsWritten => M3UpgradeFailurePoint.AfterRowsWritten, _ => null });
            await database.CreateCheckpointStore().UpgradeM2ToM3IfNeededAsync(m3FailurePoint, cancellationToken);
            await database.CreateCheckpointStore().UpgradeM3ToM4IfNeededAsync(openOptions?.M4UpgradeFailurePoint, cancellationToken);
            return database;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    public async Task<SqlitePragmas> ReadConnectionPragmasAsync(CancellationToken cancellationToken = default)
    {
        var connection = _context.Database.GetDbConnection();
        var journalMode = Convert.ToString(await ExecuteScalarAsync(connection, "PRAGMA journal_mode;", cancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        var foreignKeys = Convert.ToInt32(await ExecuteScalarAsync(connection, "PRAGMA foreign_keys;", cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        var busyTimeout = Convert.ToInt32(await ExecuteScalarAsync(connection, "PRAGMA busy_timeout;", cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        return new SqlitePragmas(journalMode, foreignKeys, busyTimeout);
    }

    public WorldCheckpointStore CreateCheckpointStore() => new(_context);

    public Task<bool> HasCheckpointAsync(CancellationToken cancellationToken = default) =>
        new WorldCheckpointStore(_context).HasCheckpointAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private async Task VerifyConnectionPragmasAsync(CancellationToken cancellationToken)
    {
        var pragmas = await ReadConnectionPragmasAsync(cancellationToken);
        if (!string.Equals(pragmas.JournalMode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SQLite WAL mode was not enabled; actual mode was '{pragmas.JournalMode}'.");
        }

        if (pragmas.ForeignKeys != 1)
        {
            throw new InvalidOperationException("SQLite foreign-key enforcement was not enabled.");
        }

        if (pragmas.BusyTimeoutMilliseconds != BusyTimeoutMilliseconds)
        {
            throw new InvalidOperationException("SQLite busy timeout was not configured.");
        }
    }

    private static async Task ConfigureConnectionAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken);
        await ExecuteNonQueryAsync(connection, $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds};", cancellationToken);
        var journalMode = Convert.ToString(await ExecuteScalarAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SQLite rejected WAL mode; actual mode was '{journalMode}'.");
        }
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<object?> ExecuteScalarAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}
