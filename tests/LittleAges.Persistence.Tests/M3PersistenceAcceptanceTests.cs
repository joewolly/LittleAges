using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M3PersistenceAcceptanceTests
{
    [Theory]
    [InlineData("survival0-with-resource-state", "UPDATE world_meta SET survival_version = 0;")]
    [InlineData("missing-resource-state", "DELETE FROM resource_state WHERE resource_node_id = (SELECT resource_node_id FROM resource_state LIMIT 1);")]
    [InlineData("missing-settlement", "DELETE FROM settlement_state;")]
    [InlineData("multiple-settlement", "PRAGMA ignore_check_constraints = ON; INSERT INTO settlement_state (id, food_stored, wood_stored, stone_stored, base_storage_capacity, demand_updated_minute, exposure_consequences_start_minute) VALUES (2, 0, 0, 0, 0, 0, 0);")]
    [InlineData("orphan-resource-state", "PRAGMA foreign_keys = OFF; INSERT INTO resource_state (resource_node_id, current_quantity) VALUES (999999, 0);")]
    [InlineData("missing-survival-event", "DELETE FROM scheduled_events WHERE id = (SELECT id FROM scheduled_events WHERE event_name = 'citizen.survival-check.v1' LIMIT 1);")]
    [InlineData("missing-regen-event", "DELETE FROM scheduled_events WHERE event_name = 'resource.regenerate.v1';")]
    [InlineData("quantity-above-maximum", "PRAGMA ignore_check_constraints = ON; UPDATE resource_state SET current_quantity = (SELECT maximum_quantity + 1 FROM resource_nodes WHERE resource_nodes.id = resource_state.resource_node_id) WHERE resource_node_id = (SELECT resource_node_id FROM resource_state LIMIT 1);")]
    [InlineData("invalid-phase", "PRAGMA ignore_check_constraints = ON; UPDATE citizens SET action_phase = 99 WHERE id = 1;")]
    [InlineData("invalid-carrying-type", "PRAGMA ignore_check_constraints = ON; UPDATE citizens SET carried_resource_type = 99, carried_resource_quantity = 1 WHERE id = 1;")]
    [InlineData("invalid-survival-payload", "UPDATE scheduled_events SET event_payload_json = '{}' WHERE id = (SELECT id FROM scheduled_events WHERE event_name = 'citizen.survival-check.v1' LIMIT 1);")]
    [InlineData("invalid-survival-timing", "UPDATE scheduled_events SET due_world_minute = due_world_minute + 1 WHERE id = (SELECT id FROM scheduled_events WHERE event_name = 'citizen.survival-check.v1' LIMIT 1);")]
    public async Task M3CorruptionIsRejectedWithoutRepair(string _, string mutation)
    {
        await WithDatabaseAsync(async path =>
        {
            await CreateFreshM3Async(path);
            await using var database = await WorldDatabase.OpenAsync(path);
            await using var command = database.Context.Database.GetDbConnection().CreateCommand();
            command.CommandText = mutation;
            await command.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => database.CreateCheckpointStore().LoadAsync());
        });
    }

    [Fact]
    public async Task UnknownSurvivalVersionIsRejected()
    {
        await WithDatabaseAsync(async path =>
        {
            await CreateFreshM3Async(path);
            await using var database = await WorldDatabase.OpenAsync(path);
            await using var command = database.Context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA ignore_check_constraints = ON; UPDATE world_meta SET survival_version = 99 WHERE id = 1;";
            await command.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<NotSupportedException>(() => database.CreateCheckpointStore().LoadAsync());
        });
    }

    [Fact]
    public async Task M3MigrationCanDowngradeToM2Schema()
    {
        await WithDatabaseAsync(async path =>
        {
            var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
            var options = new DbContextOptionsBuilder<LittleAgesDbContext>().UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(WorldDatabase).Assembly.GetName().Name)).Options;
            await using var context = new LittleAgesDbContext(options);
            await context.Database.OpenConnectionAsync();
            var migrator = context.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
            await migrator.MigrateAsync();
            await migrator.MigrateAsync("20260912020000_M2Citizens");
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('resource_state', 'settlement_state');";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('world_meta') WHERE name = 'survival_version';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('citizens') WHERE name IN ('health_updated_minute', 'action_phase', 'target_resource_node_id', 'carried_resource_type', 'carried_resource_quantity');";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        });
    }

    [Theory]
    [InlineData(CitizenAction.Idle, CitizenActionPhase.Perform)]
    [InlineData(CitizenAction.Rest, CitizenActionPhase.Perform)]
    [InlineData(CitizenAction.Wander, CitizenActionPhase.TravelToTarget)]
    [InlineData(CitizenAction.Explore, CitizenActionPhase.TravelToTarget)]
    public async Task M2UpgradeDerivesPhaseAndPreservesActionEvent(CitizenAction action, CitizenActionPhase expectedPhase)
    {
        await WithDatabaseAsync(async path =>
        {
            var source = CreateM2ActionSnapshot(action);
            var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
            var options = new DbContextOptionsBuilder<LittleAgesDbContext>().UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(WorldDatabase).Assembly.GetName().Name)).Options;
            await using (var context = new LittleAgesDbContext(options))
            {
                await context.Database.MigrateAsync();
                await new WorldCheckpointStore(context).CheckpointAsync(source, DateTime.UtcNow);
            }

            await using var database = await WorldDatabase.OpenAsync(path);
            var loaded = await database.CreateCheckpointStore().LoadAsync();
            var expected = source.Citizens.Single(x => x.Id.Value == 1);
            var actual = loaded.Citizens.Single(x => x.Id.Value == 1);
            Assert.Equal(expected.CurrentAction, actual.CurrentAction);
            Assert.Equal(expected.ActionSequence, actual.ActionSequence);
            Assert.Equal(expected.ActionStartedMinute, actual.ActionStartedMinute);
            Assert.Equal(expected.ActionCompletesMinute, actual.ActionCompletesMinute);
            Assert.Equal(expected.ActionTarget, actual.ActionTarget);
            Assert.Equal(expectedPhase, actual.ActionPhase);
            Assert.Equal(10000, actual.Health);
            Assert.Equal(0, actual.Needs.Hunger);
            Assert.Equal(0, actual.HealthUpdatedMinute);
        });
    }

    [Fact]
    public async Task M2UpgradePreservesSaturatedHungerAndAppliesNoRetroactiveDamage()
    {
        await WithDatabaseAsync(async path =>
        {
            var source = CreateM2ActionSnapshot(CitizenAction.Idle);
            source.Citizens[0].Needs = new CitizenNeeds(10000, 7000, 0, 0);
            await SaveM2AndUpgradeAsync(path, source);
            await using var database = await WorldDatabase.OpenAsync(path);
            var citizen = (await database.CreateCheckpointStore().LoadAsync()).Citizens.Single(x => x.Id.Value == 1);
            Assert.Equal(10000, citizen.Needs.Hunger);
            Assert.Equal(10000, citizen.Health);
            Assert.Equal(0, citizen.HealthUpdatedMinute);
        });
    }

    [Fact]
    public async Task M3CheckpointFailureRestoresResourceStorageHealthSkillAndPhase()
    {
        await WithDatabaseAsync(async path =>
        {
            var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(source.CreatePersistenceSnapshot(), DateTime.UtcNow);
            var changed = source.CreatePersistenceSnapshot();
            changed.ResourceStates[0].CurrentQuantity = Math.Max(0, changed.ResourceStates[0].CurrentQuantity - 1);
            changed.Settlement!.FoodStored = 17;
            changed.Citizens[0].Health = 7777;
            changed.Citizens[0].Skills.Foraging = checked(changed.Citizens[0].Skills.Foraging + 25);
            changed.Citizens[0].CurrentAction = CitizenAction.Idle;
            changed.Citizens[0].ActionPhase = CitizenActionPhase.Perform;
            changed.Citizens[0].ActionStartedMinute = WorldMinute.Zero;
            changed.Citizens[0].ActionCompletesMinute = new WorldMinute(1000);
            var actionEvent = changed.ScheduledEvents.Single(x => x.Order.EntitySortKey == 1 && x.Name == CitizenEventNames.Decision);
            var events = changed.ScheduledEvents.Select(x => x == actionEvent ? x with { Name = CitizenEventNames.ActionComplete, Order = new ScheduledEventOrder(new WorldMinute(1000), CitizenEventNames.CompletionPriority, 1, x.Order.Sequence), PayloadJson = "{\"citizenId\":\"1\",\"actionSequence\":0}" } : x).ToArray();
            changed = new SimulationPersistenceSnapshot(changed.Seed, changed.WorldMinute, changed.WorldSchemaVersion, changed.SimulationRulesVersion, changed.ApplicationVersion, changed.WorldConfiguration, changed.Counters, events, changed.World, changed.Citizens, changed.CitizenGenerationVersion, changed.ResourceStates, changed.Settlement, changed.SurvivalVersion);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.CheckpointAsync(changed, DateTime.UtcNow, CheckpointFailurePoint.AfterRowsWritten));
            var restored = await store.LoadAsync();
            Assert.Equal(source.ResourceStates, restored.ResourceStates);
            Assert.Equal(source.Settlement, restored.Settlement);
            Assert.Equal(source.Citizens, restored.Citizens);
        });
    }

    [Theory]
    [InlineData(CitizenAction.Eat)]
    [InlineData(CitizenAction.GatherFood)]
    public async Task M3CheckpointRoundTripsEatAndGatherPhases(CitizenAction action)
    {
        await WithDatabaseAsync(async path =>
        {
            var snapshot = CreateM3ActionSnapshot(action);
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(snapshot, DateTime.UtcNow);
            var loaded = await store.LoadAsync();
            var citizen = loaded.Citizens.Single(x => x.Id.Value == 1);
            Assert.Equal(action, citizen.CurrentAction);
            Assert.Equal(action == CitizenAction.Eat ? CitizenActionPhase.Perform : CitizenActionPhase.TravelToTarget, citizen.ActionPhase);
            Assert.Equal(snapshot.Citizens[0].ActionCompletesMinute, citizen.ActionCompletesMinute);
            Assert.Equal(snapshot.Citizens[0].ActionTarget, citizen.ActionTarget);
            Assert.Equal(snapshot.Citizens[0].TargetResourceNodeId, citizen.TargetResourceNodeId);
            var expectedEvent = snapshot.ScheduledEvents.Single(x => x.Order.EntitySortKey == 1 && x.Name != CitizenEventNames.SurvivalCheck);
            Assert.Equal(expectedEvent, loaded.ScheduledEvents.Single(x => x.Order.EntitySortKey == 1 && x.Name != CitizenEventNames.SurvivalCheck));
        });
    }

    [Fact]
    public async Task DeadCitizenRoundTripsAndHasNoFutureEvents()
    {
        await WithDatabaseAsync(async path =>
        {
            var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
            var snapshot = source.CreatePersistenceSnapshot();
            var dead = snapshot.Citizens[0];
            dead.Health = 0; dead.DeathMinute = 0; dead.DeathCause = "starvation"; dead.CurrentAction = CitizenAction.Dead; dead.ActionPhase = CitizenActionPhase.None; dead.ActionSequence = 0; dead.ActionStartedMinute = null; dead.ActionCompletesMinute = null; dead.ActionTarget = null; dead.TargetResourceNodeId = null; dead.CarriedResourceType = null; dead.CarriedResourceQuantity = 0;
            var events = snapshot.ScheduledEvents.Where(x => x.Order.EntitySortKey != dead.Id.Value).ToArray();
            var adjusted = new SimulationPersistenceSnapshot(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters, events, snapshot.World, snapshot.Citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates, snapshot.Settlement, snapshot.SurvivalVersion);
            await using var database = await WorldDatabase.OpenAsync(path);
            await database.CreateCheckpointStore().CheckpointAsync(adjusted, DateTime.UtcNow);
            var loaded = await database.CreateCheckpointStore().LoadAsync();
            var restored = loaded.Citizens.Single(x => x.Id.Value == dead.Id.Value);
            Assert.False(restored.IsAlive); Assert.Equal(0, restored.Health); Assert.Equal(0, restored.DeathMinute); Assert.Equal("starvation", restored.DeathCause);
            Assert.DoesNotContain(loaded.ScheduledEvents, x => x.Order.EntitySortKey == dead.Id.Value);
        });
    }

    [Fact]
    public async Task ActualM0ToM4ChainIsIdempotent()
    {
        await WithDatabaseAsync(async path =>
        {
            await CreateActualM0DatabaseAsync(path);
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var snapshot = await database.CreateCheckpointStore().LoadAsync();
                Assert.Equal(1, snapshot.SurvivalVersion); Assert.Equal(20, snapshot.Citizens.Count); Assert.Equal(snapshot.World!.Resources.Count, snapshot.ResourceStates.Count); Assert.Equal(400, snapshot.Settlement!.FoodStored);
                Assert.Equal(22 + 20 + 1 + 1, snapshot.ScheduledEvents.Count);
                Assert.Equal(1, snapshot.SettlementVersion);
                Assert.Equal(CitizenSimulationRules.BaseStorageCapacity, snapshot.Settlement.BaseStorageCapacity);
                Assert.Equal(snapshot.WorldMinute.Value, snapshot.Settlement.DemandUpdatedMinute);
                Assert.Equal(snapshot.WorldMinute.Add(CitizenSimulationRules.ExposureGraceDurationMinutes).Value, snapshot.Settlement.ExposureConsequencesStartMinute);
                Assert.Equal(51, snapshot.Counters.NextScheduledEventSequence);
                Assert.Contains(snapshot.ScheduledEvents, item => item.Name == CitizenEventNames.SettlementEvaluateDemand && item.PayloadJson == "{\"version\":1}");
            }
            await using var reopened = await WorldDatabase.OpenAsync(path);
            var second = await reopened.CreateCheckpointStore().LoadAsync();
            Assert.Equal(1, second.SurvivalVersion); Assert.Equal(400, second.Settlement!.FoodStored); Assert.Equal(20, second.Citizens.Count);
        });
    }

    private static SimulationPersistenceSnapshot CreateM2ActionSnapshot(CitizenAction action)
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.PreviousSimulationRulesVersion);
        var baseline = engine.CreatePersistenceSnapshot();
        var world = baseline.World!;
        var citizen = baseline.Citizens[0]; citizen.CurrentAction = action; citizen.ActionStartedMinute = WorldMinute.Zero;
        ScheduledEventSnapshot replacement;
        if (action is CitizenAction.Wander or CitizenAction.Explore)
        {
            var target = world.Tiles.Where(x => x.Walkable && x.Coordinate != citizen.Location).Select(x => (x.Coordinate, Path: DeterministicPathfinder.Find(world, citizen.Location, x.Coordinate))).First(x => x.Path is { Count: >= 3 });
            var path = target.Path!;
            citizen.ActionTarget = target.Coordinate;
            var cost = path.Skip(1).Zip(path, (next, previous) => (long)(next.X == previous.X || next.Y == previous.Y ? 10 : 14) * world.GetTile(next).MovementCost).Sum();
            citizen.ActionCompletesMinute = new WorldMinute(cost);
            var first = path[1]; var firstCost = (long)(first.X == citizen.Location.X || first.Y == citizen.Location.Y ? 10 : 14) * world.GetTile(first).MovementCost;
            replacement = new ScheduledEventSnapshot(new ScheduledEventId(1), new ScheduledEventOrder(new WorldMinute(firstCost), CitizenEventNames.MovementPriority, 1, 1), CitizenEventNames.MoveStep, "{\"citizenId\":\"1\",\"actionSequence\":0}");
        }
        else
        {
            citizen.ActionCompletesMinute = new WorldMinute(action == CitizenAction.Rest ? CitizenSimulationRules.RestDurationMinutes : 1000);
            replacement = new ScheduledEventSnapshot(new ScheduledEventId(1), new ScheduledEventOrder(citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority, 1, 1), CitizenEventNames.ActionComplete, "{\"citizenId\":\"1\",\"actionSequence\":0}");
        }
        var events = baseline.ScheduledEvents.Select(x => x.Order.EntitySortKey == 1 ? replacement : x).ToArray();
        return new SimulationPersistenceSnapshot(baseline.Seed, baseline.WorldMinute, baseline.WorldSchemaVersion, baseline.SimulationRulesVersion, baseline.ApplicationVersion, baseline.WorldConfiguration, baseline.Counters, events, baseline.World, baseline.Citizens, baseline.CitizenGenerationVersion);
    }

    private static SimulationPersistenceSnapshot CreateM3ActionSnapshot(CitizenAction action)
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        var baseline = engine.CreatePersistenceSnapshot();
        var world = baseline.World!;
        var citizen = baseline.Citizens[0];
        citizen.CurrentAction = action;
        citizen.ActionPhase = action == CitizenAction.GatherFood ? CitizenActionPhase.TravelToTarget : CitizenActionPhase.Perform;
        citizen.ActionStartedMinute = WorldMinute.Zero;
        ScheduledEventSnapshot replacement;
        if (action == CitizenAction.GatherFood)
        {
            var node = world.Resources.First(x => x.Type == ResourceType.Food);
            var path = DeterministicPathfinder.Find(world, citizen.Location, node.Coordinate);
            Assert.NotNull(path);
            Assert.True(path!.Count >= 2);
            citizen.TargetResourceNodeId = node.Id;
            citizen.ActionTarget = node.Coordinate;
            var travelCost = path.Skip(1).Zip(path, (next, previous) => (long)(next.X == previous.X || next.Y == previous.Y ? 10 : 14) * world.GetTile(next).MovementCost).Sum();
            citizen.ActionCompletesMinute = new WorldMinute(travelCost);
            var first = path[1];
            var firstCost = (long)(first.X == citizen.Location.X || first.Y == citizen.Location.Y ? 10 : 14) * world.GetTile(first).MovementCost;
            replacement = new ScheduledEventSnapshot(new ScheduledEventId(1), new ScheduledEventOrder(new WorldMinute(firstCost), CitizenEventNames.MovementPriority, 1, 1), CitizenEventNames.MoveStep, "{\"citizenId\":\"1\",\"actionSequence\":0}");
        }
        else
        {
            citizen.ActionCompletesMinute = new WorldMinute(CitizenSimulationRules.EatDurationMinutes);
            citizen.ActionTarget = null;
            replacement = new ScheduledEventSnapshot(new ScheduledEventId(1), new ScheduledEventOrder(citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority, 1, 1), CitizenEventNames.ActionComplete, "{\"citizenId\":\"1\",\"actionSequence\":0}");
        }
        var events = baseline.ScheduledEvents.Select(x => x.Order.EntitySortKey == 1 && x.Name == CitizenEventNames.Decision ? replacement : x).ToArray();
        return new SimulationPersistenceSnapshot(baseline.Seed, baseline.WorldMinute, baseline.WorldSchemaVersion, baseline.SimulationRulesVersion, baseline.ApplicationVersion, baseline.WorldConfiguration, baseline.Counters, events, baseline.World, baseline.Citizens, baseline.CitizenGenerationVersion, baseline.ResourceStates, baseline.Settlement, baseline.SurvivalVersion);
    }

    private static async Task SaveM2AndUpgradeAsync(string path, SimulationPersistenceSnapshot source)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        var options = new DbContextOptionsBuilder<LittleAgesDbContext>().UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(WorldDatabase).Assembly.GetName().Name)).Options;
        await using (var context = new LittleAgesDbContext(options)) { await context.Database.MigrateAsync(); await new WorldCheckpointStore(context).CheckpointAsync(source, DateTime.UtcNow); }
    }

    private static async Task CreateFreshM3Async(string path)
    {
        await using var database = await WorldDatabase.OpenAsync(path);
        await database.CreateCheckpointStore().CheckpointAsync(new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion).CreatePersistenceSnapshot(), DateTime.UtcNow);
    }

    private static async Task CreateActualM0DatabaseAsync(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
        var options = new DbContextOptionsBuilder<LittleAgesDbContext>().UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(WorldDatabase).Assembly.GetName().Name)).Options;
        await using var context = new LittleAgesDbContext(options);
        await context.Database.OpenConnectionAsync();
        await context.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20260912000000_InitialM0");
        await using var command = context.Database.GetDbConnection().CreateCommand(); command.CommandText = "INSERT INTO world_meta (id, world_seed, world_minute, world_schema_version, simulation_rules_version, application_version, world_configuration_json, next_entity_id, next_historical_event_id, next_scheduled_event_sequence, created_utc, last_checkpoint_utc) VALUES (1, '42', 0, '0.1', 'm0-rng1', 'm0-test', '{\"calendar\":\"m0\"}', 256, 512, 9, $now, $now); INSERT INTO scheduled_events (id, due_world_minute, priority, entity_sort_key, sequence, event_name, event_payload_json) VALUES (7, 1250, 10, 0, 7, 'legacy.event.a', '{}'), (8, 1300, 11, 0, 8, 'legacy.event.b', '{}');"; command.Parameters.Add(new SqliteParameter("$now", DateTime.UtcNow)); await command.ExecuteNonQueryAsync();
    }

    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LittleAges-M3-Acceptance-Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); var path = Path.Combine(directory, "world.db");
        try { await test(path); } finally { foreach (var suffix in new[] { string.Empty, "-wal", "-shm" }) { var file = path + suffix; if (File.Exists(file)) File.Delete(file); } if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
