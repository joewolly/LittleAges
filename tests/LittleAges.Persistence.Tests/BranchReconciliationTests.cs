using LittleAges.Domain;
using LittleAges.Simulation;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class BranchReconciliationTests
{
    [Fact]
    public async Task LivingDatabaseWithHistoricalColumnOrderPreservesActionsAndContinuation()
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-Reconciliation", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "world.db");
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.LivingSimulationRulesVersion);
            do { engine.AdvanceUntil(engine.CurrentMinute.Add(30)); }
            while (!engine.Citizens.Any(c => c.CurrentAction == CitizenAction.LivingWork) && engine.CurrentMinute.Value < 14400);
            Assert.Contains(engine.Citizens, c => c.CurrentAction == CitizenAction.LivingWork);
            await using (var db = await WorldDatabase.OpenAsync(path))
                await db.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot());

            // Reproduce the already-shipped Living branch: its EF table rebuild
            // ordered citizen columns alphabetically and had no M10/M11 migrations.
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                var columns = new List<(string Name, string Type, bool Required)>();
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA table_info(citizens)";
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync()) columns.Add((reader.GetString(1), reader.GetString(2), reader.GetInt32(3) != 0));
                }
                columns = columns.OrderBy(c => c.Name == "id" ? 0 : 1).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
                var names = string.Join(",", columns.Select(c => $"\"{c.Name}\""));
                var definitions = string.Join(",", columns.Select(c => $"\"{c.Name}\" {c.Type} {(c.Required ? "NOT NULL" : "NULL")}{(c.Name == "id" ? " PRIMARY KEY" : "")}"));
                await Execute(connection, $"""
                    PRAGMA foreign_keys=OFF;
                    CREATE TABLE citizens_living ({definitions}, CONSTRAINT CK_citizens_action CHECK (current_action BETWEEN 0 AND 13));
                    INSERT INTO citizens_living ({names}) SELECT {names} FROM citizens;
                    DROP TABLE citizens;
                    ALTER TABLE citizens_living RENAME TO citizens;
                    CREATE UNIQUE INDEX IX_citizens_founder_ordinal ON citizens(founder_ordinal);
                    DROP TABLE agriculture_state;
                    DROP TABLE economy_state;
                    DELETE FROM __EFMigrationsHistory WHERE MigrationId IN ('20260920000000_M10Agriculture','20260920010000_M11Economy','20260923010000_M13UnifiedCitizenAction');
                    PRAGMA foreign_keys=ON;
                    """);
                Assert.True(await Scalar(connection, "SELECT count(*) FROM citizens WHERE current_action=13") > 0);
            }

            SimulationEngine reopened;
            await using (var db = await WorldDatabase.OpenAsync(path))
                reopened = new SimulationEngine(await db.CreateCheckpointStore().LoadAsync());
            Assert.Equal(engine.ComputeSocialFingerprint(), reopened.ComputeSocialFingerprint());
            Assert.Equal(engine.HistoryFingerprint, reopened.HistoryFingerprint);
            Assert.Equal(engine.LivingStateJson, reopened.LivingStateJson);
            Assert.Contains(reopened.Citizens, c => c.CurrentAction == CitizenAction.LivingWork);
            Assert.DoesNotContain(reopened.Citizens, c => c.CurrentAction == CitizenAction.WorkFarm);

            var target = engine.CurrentMinute.Add(WorldCalendar.MinutesPerDay);
            engine.AdvanceUntil(target);
            reopened.AdvanceUntil(target);
            Assert.Equal(engine.HistoryFingerprint, reopened.HistoryFingerprint);
            Assert.Equal(engine.LivingStateJson, reopened.LivingStateJson);
            await using (var db = await WorldDatabase.OpenAsync(path))
                await db.CreateCheckpointStore().CheckpointAsync(reopened.CreatePersistenceSnapshot());
            await using (var db = await WorldDatabase.OpenAsync(path))
                Assert.Equal(engine.HistoryFingerprint, new SimulationEngine(await db.CreateCheckpointStore().LoadAsync()).HistoryFingerprint);
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task GrowingDatabaseCanAddLivingSupportWithoutNarrowingItsActionConstraint()
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-Reconciliation", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "world.db");
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.BarterSimulationRulesVersion);
            await using (var db = await WorldDatabase.OpenAsync(path))
                await db.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot());
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await Execute(connection, "ALTER TABLE world_meta DROP COLUMN living_state_json; DELETE FROM __EFMigrationsHistory WHERE MigrationId='20260920010000_LivingSettlement';");
            }
            await using (var db = await WorldDatabase.OpenAsync(path))
            {
                var snapshot = await db.CreateCheckpointStore().LoadAsync();
                Assert.Null(snapshot.LivingStateJson);
                Assert.Equal(SimulationEngine.BarterSimulationRulesVersion, snapshot.SimulationRulesVersion);
                Assert.Equal(engine.HistoryFingerprint, new SimulationEngine(snapshot).HistoryFingerprint);
                await db.CreateCheckpointStore().CheckpointAsync(snapshot);
            }
            await using var inspect = new SqliteConnection($"Data Source={path};Pooling=False");
            await inspect.OpenAsync();
            // An unapplied, older Living migration must not replace the M11 table.
            await using var transaction = await inspect.BeginTransactionAsync();
            await using var command = inspect.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "UPDATE citizens SET current_action=15 WHERE id=(SELECT min(id) FROM citizens)";
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
            await transaction.RollbackAsync();
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task Execute(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> Scalar(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
