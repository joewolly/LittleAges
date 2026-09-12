using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task OpenAppliesMigrationAndReopenRetainsSchema()
    {
        await WithDatabaseAsync(async path =>
        {
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                Assert.True(File.Exists(path));
                Assert.Equal("wal", (await database.ReadConnectionPragmasAsync()).JournalMode, ignoreCase: true);
                Assert.Equal(2, await CountTablesAsync(database));
            }

            await using var reopened = await WorldDatabase.OpenAsync(path);
            Assert.Equal(2, await CountTablesAsync(reopened));
        });
    }

    [Fact]
    public async Task OpenConfiguresAndVerifiesConnectionPragmas()
    {
        await WithDatabaseAsync(async path =>
        {
            await using var database = await WorldDatabase.OpenAsync(path);
            var pragmas = await database.ReadConnectionPragmasAsync();

            Assert.Equal("wal", pragmas.JournalMode, ignoreCase: true);
            Assert.Equal(1, pragmas.ForeignKeys);
            Assert.Equal(5_000, pragmas.BusyTimeoutMilliseconds);
        });
    }

    [Fact]
    public async Task CheckpointRoundTripsMaximumSeedMetadataCountersAndEventOrder()
    {
        await WithDatabaseAsync(async path =>
        {
            var source = CreateSnapshot(ulong.MaxValue, new WorldMinute(12_345));
            await using var database = await WorldDatabase.OpenAsync(path);
            var checkpointTime = new DateTime(2026, 9, 12, 10, 11, 12, DateTimeKind.Utc);
            await database.CreateCheckpointStore().CheckpointAsync(source, checkpointTime);

            var loaded = await database.CreateCheckpointStore().LoadAsync();
            Assert.Equal(source.Seed, loaded.Seed);
            Assert.Equal(source.WorldMinute, loaded.WorldMinute);
            Assert.Equal(source.WorldSchemaVersion, loaded.WorldSchemaVersion);
            Assert.Equal(source.SimulationRulesVersion, loaded.SimulationRulesVersion);
            Assert.Equal(source.ApplicationVersion, loaded.ApplicationVersion);
            Assert.Equal(source.WorldConfiguration, loaded.WorldConfiguration);
            Assert.Equal(source.Counters, loaded.Counters);
            Assert.Equal(source.ScheduledEvents.OrderBy(static scheduledEvent => scheduledEvent.Order), loaded.ScheduledEvents);
            var metadata = await database.Context.WorldMeta.SingleAsync();
            Assert.Equal(checkpointTime, metadata.CreatedUtc);
            Assert.Equal(checkpointTime, metadata.LastCheckpointUtc);

            var sourceRandom = new DeterministicRandom(source.Seed);
            var loadedRandom = new DeterministicRandom(loaded.Seed);
            Assert.Equal(
                sourceRandom.NextUInt64(RandomDomain.DecisionVariation, 77, 88, 99),
                loadedRandom.NextUInt64(RandomDomain.DecisionVariation, 77, 88, 99));
        });
    }

    [Fact]
    public async Task FailedCheckpointRollsBackAllRowsAndLeavesPreviousSnapshotIntact()
    {
        await WithDatabaseAsync(async path =>
        {
            var original = CreateSnapshot(17, new WorldMinute(10));
            var replacement = CreateSnapshot(18, new WorldMinute(99));
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(original, new DateTime(2026, 9, 12, 1, 0, 0, DateTimeKind.Utc));

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.CheckpointAsync(
                replacement,
                new DateTime(2026, 9, 12, 2, 0, 0, DateTimeKind.Utc),
                CheckpointFailurePoint.AfterRowsWritten));

            var loaded = await store.LoadAsync();
            Assert.Equal(original.Seed, loaded.Seed);
            Assert.Equal(original.WorldMinute, loaded.WorldMinute);
            Assert.Equal(original.ScheduledEvents.OrderBy(static scheduledEvent => scheduledEvent.Order), loaded.ScheduledEvents);
            var metadata = await database.Context.WorldMeta.SingleAsync();
            Assert.Equal(new DateTime(2026, 9, 12, 1, 0, 0, DateTimeKind.Utc), metadata.CreatedUtc);
            Assert.Equal(new DateTime(2026, 9, 12, 1, 0, 0, DateTimeKind.Utc), metadata.LastCheckpointUtc);
        });
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("rules")]
    public async Task LoadRejectsUnsupportedCompatibilityVersions(string versionKind)
    {
        await WithDatabaseAsync(async path =>
        {
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(CreateSnapshot(1, new WorldMinute(10)), new DateTime(2026, 9, 12, 3, 0, 0, DateTimeKind.Utc));
            var metadata = await database.Context.WorldMeta.SingleAsync();
            if (versionKind == "schema")
            {
                metadata.WorldSchemaVersion = "unsupported-schema";
            }
            else
            {
                metadata.SimulationRulesVersion = "unsupported-rules";
            }

            await database.Context.SaveChangesAsync();
            await Assert.ThrowsAsync<NotSupportedException>(() => store.LoadAsync());
        });
    }

    [Fact]
    public async Task LoadRejectsMalformedWorldConfigurationJson()
    {
        await WithDatabaseAsync(async path =>
        {
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(CreateSnapshot(1, new WorldMinute(10)), new DateTime(2026, 9, 12, 3, 0, 0, DateTimeKind.Utc));
            var metadata = await database.Context.WorldMeta.SingleAsync();
            metadata.WorldConfigurationJson = "{not-json";
            await database.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        });
    }

    [Fact]
    public async Task HasCheckpointRejectsOrphanedScheduledEvents()
    {
        await WithDatabaseAsync(async path =>
        {
            await using var database = await WorldDatabase.OpenAsync(path);
            database.Context.ScheduledEvents.Add(new ScheduledEventRow
            {
                Id = 1,
                DueWorldMinute = 5,
                Priority = 0,
                EntitySortKey = 0,
                Sequence = 1,
                EventName = "orphan",
                EventPayloadJson = "{}"
            });
            await database.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidDataException>(() => database.HasCheckpointAsync());
        });
    }

    private static SimulationPersistenceSnapshot CreateSnapshot(ulong seed, WorldMinute minute) => new(
        new WorldSeed(seed),
        minute,
        SimulationEngine.CurrentWorldSchemaVersion,
        SimulationEngine.CurrentSimulationRulesVersion,
        "application-test",
        "{\"calendar\":\"360-day\"}",
        new DeterministicCountersSnapshot(21, 34, 3),
        [
            new ScheduledEventSnapshot(new ScheduledEventId(2), new ScheduledEventOrder(minute + 20, 2, 50, 2), "later"),
            new ScheduledEventSnapshot(new ScheduledEventId(1), new ScheduledEventOrder(minute + 10, 1, 40, 1), "sooner")
        ]);

    private static async Task<int> CountTablesAsync(WorldDatabase database)
    {
        await using var command = database.Context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('world_meta', 'scheduled_events');";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LittleAges-Persistence-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "world.db");
        try
        {
            await test(path);
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var file = path + suffix;
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }

            Directory.Delete(directory, recursive: true);
        }
    }
}
