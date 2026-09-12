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
                Assert.Equal(4, await CountTablesAsync(database));
            }

            await using var reopened = await WorldDatabase.OpenAsync(path);
            Assert.Equal(4, await CountTablesAsync(reopened));
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
            Assert.NotNull(loaded.World);
            Assert.Equal(source.World!.OriginalSeed, loaded.World!.OriginalSeed);
            Assert.Equal(source.World.GenerationVersion, loaded.World.GenerationVersion);
            Assert.Equal(source.World.GenerationAttempt, loaded.World.GenerationAttempt);
            Assert.Equal(source.World.Configuration.CanonicalJson, loaded.World.Configuration.CanonicalJson);
            Assert.Equal(source.World.StartingSite, loaded.World.StartingSite);
            Assert.Equal(source.World.Tiles, loaded.World.Tiles);
            Assert.Equal(source.World.Resources, loaded.World.Resources);
            Assert.Equal(source.World.Fingerprint, loaded.World.Fingerprint);
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
            AssertWorldEqual(original.World!, loaded.World!);
            var metadata = await database.Context.WorldMeta.SingleAsync();
            Assert.Equal(new DateTime(2026, 9, 12, 1, 0, 0, DateTimeKind.Utc), metadata.CreatedUtc);
            Assert.Equal(new DateTime(2026, 9, 12, 1, 0, 0, DateTimeKind.Utc), metadata.LastCheckpointUtc);

            await database.DisposeAsync();
            await using var reopened = await WorldDatabase.OpenAsync(path);
            var reopenedSnapshot = await reopened.CreateCheckpointStore().LoadAsync();
            Assert.Equal(loaded.Seed, reopenedSnapshot.Seed);
            Assert.Equal(loaded.WorldMinute, reopenedSnapshot.WorldMinute);
            AssertWorldEqual(loaded.World!, reopenedSnapshot.World!);
        });
    }

    [Fact]
    public async Task SecondCheckpointAndReopenCyclePreservesCompleteWorld()
    {
        await WithDatabaseAsync(async path =>
        {
            var source = CreateSnapshot(23, new WorldMinute(7));
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                await database.CreateCheckpointStore().CheckpointAsync(source, new DateTime(2026, 9, 12, 5, 0, 0, DateTimeKind.Utc));
            }

            SimulationPersistenceSnapshot firstReload;
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                firstReload = await database.CreateCheckpointStore().LoadAsync();
                await database.CreateCheckpointStore().CheckpointAsync(firstReload, new DateTime(2026, 9, 12, 5, 1, 0, DateTimeKind.Utc));
            }

            await using var reopened = await WorldDatabase.OpenAsync(path);
            var secondReload = await reopened.CreateCheckpointStore().LoadAsync();
            Assert.Equal(firstReload.Seed, secondReload.Seed);
            Assert.Equal(firstReload.WorldMinute, secondReload.WorldMinute);
            Assert.Equal(firstReload.WorldConfiguration, secondReload.WorldConfiguration);
            Assert.Equal(firstReload.Counters, secondReload.Counters);
            Assert.Equal(firstReload.ScheduledEvents, secondReload.ScheduledEvents);
            AssertWorldEqual(firstReload.World!, secondReload.World!);
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

    private static SimulationPersistenceSnapshot CreateSnapshot(ulong seed, WorldMinute minute)
    {
        var world = new WorldGenerator().Generate(new WorldSeed(seed), WorldGenerationConfiguration.Default);
        return new SimulationPersistenceSnapshot(
            new WorldSeed(seed),
            minute,
            SimulationEngine.CurrentWorldSchemaVersion,
            SimulationEngine.CurrentSimulationRulesVersion,
            "application-test",
            world.Configuration.CanonicalJson,
            new DeterministicCountersSnapshot(21, 34, 3),
            [
                new ScheduledEventSnapshot(new ScheduledEventId(2), new ScheduledEventOrder(minute + 20, 2, 50, 2), "later"),
                new ScheduledEventSnapshot(new ScheduledEventId(1), new ScheduledEventOrder(minute + 10, 1, 40, 1), "sooner")
            ],
            world);
    }

    [Fact]
    public async Task HasCheckpointRejectsOrphanedWorldRows()
    {
        await WithDatabaseAsync(async path =>
        {
            await using var database = await WorldDatabase.OpenAsync(path);
            database.Context.WorldTiles.Add(new WorldTileRow { TileIndex = 0, X = 0, Y = 0, Terrain = 2, Elevation = 1, Fertility = 1, WaterAccess = 1, Walkable = true, MovementCost = 1 });
            await database.Context.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => database.HasCheckpointAsync());
        });
    }

    [Fact]
    public async Task LoadRejectsUnknownTerrainAndBadMovement()
    {
        await WithDatabaseAsync(async path =>
        {
            await using var database = await WorldDatabase.OpenAsync(path);
            await database.CreateCheckpointStore().CheckpointAsync(CreateSnapshot(1, WorldMinute.Zero), DateTime.UtcNow);
            await using var command = database.Context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA ignore_check_constraints = ON;";
            await command.ExecuteNonQueryAsync();
            command.CommandText = "UPDATE world_tiles SET terrain = 99 WHERE tile_index = 0;";
            await command.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => database.CreateCheckpointStore().LoadAsync());
            command.CommandText = "UPDATE world_tiles SET terrain = 2, walkable = 1, movement_cost = 0 WHERE tile_index = 0;";
            await command.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => database.CreateCheckpointStore().LoadAsync());
        });
    }

    [Fact]
    public async Task LoadRejectsUnknownGenerationVersionAndMissingTile()
    {
        await WithDatabaseAsync(async path =>
        {
            await using var database = await WorldDatabase.OpenAsync(path);
            await database.CreateCheckpointStore().CheckpointAsync(CreateSnapshot(2, WorldMinute.Zero), DateTime.UtcNow);
            var metadata = await database.Context.WorldMeta.SingleAsync();
            metadata.GenerationVersion = 99;
            await database.Context.SaveChangesAsync();
            await Assert.ThrowsAsync<NotSupportedException>(() => database.CreateCheckpointStore().LoadAsync());

            metadata.GenerationVersion = WorldGenerationConfiguration.CurrentVersion;
            await database.Context.SaveChangesAsync();
            await using var command = database.Context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "DELETE FROM world_tiles WHERE tile_index = 0;";
            await command.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => database.CreateCheckpointStore().LoadAsync());
        });
    }

    [Fact]
    public async Task LoadRejectsWorldFingerprintAndConfigurationMismatch()
    {
        await WithDatabaseAsync(async path =>
        {
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            var source = CreateSnapshot(4, WorldMinute.Zero);
            await store.CheckpointAsync(source, DateTime.UtcNow);
            var metadata = await database.Context.WorldMeta.SingleAsync();
            metadata.WorldFingerprint = new string('0', 64);
            await database.Context.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());

            metadata.WorldFingerprint = source.World!.Fingerprint;
            metadata.WorldConfigurationJson = "{}";
            await database.Context.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        });
    }

    [Fact]
    public async Task LoadRejectsUnknownResourceValueAndInvalidResourceReference()
    {
        await WithDatabaseAsync(async path =>
        {
            await using var database = await WorldDatabase.OpenAsync(path);
            await database.CreateCheckpointStore().CheckpointAsync(CreateSnapshot(5, WorldMinute.Zero), DateTime.UtcNow);
            await using var command = database.Context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA ignore_check_constraints = ON;";
            await command.ExecuteNonQueryAsync();
            command.CommandText = "UPDATE resource_nodes SET resource = 99 WHERE id = (SELECT id FROM resource_nodes LIMIT 1);";
            await command.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => database.CreateCheckpointStore().LoadAsync());
        });

        await WithDatabaseAsync(async path =>
        {
            await using var database = await WorldDatabase.OpenAsync(path);
            await database.CreateCheckpointStore().CheckpointAsync(CreateSnapshot(6, WorldMinute.Zero), DateTime.UtcNow);
            await using var command = database.Context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA ignore_check_constraints = ON;";
            await command.ExecuteNonQueryAsync();
            command.CommandText = "UPDATE resource_nodes SET tile_index = tile_index + 1 WHERE id = (SELECT id FROM resource_nodes LIMIT 1);";
            await command.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => database.CreateCheckpointStore().LoadAsync());
        });
    }

    private static async Task<int> CountTablesAsync(WorldDatabase database)
    {
        await using var command = database.Context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('world_meta', 'scheduled_events', 'world_tiles', 'resource_nodes');";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AssertWorldEqual(WorldMap expected, WorldMap actual)
    {
        Assert.Equal(expected.OriginalSeed, actual.OriginalSeed);
        Assert.Equal(expected.GenerationVersion, actual.GenerationVersion);
        Assert.Equal(expected.GenerationAttempt, actual.GenerationAttempt);
        Assert.Equal(expected.Configuration.CanonicalJson, actual.Configuration.CanonicalJson);
        Assert.Equal(expected.StartingSite, actual.StartingSite);
        Assert.Equal(expected.Tiles, actual.Tiles);
        Assert.Equal(expected.Resources, actual.Resources);
        Assert.Equal(expected.Fingerprint, actual.Fingerprint);
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
