using System.Globalization;
using System.Text;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M3AcceptanceMatrixTests
{
    [Theory]
    [InlineData("mid-eat-travel")]
    [InlineData("mid-eat-perform")]
    [InlineData("mid-gather-outbound")]
    [InlineData("mid-gather-perform")]
    [InlineData("mid-gather-return")]
    [InlineData("before-survival")]
    [InlineData("before-regeneration")]
    public async Task SevenPointM3CheckpointMatrixPreservesFieldsAndContinuation(string phase)
    {
        await WithDatabaseAsync(async path =>
        {
            var source = BuildPhaseSnapshot(phase);
            await using var database = await WorldDatabase.OpenAsync(path);
            await database.CreateCheckpointStore().CheckpointAsync(source.CreatePersistenceSnapshot(), DateTime.UtcNow);
            var loaded = await database.CreateCheckpointStore().LoadAsync();

            var sourceCitizen = source.GetCitizen(new CitizenId(1))!;
            var loadedCitizen = loaded.Citizens.Single(x => x.Id == new CitizenId(1));
            Assert.Equal(source.CurrentMinute, loaded.WorldMinute);
            Assert.Equal(source.Settlement.FoodStored, loaded.Settlement!.FoodStored);
            Assert.Equal(source.Settlement.WoodStored, loaded.Settlement.WoodStored);
            Assert.Equal(source.Settlement.StoneStored, loaded.Settlement.StoneStored);
            Assert.Equal(sourceCitizen.CurrentAction, loadedCitizen.CurrentAction);
            Assert.Equal(sourceCitizen.ActionPhase, loadedCitizen.ActionPhase);
            Assert.Equal(sourceCitizen.ActionStartedMinute, loadedCitizen.ActionStartedMinute);
            Assert.Equal(sourceCitizen.ActionCompletesMinute, loadedCitizen.ActionCompletesMinute);
            Assert.Equal(sourceCitizen.TargetResourceNodeId, loadedCitizen.TargetResourceNodeId);
            Assert.Equal(sourceCitizen.CarriedResourceType, loadedCitizen.CarriedResourceType);
            Assert.Equal(sourceCitizen.CarriedResourceQuantity, loadedCitizen.CarriedResourceQuantity);
            Assert.Equal(Canonical(source.CreatePersistenceSnapshot()), Canonical(loaded));

            var target = source.CurrentMinute.Add(720);
            source.AdvanceUntil(target);
            var resumed = new SimulationEngine(loaded);
            resumed.AdvanceUntil(target);
            Assert.Equal(source.SurvivalFingerprint, resumed.SurvivalFingerprint);
            Assert.Equal(Canonical(source.CreatePersistenceSnapshot()), Canonical(resumed.CreatePersistenceSnapshot()));
        });
    }

    [Fact]
    public async Task SaveReloadImmediatelyBeforeRegenerationHasIdenticalContinuation()
    {
        await WithDatabaseAsync(async path =>
        {
            var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
            source.AdvanceUntil(new WorldMinute(WorldCalendar.MinutesPerDay - 1));
            var before = source.CreatePersistenceSnapshot();
            await using var database = await WorldDatabase.OpenAsync(path);
            await database.CreateCheckpointStore().CheckpointAsync(before, DateTime.UtcNow);
            var reloaded = SimulationEngine.FromPersistenceSnapshot(await database.CreateCheckpointStore().LoadAsync());
            Assert.Equal(Canonical(before), Canonical(reloaded.CreatePersistenceSnapshot()));
            source.AdvanceUntil(new WorldMinute(WorldCalendar.MinutesPerDay));
            reloaded.AdvanceUntil(new WorldMinute(WorldCalendar.MinutesPerDay));
            Assert.Equal(source.SurvivalFingerprint, reloaded.SurvivalFingerprint);
            Assert.Equal(Canonical(source.CreatePersistenceSnapshot()), Canonical(reloaded.CreatePersistenceSnapshot()));
        });
    }

    [Fact]
    public async Task ScarcityOnlyStarvesCitizenAtExactComputedMinuteAndDeadStatePersists()
    {
        await WithDatabaseAsync(async path =>
        {
            var engine = CreateScarcityEngine();
            var citizenId = new CitizenId(1);
            var health = 1000;
            var expectedDeathMinute = 0L;
            for (var minute = CitizenSimulationRules.SurvivalCheckIntervalMinutes; ; minute += CitizenSimulationRules.SurvivalCheckIntervalMinutes)
            {
                var hunger = NeedsProjection.ProjectHunger(9000, 0, minute);
                var damage = (CitizenSimulationRules.StarvationDamagePerCheck * (10000 - engine.GetCitizen(citizenId)!.Traits.Resilience / 4)) / 10000;
                health -= damage;
                if (health <= 0) { expectedDeathMinute = minute; break; }
            }
            engine.AdvanceUntil(new WorldMinute(expectedDeathMinute));
            var dead = engine.GetCitizen(citizenId)!;
            Assert.Equal(expectedDeathMinute, dead.DeathMinute);
            Assert.Equal("starvation", dead.DeathCause);
            Assert.Equal(0, dead.Health);
            Assert.True(dead.Needs.Hunger >= CitizenSimulationRules.StarvationThreshold);
            Assert.DoesNotContain(engine.CreatePersistenceSnapshot().ScheduledEvents, e => e.Order.EntitySortKey == citizenId.Value && e.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete or CitizenEventNames.SurvivalCheck);
            Assert.Equal(0, engine.Settlement.FoodStored);
            Assert.All(engine.ResourceStates.Where(state => engine.World.Resources.Single(node => node.Id == state.ResourceNodeId).Type == ResourceType.Food), state => Assert.Equal(0, state.CurrentQuantity));

            await using var database = await WorldDatabase.OpenAsync(path);
            await database.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot(), DateTime.UtcNow);
            var loaded = await database.CreateCheckpointStore().LoadAsync();
            var restored = loaded.Citizens.Single(c => c.Id == citizenId);
            Assert.False(restored.IsAlive);
            Assert.Equal(expectedDeathMinute, restored.DeathMinute);
            Assert.Equal("starvation", restored.DeathCause);
            Assert.DoesNotContain(loaded.ScheduledEvents, e => e.Order.EntitySortKey == citizenId.Value && e.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete or CitizenEventNames.SurvivalCheck);
        });
    }

    [Fact]
    public async Task ActualM0CollidingLegacyEventSurvivesM4UpgradeAndCitizenDeath()
    {
        await WithDatabaseAsync(async path =>
        {
            await CreateActualM0WithCollidingEventAsync(path);
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            var upgraded = await store.LoadAsync();
            var legacy = upgraded.ScheduledEvents.Single(item => item.Name == "legacy.m0.colliding");
            var dead = upgraded.Citizens.OrderBy(item => item.Id.Value).First();
            Assert.Equal(dead.Id.Value, legacy.Order.EntitySortKey);
            dead.Health = 0;
            dead.DeathMinute = upgraded.WorldMinute.Value;
            dead.DeathCause = "starvation";
            dead.CurrentAction = CitizenAction.Dead;
            dead.ActionPhase = CitizenActionPhase.None;
            dead.ActionTarget = null;
            dead.TargetResourceNodeId = null;
            dead.ActionStartedMinute = null;
            dead.ActionCompletesMinute = null;
            dead.CarriedResourceType = null;
            dead.CarriedResourceQuantity = 0;
            var events = upgraded.ScheduledEvents.Where(item =>
                item.Order.EntitySortKey != dead.Id.Value ||
                item.Name is not (CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete or CitizenEventNames.SurvivalCheck)).ToArray();
            var adjusted = new SimulationPersistenceSnapshot(upgraded.Seed, upgraded.WorldMinute, upgraded.WorldSchemaVersion,
                upgraded.SimulationRulesVersion, upgraded.ApplicationVersion, upgraded.WorldConfiguration, upgraded.Counters,
                events, upgraded.World, upgraded.Citizens, upgraded.CitizenGenerationVersion, upgraded.ResourceStates,
                upgraded.Settlement, upgraded.SurvivalVersion, upgraded.SettlementVersion, upgraded.Structures, upgraded.StructureContributions,
                upgraded.SocialVersion, upgraded.Relationships, upgraded.Households);
            await store.CheckpointAsync(adjusted, DateTime.UtcNow);
            var reloaded = await store.LoadAsync();

            var restored = reloaded.Citizens.Single(item => item.Id == dead.Id);
            Assert.False(restored.IsAlive);
            Assert.Equal("starvation", restored.DeathCause);
            var retained = reloaded.ScheduledEvents.Single(item => item.Name == "legacy.m0.colliding");
            Assert.Equal(legacy.Order, retained.Order);
            Assert.Equal(legacy.PayloadJson, retained.PayloadJson);
        });
    }

    private static SimulationEngine BuildPhaseSnapshot(string phase)
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        if (phase == "before-survival")
        {
            source.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SurvivalCheckIntervalMinutes - 1));
            return source;
        }
        if (phase == "before-regeneration")
        {
            source.AdvanceUntil(new WorldMinute(WorldCalendar.MinutesPerDay - 1));
            return source;
        }

        var snapshot = source.CreatePersistenceSnapshot();
        var citizen = snapshot.Citizens[0];
        var target = source.World.Tiles.First(tile => tile.Walkable && tile.Coordinate != source.World.StartingSite);
        var resource = source.World.Resources.First(node => node.Type == ResourceType.Food && DeterministicPathfinder.Find(source.World, source.World.StartingSite, node.Coordinate) is { Count: >= 2 });
        ScheduledEventSnapshot replacement;
        if (phase == "mid-eat-travel")
        {
            citizen.Location = target.Coordinate; citizen.CurrentAction = CitizenAction.Eat; citizen.ActionPhase = CitizenActionPhase.TravelToTarget; citizen.ActionTarget = source.World.StartingSite;
            var path = DeterministicPathfinder.Find(source.World, citizen.Location, source.World.StartingSite)!;
            citizen.ActionStartedMinute = WorldMinute.Zero; citizen.ActionCompletesMinute = new WorldMinute(RemainingPathCost(path, source.World));
            replacement = MoveEvent(snapshot, citizen, path, source.World);
        }
        else if (phase == "mid-eat-perform")
        {
            citizen.Location = source.World.StartingSite; citizen.CurrentAction = CitizenAction.Eat; citizen.ActionPhase = CitizenActionPhase.Perform; citizen.ActionTarget = null;
            citizen.ActionStartedMinute = WorldMinute.Zero; citizen.ActionCompletesMinute = new WorldMinute(CitizenSimulationRules.EatDurationMinutes);
            replacement = CompleteEvent(snapshot, citizen, citizen.ActionCompletesMinute.Value);
        }
        else if (phase == "mid-gather-outbound")
        {
            citizen.Location = source.World.StartingSite; citizen.CurrentAction = CitizenAction.GatherFood; citizen.ActionPhase = CitizenActionPhase.TravelToTarget; citizen.ActionTarget = resource.Coordinate; citizen.TargetResourceNodeId = resource.Id;
            var path = DeterministicPathfinder.Find(source.World, citizen.Location, resource.Coordinate)!;
            citizen.ActionStartedMinute = WorldMinute.Zero; citizen.ActionCompletesMinute = new WorldMinute(RemainingPathCost(path, source.World));
            replacement = MoveEvent(snapshot, citizen, path, source.World);
        }
        else if (phase == "mid-gather-perform")
        {
            citizen.Location = resource.Coordinate; citizen.CurrentAction = CitizenAction.GatherFood; citizen.ActionPhase = CitizenActionPhase.Perform; citizen.ActionTarget = null; citizen.TargetResourceNodeId = resource.Id;
            citizen.ActionStartedMinute = WorldMinute.Zero; citizen.ActionCompletesMinute = new WorldMinute(Math.Max(CitizenSimulationRules.MinimumGatherDurationMinutes, CitizenSimulationRules.BaseGatherDurationMinutes - citizen.Skills.Foraging / 100));
            replacement = CompleteEvent(snapshot, citizen, citizen.ActionCompletesMinute.Value);
        }
        else if (phase == "mid-gather-return")
        {
            citizen.Location = resource.Coordinate; citizen.CurrentAction = CitizenAction.GatherFood; citizen.ActionPhase = CitizenActionPhase.ReturnToStockpile; citizen.ActionTarget = source.World.StartingSite; citizen.TargetResourceNodeId = resource.Id; citizen.CarriedResourceType = ResourceType.Food; citizen.CarriedResourceQuantity = 1;
            var path = DeterministicPathfinder.Find(source.World, citizen.Location, source.World.StartingSite)!;
            citizen.ActionStartedMinute = WorldMinute.Zero; citizen.ActionCompletesMinute = new WorldMinute(RemainingPathCost(path, source.World));
            replacement = MoveEvent(snapshot, citizen, path, source.World);
        }
        else throw new ArgumentOutOfRangeException(nameof(phase));

        var events = snapshot.ScheduledEvents.Select(e => e.Order.EntitySortKey == citizen.Id.Value && e.Name == CitizenEventNames.Decision ? replacement : e).ToArray();
        return SimulationEngine.FromPersistenceSnapshot(new SimulationPersistenceSnapshot(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters, events, snapshot.World, snapshot.Citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates, snapshot.Settlement, snapshot.SurvivalVersion));
    }

    private static SimulationEngine CreateScarcityEngine()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
        var snapshot = source.CreatePersistenceSnapshot();
        var citizen = snapshot.Citizens[0];
        citizen.Health = 1000;
        citizen.Needs = new CitizenNeeds(9000, 0, 0, 0);
        citizen.CurrentAction = CitizenAction.Idle;
        citizen.ActionPhase = CitizenActionPhase.Perform;
        citizen.ActionStartedMinute = WorldMinute.Zero;
        citizen.ActionCompletesMinute = new WorldMinute(1_000_000);
        foreach (var state in snapshot.ResourceStates.Where(state => source.World.Resources.Single(node => node.Id == state.ResourceNodeId).Type == ResourceType.Food)) state.CurrentQuantity = 0;
        var events = snapshot.ScheduledEvents.Select(e => e.Order.EntitySortKey == citizen.Id.Value && e.Name == CitizenEventNames.Decision
            ? e with { Name = CitizenEventNames.ActionComplete, Order = new ScheduledEventOrder(new WorldMinute(1_000_000), CitizenEventNames.CompletionPriority, citizen.Id.Value, e.Order.Sequence), PayloadJson = $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}" }
            : e).ToArray();
        var foodlessWorld = new WorldMap(source.World.Seed, source.World.GenerationVersion, source.World.GenerationAttempt, source.World.Configuration,
            source.World.Tiles, source.World.Resources.Select(node => node.Type == ResourceType.Food
                ? new ResourceNode(node.Id, node.Coordinate, node.Type, node.InitialQuantity, node.MaximumQuantity, 0)
                : node), source.World.StartingSite);
        var adjusted = new SimulationPersistenceSnapshot(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion,
            snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters, events, foodlessWorld, snapshot.Citizens,
            snapshot.CitizenGenerationVersion, snapshot.ResourceStates, new SettlementState(0, 0, 0), snapshot.SurvivalVersion);
        return SimulationEngine.FromPersistenceSnapshot(adjusted);
    }

    private static async Task CreateActualM0WithCollidingEventAsync(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
        var options = new DbContextOptionsBuilder<LittleAgesDbContext>().UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(WorldDatabase).Assembly.GetName().Name)).Options;
        await using var context = new LittleAgesDbContext(options);
        await context.Database.OpenConnectionAsync();
        await context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20260912000000_InitialM0");
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "INSERT INTO world_meta (id, world_seed, world_minute, world_schema_version, simulation_rules_version, application_version, world_configuration_json, next_entity_id, next_historical_event_id, next_scheduled_event_sequence, created_utc, last_checkpoint_utc) VALUES (1, '42', 0, '0.1', 'm0-rng1', 'm0-test', '{\"calendar\":\"m0\"}', 256, 512, 9, $now, $now); INSERT INTO scheduled_events (id, due_world_minute, priority, entity_sort_key, sequence, event_name, event_payload_json) VALUES (7, 1250, 10, 256, 7, 'legacy.m0.colliding', '{}'), (8, 1300, 11, 0, 8, 'legacy.m0.other', '{}');";
        command.Parameters.Add(new SqliteParameter("$now", DateTime.UtcNow));
        await command.ExecuteNonQueryAsync();
    }

    private static ScheduledEventSnapshot MoveEvent(SimulationPersistenceSnapshot snapshot, Citizen citizen, IReadOnlyList<TileCoordinate> path, WorldMap world)
    {
        var source = snapshot.ScheduledEvents.Single(e => e.Order.EntitySortKey == citizen.Id.Value && e.Name == CitizenEventNames.Decision);
        return source with { Name = CitizenEventNames.MoveStep, Order = new ScheduledEventOrder(new WorldMinute(StepCost(path[0], path[1], world)), CitizenEventNames.MovementPriority, citizen.Id.Value, source.Order.Sequence), PayloadJson = $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}" };
    }

    private static ScheduledEventSnapshot CompleteEvent(SimulationPersistenceSnapshot snapshot, Citizen citizen, WorldMinute due)
    {
        var source = snapshot.ScheduledEvents.Single(e => e.Order.EntitySortKey == citizen.Id.Value && e.Name == CitizenEventNames.Decision);
        return source with { Name = CitizenEventNames.ActionComplete, Order = new ScheduledEventOrder(due, CitizenEventNames.CompletionPriority, citizen.Id.Value, source.Order.Sequence), PayloadJson = $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}" };
    }

    private static string Canonical(SimulationPersistenceSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.Append(snapshot.Seed.Value).Append('|').Append(snapshot.WorldMinute.Value).Append('|').Append(snapshot.World?.Fingerprint);
        builder.Append('|').Append(snapshot.Settlement is null ? "" : string.Create(CultureInfo.InvariantCulture, $"{snapshot.Settlement.FoodStored},{snapshot.Settlement.WoodStored},{snapshot.Settlement.StoneStored}"));
        foreach (var state in snapshot.ResourceStates) builder.Append(string.Create(CultureInfo.InvariantCulture, $"|r:{state.ResourceNodeId.Value}:{state.CurrentQuantity}"));
        foreach (var citizen in snapshot.Citizens) builder.Append(string.Create(CultureInfo.InvariantCulture, $"|c:{citizen.Id.Value}:{citizen.Location.X},{citizen.Location.Y}:{citizen.Health}:{citizen.CurrentAction}:{citizen.ActionPhase}:{citizen.ActionSequence}:{citizen.ActionCompletesMinute?.Value}:{citizen.TargetResourceNodeId?.Value}:{citizen.CarriedResourceType}:{citizen.CarriedResourceQuantity}:{citizen.NeedsUpdatedMinute}:{citizen.HealthUpdatedMinute}:{citizen.DeathMinute}:{citizen.DeathCause}"));
        foreach (var item in snapshot.ScheduledEvents.OrderBy(e => e.Order)) builder.Append(string.Create(CultureInfo.InvariantCulture, $"|e:{item.Id.Value}:{item.Order.DueWorldMinute.Value}:{item.Order.Priority}:{item.Order.EntitySortKey}:{item.Order.Sequence}:{item.Name}:{item.PayloadJson}"));
        return builder.ToString();
    }

    private static long StepCost(TileCoordinate from, TileCoordinate to, WorldMap world) => checked((long)(from.X == to.X || from.Y == to.Y ? 10 : 14) * world.GetTile(to).MovementCost);
    private static long RemainingPathCost(IReadOnlyList<TileCoordinate> path, WorldMap world)
    {
        var cost = 0L;
        for (var index = 1; index < path.Count; index++) cost = checked(cost + StepCost(path[index - 1], path[index], world));
        return cost;
    }

    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LittleAges-M3-Matrix-Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); var path = Path.Combine(directory, "world.db");
        try { await test(path); } finally { foreach (var suffix in new[] { string.Empty, "-wal", "-shm" }) { var file = path + suffix; if (File.Exists(file)) File.Delete(file); } if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
