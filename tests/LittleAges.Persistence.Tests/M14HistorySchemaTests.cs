using System.Data.Common;
using System.Reflection;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M14HistorySchemaTests
{
    private const string M14MigrationFoundation = "20260923020000_M14MigrationFoundation";

    [Fact]
    public async Task M14HistoryMigrationPreservesHistoryAndCheckpointsNewEventTypes()
    {
        var root = Path.Combine(Path.GetTempPath(), "littleages-m14-history-schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "world.db");
        try
        {
            var engine = new SimulationEngine(new WorldSeed(19024),
                simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
            var original = engine.CreatePersistenceSnapshot();
            string[] originalIndexes;
            (long Id, long Minute, int Type, int Importance, int Origin, string Payload, int SchemaVersion)[] originalEvents;
            (long EventId, long CitizenId, string Role)[] originalCitizenLinks;

            await using (var beforeMigration = await WorldDatabase.OpenAsync(path))
            {
                var migrator = beforeMigration.Context.Database.GetService<IMigrator>();
                await migrator.MigrateAsync(M14MigrationFoundation);
                await beforeMigration.CreateCheckpointStore().CheckpointAsync(original,
                    new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc));

                originalIndexes = await ReadExplicitIndexesAsync(beforeMigration.Context.Database.GetDbConnection());
                originalEvents = await beforeMigration.Context.HistoricalEvents.AsNoTracking()
                    .OrderBy(row => row.Id)
                    .Select(row => new ValueTuple<long, long, int, int, int, string, int>(row.Id, row.WorldMinute,
                        row.EventType, row.Importance, row.Origin, row.PayloadJson, row.SchemaVersion))
                    .ToArrayAsync();
                originalCitizenLinks = await beforeMigration.Context.HistoricalEventCitizens.AsNoTracking()
                    .OrderBy(row => row.EventId).ThenBy(row => row.CitizenId).ThenBy(row => row.Role)
                    .Select(row => new ValueTuple<long, long, string>(row.EventId, row.CitizenId, row.Role))
                    .ToArrayAsync();
                Assert.NotEmpty(originalCitizenLinks);
                Assert.Contains("BETWEEN 1 AND 15", await ReadHistoricalEventsSqlAsync(beforeMigration.Context.Database.GetDbConnection()), StringComparison.OrdinalIgnoreCase);
            }

            await using var database = await WorldDatabase.OpenAsync(path);
            var loaded = await database.CreateCheckpointStore().LoadAsync();
            Assert.Equal(originalEvents, loaded.HistoricalEvents.Select(item =>
                (item.Id.Value, item.WorldMinute, (int)item.EventType, (int)item.Importance, (int)item.Origin,
                    item.PayloadJson, item.SchemaVersion)).ToArray());
            Assert.Equal(originalCitizenLinks, loaded.HistoricalEventCitizens.Select(item =>
                (item.HistoricalEventId.Value, item.CitizenId.Value, item.Role)).ToArray());
            Assert.Equal(originalIndexes, await ReadExplicitIndexesAsync(database.Context.Database.GetDbConnection()));
            Assert.Contains("BETWEEN 1 AND 22", await ReadHistoricalEventsSqlAsync(database.Context.Database.GetDbConnection()), StringComparison.OrdinalIgnoreCase);

            var restoredEngine = SimulationEngine.FromPersistenceSnapshot(loaded);
            var family = restoredEngine.Citizens.Where(citizen => citizen.IsAlive).OrderBy(citizen => citizen.Id.Value).Take(2).ToArray();
            Assert.Equal(2, family.Length);
            var familyVisitPayload = HistoricalEventPayloads.FamilyVisitDeparted(
                family[0].Id.Value, family[1].Id.Value, originSettlementId: 1, destinationSettlementId: 2);
            var emitHistory = typeof(SimulationEngine).GetMethod("EmitHistory", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(emitHistory);
            emitHistory!.Invoke(restoredEngine,
            [
                HistoricalEventType.FamilyVisitDeparted,
                HistoricalImportance.Personal,
                null,
                familyVisitPayload,
                new[] { (family[0].Id, "subject"), (family[1].Id, "participant") },
                Array.Empty<(StructureId StructureId, string Role)>()
            ]);
            var expanded = restoredEngine.CreatePersistenceSnapshot();
            var newEvent = Assert.Single(expanded.HistoricalEvents, item => item.EventType == HistoricalEventType.FamilyVisitDeparted);

            await database.CreateCheckpointStore().CheckpointAsync(expanded,
                new DateTime(2026, 9, 24, 0, 1, 0, DateTimeKind.Utc));
            var reopened = await database.CreateCheckpointStore().LoadAsync();
            Assert.Contains(reopened.HistoricalEvents, item => item.Id == newEvent.Id && item.EventType == HistoricalEventType.FamilyVisitDeparted);
            Assert.Contains(reopened.HistoricalEventCitizens, link => link.HistoricalEventId == newEvent.Id && link.Role == "subject");

            var invalidId = reopened.HistoricalEvents.Max(item => item.Id.Value) + 1;
            var insertInvalidType = database.Context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO historical_events
                    (id, world_minute, event_type, importance, origin, location_x, location_y, payload_json, schema_version)
                VALUES ({invalidId}, {reopened.WorldMinute.Value}, {23}, {1}, {1}, NULL, NULL, {"{}"}, {1})
                """);
            var constraintFailure = await Assert.ThrowsAsync<SqliteException>(() => insertInvalidType);
            Assert.Equal(19, constraintFailure.SqliteErrorCode);

            var connection = database.Context.Database.GetDbConnection();
            Assert.Equal("ok", await ReadScalarAsync(connection, "PRAGMA integrity_check;"));
            Assert.Empty(await ReadRowsAsync(connection, "PRAGMA foreign_key_check;"));
            Assert.Equal("1", await ReadScalarAsync(connection, "PRAGMA foreign_keys;"));
            Assert.Contains("historical_events", await ReadRowsAsync(connection, "PRAGMA foreign_key_list('historical_event_citizens');"));
            Assert.Contains("historical_events", await ReadRowsAsync(connection, "PRAGMA foreign_key_list('historical_event_structures');"));
            Assert.Contains("historical_events", await ReadRowsAsync(connection, "PRAGMA foreign_key_list('memories');"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string[]> ReadExplicitIndexesAsync(DbConnection connection)
    {
        var indexes = new List<string>();
        foreach (var table in new[] { "historical_events", "historical_event_citizens", "historical_event_structures", "memories" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA index_list('{table}');";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (!name.StartsWith("sqlite_autoindex_", StringComparison.Ordinal)) indexes.Add(name);
            }
        }

        return indexes.Order(StringComparer.Ordinal).ToArray();
    }

    private static async Task<string> ReadHistoricalEventsSqlAsync(DbConnection connection) =>
        await ReadScalarAsync(connection, "SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = 'historical_events';");

    private static async Task<string> ReadScalarAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static async Task<string[]> ReadRowsAsync(DbConnection connection, string sql)
    {
        var rows = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            for (var column = 0; column < reader.FieldCount; column++)
                if (!reader.IsDBNull(column)) rows.Add(Convert.ToString(reader.GetValue(column), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }
        return rows.ToArray();
    }
}
