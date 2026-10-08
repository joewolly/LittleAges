using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using LittleAges.Simulation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LittleAges.Persistence;

/// <summary>A verified SQLite backup made before opening the world for schema migration.</summary>
public sealed record WorldRulesBackup
{
    private WorldRulesBackup(string path, string sha256, string sourceRules, long worldMinute, string worldFingerprint, string checkpointFingerprint)
    {
        Path = path; Sha256 = sha256; SourceRules = sourceRules; WorldMinute = worldMinute;
        WorldFingerprint = worldFingerprint; CheckpointFingerprint = checkpointFingerprint;
    }

    public string Path { get; }
    public string Sha256 { get; }
    public string SourceRules { get; }
    public long WorldMinute { get; }
    public string WorldFingerprint { get; }
    public string CheckpointFingerprint { get; }

    public static Task<WorldRulesBackup?> PrepareAsync(string databasePath, CancellationToken cancellationToken = default) =>
        PrepareAsync(databasePath, true, cancellationToken);

    public static async Task<WorldRulesBackup?> PrepareAsync(string databasePath, bool rulesUpgradeRequested,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath)) return null;
        await using var source = ReadOnly(databasePath);
        await source.OpenAsync(cancellationToken);
        var metadata = await ReadMetadataAsync(source, cancellationToken);
        if (metadata is null || !WorldRulesUpgrades.SupportedRules.Contains(metadata.Value.Rules, StringComparer.Ordinal)) return null;
        if (!rulesUpgradeRequested || !WorldRulesUpgrades.CanUpgrade(metadata.Value.Rules))
        {
            await using var schemaContext = new LittleAgesDbContext(new DbContextOptionsBuilder<LittleAgesDbContext>().UseSqlite(source).Options);
            if (!(await schemaContext.Database.GetPendingMigrationsAsync(cancellationToken)).Any()) return null;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var directory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(databasePath))!,
            "rules-upgrade-backups", System.IO.Path.GetFileNameWithoutExtension(databasePath) + "-" +
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "world.db");
        await using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                     { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
        {
            await destination.OpenAsync(cancellationToken);
            // SQLite's backup API includes committed WAL pages; a raw file copy does not.
            source.BackupDatabase(destination);
        }
        cancellationToken.ThrowIfCancellationRequested();
        await using var check = ReadOnly(path);
        await check.OpenAsync(cancellationToken);
        await using (var integrity = check.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            if (!string.Equals(Convert.ToString(await integrity.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture), "ok", StringComparison.Ordinal))
                throw new InvalidDataException($"Rules upgrade backup failed integrity verification: {path}");
        }
        await using (var foreignKeys = check.CreateCommand())
        {
            foreignKeys.CommandText = "PRAGMA foreign_key_check;";
            await using var reader = await foreignKeys.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken)) throw new InvalidDataException("Rules upgrade backup has invalid foreign keys.");
        }
        var backedUp = await ReadMetadataAsync(check, cancellationToken)
            ?? throw new InvalidDataException("Rules upgrade backup has no checkpoint.");
        if (backedUp != metadata.Value) throw new InvalidDataException("The source changed while preparing its rules upgrade backup.");
        // Read without migrations or writer pragmas so verification never changes the retained file.
        await using var context = new LittleAgesDbContext(new DbContextOptionsBuilder<LittleAgesDbContext>().UseSqlite(check).Options);
        var checkpoint = await new WorldCheckpointStore(context).LoadAsync(cancellationToken);
        var checkpointFingerprint = WorldRulesUpgrades.Fingerprint(checkpoint);
        await check.CloseAsync();
        var backup = new WorldRulesBackup(path, await HashAsync(path, cancellationToken), backedUp.Rules, backedUp.Minute, backedUp.Fingerprint, checkpointFingerprint);
        await File.WriteAllTextAsync(System.IO.Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(new { version = 1, backup }), cancellationToken);
        return backup;
    }

    internal async Task VerifyAsync(SimulationPersistenceSnapshot source, CancellationToken cancellationToken)
    {
        if (source.SimulationRulesVersion != SourceRules || source.WorldMinute.Value != WorldMinute || source.World!.Fingerprint != WorldFingerprint ||
            WorldRulesUpgrades.Fingerprint(source) != CheckpointFingerprint ||
            !string.Equals(await HashAsync(Path, cancellationToken), Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("The verified backup does not match the rules upgrade source.");
    }

    private static SqliteConnection ReadOnly(string path) => new(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static async Task<(string Rules, long Minute, string Fingerprint)?> ReadMetadataAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var tables = connection.CreateCommand();
        tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='world_meta';";
        if (Convert.ToInt64(await tables.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 0) return null;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT simulation_rules_version, world_minute, world_fingerprint FROM world_meta WHERE id = 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return (reader.GetString(0), reader.GetInt64(1), reader.GetString(2));
    }
}
