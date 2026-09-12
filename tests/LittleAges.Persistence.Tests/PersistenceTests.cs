using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Data.Sqlite;
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
    public async Task ProductionOpenUpgradesActualM0AndRetainsCanonicalWorldOnReopen()
    {
        await WithDatabaseAsync(async path =>
        {
            var createdUtc = new DateTime(2026, 9, 1, 2, 3, 4, DateTimeKind.Utc);
            await CreateActualM0DatabaseAsync(path, ulong.MaxValue, 1234, createdUtc);
            var upgradeUtc = new DateTime(2026, 9, 2, 2, 3, 4, DateTimeKind.Utc);
            SimulationPersistenceSnapshot? firstSnapshot = null;

            await using (var database = await WorldDatabase.OpenAsync(path, new WorldDatabaseOpenOptions(upgradeUtc)))
            {
                var migrations = await database.Context.Database.GetAppliedMigrationsAsync();
                Assert.Equal(["20260912000000_InitialM0", "20260912010000_M1World"], migrations.ToArray());
                var snapshot = await database.CreateCheckpointStore().LoadAsync();
                Assert.Equal(ulong.MaxValue, snapshot.Seed.Value);
                Assert.Equal(1234, snapshot.WorldMinute.Value);
                Assert.Equal(SimulationEngine.CurrentWorldSchemaVersion, snapshot.WorldSchemaVersion);
                Assert.Equal(SimulationEngine.CurrentSimulationRulesVersion, snapshot.SimulationRulesVersion);
                Assert.Equal("m0-test", snapshot.ApplicationVersion);
                Assert.Equal(new DeterministicCountersSnapshot(256, 512, 9), snapshot.Counters);
                Assert.Equal(WorldGenerationConfiguration.Default.CanonicalJson, snapshot.World!.Configuration.CanonicalJson);
                Assert.Equal(WorldGenerationConfiguration.Default.CanonicalJson, snapshot.WorldConfiguration);
                Assert.Equal(WorldGenerationConfiguration.CurrentVersion, snapshot.World.GenerationVersion);
                Assert.Equal(25_600, snapshot.World.Tiles.Count);
                Assert.Equal(new WorldGenerator().Generate(new WorldSeed(ulong.MaxValue)).Fingerprint, snapshot.World.Fingerprint);
                Assert.Equal(
                    [
                        new ScheduledEventSnapshot(new ScheduledEventId(7), new ScheduledEventOrder(new WorldMinute(1250), 1, 2, 7), "sooner"),
                        new ScheduledEventSnapshot(new ScheduledEventId(8), new ScheduledEventOrder(new WorldMinute(1300), 2, 3, 8), "later")
                    ],
                    snapshot.ScheduledEvents);
                var metadata = await database.Context.WorldMeta.SingleAsync();
                Assert.Equal(createdUtc, metadata.CreatedUtc);
                Assert.Equal(upgradeUtc, metadata.LastCheckpointUtc);
                Assert.Equal(256, metadata.NextEntityId);
                Assert.Equal(512, metadata.NextHistoricalEventId);
                Assert.Equal(9, metadata.NextScheduledEventSequence);
                firstSnapshot = snapshot;
            }

            await using var reopened = await WorldDatabase.OpenAsync(path);
            var reopenedSnapshot = await reopened.CreateCheckpointStore().LoadAsync();
            Assert.NotNull(firstSnapshot);
            Assert.Equal(firstSnapshot!.Seed, reopenedSnapshot.Seed);
            Assert.Equal(firstSnapshot.WorldMinute, reopenedSnapshot.WorldMinute);
            Assert.Equal(firstSnapshot.WorldSchemaVersion, reopenedSnapshot.WorldSchemaVersion);
            Assert.Equal(firstSnapshot.SimulationRulesVersion, reopenedSnapshot.SimulationRulesVersion);
            Assert.Equal(firstSnapshot.ApplicationVersion, reopenedSnapshot.ApplicationVersion);
            Assert.Equal(firstSnapshot.WorldConfiguration, reopenedSnapshot.WorldConfiguration);
            Assert.Equal(firstSnapshot.Counters, reopenedSnapshot.Counters);
            Assert.Equal(firstSnapshot.ScheduledEvents, reopenedSnapshot.ScheduledEvents);
            AssertWorldEqual(firstSnapshot.World!, reopenedSnapshot.World!);
            Assert.Equal(reopenedSnapshot.World!.Tiles, reopenedSnapshot.World.EnumerateTilesRowMajor());
            Assert.Equal(upgradeUtc, (await reopened.Context.WorldMeta.SingleAsync()).LastCheckpointUtc);
        });
    }

    [Fact]
    public async Task OpenRejectsM1ConfigurationInLegacySentinelWithoutWritingRows()
    {
        await WithDatabaseAsync(async path =>
        {
            var createdUtc = new DateTime(2026, 9, 3, 2, 3, 4, DateTimeKind.Utc);
            await CreateActualM0DatabaseAsync(path, ulong.MaxValue, 321, createdUtc);
            await ApplyM1MigrationOnlyAsync(path);
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE world_meta SET world_configuration_json = $configuration;";
                command.Parameters.Add(new SqliteParameter("$configuration", WorldGenerationConfiguration.Default.CanonicalJson));
                await command.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsAsync<InvalidDataException>(() => WorldDatabase.OpenAsync(path));

            await using var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await verify.OpenAsync();
            await using (var command = verify.CreateCommand())
            {
                command.CommandText = "SELECT generation_version, generation_attempt, starting_x, starting_y, world_fingerprint, world_configuration_json, created_utc, last_checkpoint_utc FROM world_meta;";
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(0, reader.GetInt32(0));
                Assert.Equal(0, reader.GetInt32(1));
                Assert.Equal(0, reader.GetInt32(2));
                Assert.Equal(0, reader.GetInt32(3));
                Assert.Equal(string.Empty, reader.GetString(4));
                Assert.Equal(WorldGenerationConfiguration.Default.CanonicalJson, reader.GetString(5));
                Assert.Equal(createdUtc, DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind));
                Assert.Equal(createdUtc, DateTime.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind));
            }

            await using (var command = verify.CreateCommand())
            {
                command.CommandText = "SELECT (SELECT COUNT(*) FROM world_tiles), (SELECT COUNT(*) FROM resource_nodes), (SELECT COUNT(*) FROM scheduled_events);";
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(0L, reader.GetInt64(0));
                Assert.Equal(0L, reader.GetInt64(1));
                Assert.Equal(2L, reader.GetInt64(2));
            }
        });
    }

    [Fact]
    public async Task ActualM0UpgradeFailureRollsBackToSentinelAndRetrySucceeds()
    {
        await WithDatabaseAsync(async path =>
        {
            var createdUtc = new DateTime(2026, 9, 1, 2, 3, 4, DateTimeKind.Utc);
            await CreateActualM0DatabaseAsync(path, ulong.MaxValue, 77, createdUtc);
            var failureUtc = new DateTime(2026, 9, 2, 2, 3, 4, DateTimeKind.Utc);
            await Assert.ThrowsAsync<InvalidOperationException>(() => WorldDatabase.OpenAsync(
                path,
                new WorldDatabaseOpenOptions(failureUtc, LegacyUpgradeFailurePoint.AfterRowsWritten)));

            await AssertLegacySentinelAsync(path, 77, createdUtc);
            await using var retried = await WorldDatabase.OpenAsync(path);
            var upgraded = await retried.CreateCheckpointStore().LoadAsync();
            Assert.Equal(1, upgraded.World!.GenerationVersion);
            Assert.Equal(25_600, upgraded.World.Tiles.Count);
            Assert.Equal(new WorldGenerator().Generate(new WorldSeed(ulong.MaxValue)).Fingerprint, upgraded.World.Fingerprint);
        });
    }

    [Fact]
    public async Task OpenRejectsPartialLegacySentinelAndPartialM1WithoutRepair()
    {
        await WithDatabaseAsync(async path =>
        {
            await CreateActualM0DatabaseAsync(path, 3, 0, DateTime.UtcNow);
            await ApplyM1MigrationOnlyAsync(path);
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await connection.OpenAsync();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE world_meta SET starting_x = 1;";
                await command.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsAsync<InvalidDataException>(() => WorldDatabase.OpenAsync(path));
        });

        await WithDatabaseAsync(async path =>
        {
            await CreateActualM0DatabaseAsync(path, 4, 0, DateTime.UtcNow);
            await ApplyM1MigrationOnlyAsync(path);
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE world_meta SET generation_version = 1;";
                await command.ExecuteNonQueryAsync();
            }

            await using var database = await WorldDatabase.OpenAsync(path);
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

    private static async Task CreateActualM0DatabaseAsync(string path, ulong seed, long minute, DateTime createdUtc)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
        var options = new DbContextOptionsBuilder<LittleAgesDbContext>().UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(WorldDatabase).Assembly.GetName().Name)).Options;
        await using var context = new LittleAgesDbContext(options);
        await context.Database.OpenConnectionAsync();
        await context.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20260912000000_InitialM0");
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            INSERT INTO world_meta (id, world_seed, world_minute, world_schema_version, simulation_rules_version, application_version, world_configuration_json, next_entity_id, next_historical_event_id, next_scheduled_event_sequence, created_utc, last_checkpoint_utc)
            VALUES (1, $seed, $minute, $schema, $rules, $app, $config, 256, 512, 9, $created, $created);
            INSERT INTO scheduled_events (id, due_world_minute, priority, entity_sort_key, sequence, event_name, event_payload_json)
            VALUES (8, 1300, 2, 3, 8, 'later', '{}'), (7, 1250, 1, 2, 7, 'sooner', '{}');
            """;
        command.Parameters.Add(new SqliteParameter("$seed", seed.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        command.Parameters.Add(new SqliteParameter("$minute", minute));
        command.Parameters.Add(new SqliteParameter("$schema", SimulationEngine.CurrentWorldSchemaVersion));
        command.Parameters.Add(new SqliteParameter("$rules", SimulationEngine.CurrentSimulationRulesVersion));
        command.Parameters.Add(new SqliteParameter("$app", "m0-test"));
        command.Parameters.Add(new SqliteParameter("$config", "{\"calendar\":\"m0\"}"));
        command.Parameters.Add(new SqliteParameter("$created", createdUtc));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertLegacySentinelAsync(string path, long expectedMinute, DateTime expectedCreatedUtc)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT generation_version, generation_attempt, starting_x, starting_y, world_fingerprint, world_seed, world_minute, world_schema_version, simulation_rules_version, application_version, world_configuration_json, next_entity_id, next_historical_event_id, next_scheduled_event_sequence, created_utc, last_checkpoint_utc FROM world_meta;";
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0, reader.GetInt32(0));
            Assert.Equal(0, reader.GetInt32(1));
            Assert.Equal(0, reader.GetInt32(2));
            Assert.Equal(0, reader.GetInt32(3));
            Assert.Equal(string.Empty, reader.GetString(4));
            Assert.Equal(ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), reader.GetString(5));
            Assert.Equal(expectedMinute, reader.GetInt64(6));
            Assert.Equal(SimulationEngine.CurrentWorldSchemaVersion, reader.GetString(7));
            Assert.Equal(SimulationEngine.CurrentSimulationRulesVersion, reader.GetString(8));
            Assert.Equal("m0-test", reader.GetString(9));
            Assert.Equal("{\"calendar\":\"m0\"}", reader.GetString(10));
            Assert.Equal(256, reader.GetInt64(11));
            Assert.Equal(512, reader.GetInt64(12));
            Assert.Equal(9, reader.GetInt64(13));
            Assert.Equal(expectedCreatedUtc, DateTime.Parse(reader.GetString(14), null, System.Globalization.DateTimeStyles.RoundtripKind));
            Assert.Equal(expectedCreatedUtc, DateTime.Parse(reader.GetString(15), null, System.Globalization.DateTimeStyles.RoundtripKind));
        }
        command.CommandText = "SELECT COUNT(*) FROM world_tiles;";
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
        command.CommandText = "SELECT COUNT(*) FROM resource_nodes;";
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
        command.CommandText = "SELECT id, due_world_minute, priority, entity_sort_key, sequence, event_name, event_payload_json FROM scheduled_events ORDER BY sequence;";
        await using var events = await command.ExecuteReaderAsync();
        Assert.True(await events.ReadAsync());
        Assert.Equal(7L, events.GetInt64(0));
        Assert.Equal(1250L, events.GetInt64(1));
        Assert.Equal(1, events.GetInt32(2));
        Assert.Equal(2L, events.GetInt64(3));
        Assert.Equal(7L, events.GetInt64(4));
        Assert.Equal("sooner", events.GetString(5));
        Assert.Equal("{}", events.GetString(6));
        Assert.True(await events.ReadAsync());
        Assert.Equal(8L, events.GetInt64(0));
        Assert.Equal(1300L, events.GetInt64(1));
        Assert.Equal(2, events.GetInt32(2));
        Assert.Equal(3L, events.GetInt64(3));
        Assert.Equal(8L, events.GetInt64(4));
        Assert.Equal("later", events.GetString(5));
        Assert.Equal("{}", events.GetString(6));
        Assert.False(await events.ReadAsync());
    }

    private static async Task ApplyM1MigrationOnlyAsync(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
        var options = new DbContextOptionsBuilder<LittleAgesDbContext>().UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(WorldDatabase).Assembly.GetName().Name)).Options;
        await using var context = new LittleAgesDbContext(options);
        await context.Database.OpenConnectionAsync();
        await context.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync();
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
