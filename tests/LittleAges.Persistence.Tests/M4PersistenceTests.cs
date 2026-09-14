using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M4PersistenceTests
{
    [Fact]
    public async Task CheckpointRoundTripsM4StructureContributionAndCitizenWorkCounters()
    {
        await WithDatabaseAsync(async path =>
        {
            var source = new SimulationEngine(new WorldSeed(42)).CreatePersistenceSnapshot();
            var site = source.World!.Tiles.First(tile => tile.Coordinate != source.World.StartingSite && tile.Buildable && source.World.GetResources(tile.Coordinate).Count == 0).Coordinate;
            var structure = new Structure(new StructureId(source.Counters.NextEntityId), StructureType.Shelter, site, source.WorldMinute.Value, 40, 10, 600)
            {
                DeliveredWood = 40, DeliveredStone = 10, CompletedWork = 25
            };
            var citizen = source.Citizens[0];
            citizen.LifetimeConstructionMinutes = 25;
            var snapshot = new SimulationPersistenceSnapshot(source.Seed, source.WorldMinute, source.WorldSchemaVersion, source.SimulationRulesVersion, source.ApplicationVersion, source.WorldConfiguration, CountersAfterStructures(source.Counters, new[] { structure }), source.ScheduledEvents, source.World, source.Citizens, source.CitizenGenerationVersion, source.ResourceStates, source.Settlement, source.SurvivalVersion, source.SettlementVersion, new[] { structure }, new[] { new StructureContribution(structure.Id, citizen.Id, 25, 40, 10) });
            await using var database = await WorldDatabase.OpenAsync(path);
            await database.CreateCheckpointStore().CheckpointAsync(snapshot, DateTime.UtcNow);
            var loaded = await database.CreateCheckpointStore().LoadAsync();
            Assert.Equal(1, loaded.SettlementVersion);
            Assert.Equal(structure.Id, Assert.Single(loaded.Structures).Id);
            Assert.Equal(25, Assert.Single(loaded.StructureContributions).ConstructionWork);
            Assert.Equal(25, loaded.Citizens.Single(x => x.Id == citizen.Id).LifetimeConstructionMinutes);
        });
    }

    [Fact]
    public async Task CheckpointRoundTripsMultipleStructuresHomesContributionsAndCounters()
    {
        await WithDatabaseAsync(async path =>
        {
            var source = CreateStructuredSnapshot();
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(source, DateTime.UtcNow);
            var loaded = await store.LoadAsync();

            Assert.Equal(source.Structures.Select(StructureKey), loaded.Structures.Select(StructureKey));
            Assert.Equal(source.StructureContributions.Select(ContributionKey), loaded.StructureContributions.Select(ContributionKey));
            Assert.Equal(source.Citizens.Select(CitizenM4Key), loaded.Citizens.Select(CitizenM4Key));
            Assert.Equal(source.Settlement, loaded.Settlement);
        });
    }

    [Fact]
    public async Task FailedM4CheckpointRetainsPriorStructuresAndContributions()
    {
        await WithDatabaseAsync(async path =>
        {
            var original = CreateStructuredSnapshot();
            var changedSettlement = new SettlementState(401, original.Settlement!.WoodStored, original.Settlement.StoneStored, original.Settlement.BaseStorageCapacity, original.Settlement.DemandUpdatedMinute, original.Settlement.ExposureConsequencesStartMinute);
            var replacement = new SimulationPersistenceSnapshot(original.Seed, original.WorldMinute, original.WorldSchemaVersion, original.SimulationRulesVersion, original.ApplicationVersion, original.WorldConfiguration, original.Counters, original.ScheduledEvents, original.World, original.Citizens, original.CitizenGenerationVersion, original.ResourceStates, changedSettlement, original.SurvivalVersion, original.SettlementVersion, original.Structures, original.StructureContributions);
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(original, DateTime.UtcNow);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.CheckpointAsync(replacement, DateTime.UtcNow, CheckpointFailurePoint.AfterRowsWritten));
            var loaded = await store.LoadAsync();
            Assert.Equal(original.Settlement, loaded.Settlement);
            Assert.Equal(original.Structures.Select(StructureKey), loaded.Structures.Select(StructureKey));
            Assert.Equal(original.StructureContributions.Select(ContributionKey), loaded.StructureContributions.Select(ContributionKey));
        });
    }

    [Theory]
    [InlineData(StructureType.Shelter, 41, 10, 600, 0, 0, 0)]
    [InlineData(StructureType.Shelter, 40, 10, 600, 39, 10, 600)]
    [InlineData(StructureType.Shelter, 40, 10, 600, 40, 11, 600)]
    [InlineData(StructureType.Shelter, 40, 10, 600, 40, 10, 601)]
    public void SnapshotRejectsNonCanonicalStructureState(StructureType type, int requiredWood, int requiredStone, int requiredWork, int deliveredWood, int deliveredStone, int completedWork)
    {
        var source = CreateStructuredSnapshot();
        var original = source.Structures.Single(item => item.Id.Value == 23);
        var replacement = new Structure(original.Id, type, original.Location, original.ConstructionStartedMinute, requiredWood, requiredStone, requiredWork)
        {
            Status = StructureStatus.Complete,
            CompletedMinute = 1,
            DeliveredWood = deliveredWood,
            DeliveredStone = deliveredStone,
            CompletedWork = completedWork
        };

        Assert.Throws<ArgumentException>(() => new SimulationPersistenceSnapshot(source.Seed, source.WorldMinute, source.WorldSchemaVersion, source.SimulationRulesVersion, source.ApplicationVersion, source.WorldConfiguration, source.Counters, source.ScheduledEvents, source.World, source.Citizens, source.CitizenGenerationVersion, source.ResourceStates, source.Settlement, source.SurvivalVersion, source.SettlementVersion, source.Structures.Select(item => item.Id == original.Id ? replacement : item).ToArray(), source.StructureContributions));
    }

    [Fact]
    public async Task CheckpointRoundTripsWaitingStorageHaulingAndBuildingPhases()
    {
        await WithDatabaseAsync(async path =>
        {
            var source = CreateStructuredSnapshot();
            var project = source.Structures.Single(item => item.Id.Value == 23);
            var builder = source.Citizens[0];
            builder.CurrentAction = CitizenAction.Build; builder.ActionPhase = CitizenActionPhase.Perform; builder.TargetStructureId = project.Id; builder.ActionStartedMinute = WorldMinute.Zero; builder.ActionCompletesMinute = new WorldMinute(1);
            var waiting = source.Citizens[1];
            waiting.CurrentAction = CitizenAction.GatherFood; waiting.ActionPhase = CitizenActionPhase.WaitingForStorage; waiting.TargetResourceNodeId = source.World!.Resources.First(item => item.Type == ResourceType.Food).Id; waiting.CarriedResourceType = ResourceType.Food; waiting.CarriedResourceQuantity = 1; waiting.ActionStartedMinute = WorldMinute.Zero; waiting.ActionCompletesMinute = new WorldMinute(1);
            var hauler = source.Citizens[2];
            var pathToProject = DeterministicPathfinder.Find(source.World!, hauler.Location, project.Location)!;
            Assert.True(pathToProject.Count >= 2);
            hauler.CurrentAction = CitizenAction.HaulConstruction; hauler.ActionPhase = CitizenActionPhase.TransportToConstruction; hauler.TargetStructureId = project.Id; hauler.ActionTarget = project.Location; hauler.CarriedResourceType = ResourceType.Wood; hauler.CarriedResourceQuantity = 1; hauler.ActionStartedMinute = WorldMinute.Zero; hauler.ActionCompletesMinute = new WorldMinute(RemainingPathCost(pathToProject, source.World));
            var events = source.ScheduledEvents.Select(item => item.Order.EntitySortKey switch
            {
                1 when item.Name == CitizenEventNames.Decision => Completion(item, builder),
                2 when item.Name == CitizenEventNames.Decision => Completion(item, waiting),
                3 when item.Name == CitizenEventNames.Decision => Move(item, hauler, pathToProject, source.World!),
                _ => item
            }).ToArray();
            source = new SimulationPersistenceSnapshot(source.Seed, source.WorldMinute, source.WorldSchemaVersion, source.SimulationRulesVersion, source.ApplicationVersion, source.WorldConfiguration, source.Counters, events, source.World, source.Citizens, source.CitizenGenerationVersion, source.ResourceStates, source.Settlement, source.SurvivalVersion, source.SettlementVersion, source.Structures, source.StructureContributions);
            await using var database = await WorldDatabase.OpenAsync(path);
            await database.CreateCheckpointStore().CheckpointAsync(source, DateTime.UtcNow);
            var loaded = await database.CreateCheckpointStore().LoadAsync();
            Assert.Equal(source.Citizens.Select(CitizenM4Key), loaded.Citizens.Select(CitizenM4Key));
            Assert.Equal(source.Citizens.Take(3).Select(ActionKey), loaded.Citizens.Take(3).Select(ActionKey));
            Assert.Equal(source.ScheduledEvents.Where(item => item.Order.EntitySortKey is 1 or 2 or 3), loaded.ScheduledEvents.Where(item => item.Order.EntitySortKey is 1 or 2 or 3));
        });
    }

    [Fact]
    public async Task M3UpgradeSetsM4BoundariesAndDemandEvent()
    {
        await WithDatabaseAsync(async path =>
        {
            var m3 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion).CreatePersistenceSnapshot();
            m3.Settlement!.FoodStored = 900;
            var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
            var options = new DbContextOptionsBuilder<LittleAgesDbContext>().UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(WorldDatabase).Assembly.GetName().Name)).Options;
            await using (var context = new LittleAgesDbContext(options))
            {
                await context.Database.MigrateAsync();
                await new WorldCheckpointStore(context).CheckpointAsync(m3, DateTime.UtcNow);
            }
            await using var database = await WorldDatabase.OpenAsync(path);
            var loaded = await database.CreateCheckpointStore().LoadAsync();
            Assert.Equal(SimulationEngine.CurrentSimulationRulesVersion, loaded.SimulationRulesVersion);
            Assert.Equal(1, loaded.SettlementVersion);
            Assert.Equal(900, loaded.Settlement!.BaseStorageCapacity);
            var demand = Assert.Single(loaded.ScheduledEvents, x => x.Name == CitizenEventNames.SettlementEvaluateDemand);
            Assert.Equal(loaded.WorldMinute.Add(CitizenSimulationRules.SettlementDemandIntervalMinutes), demand.Order.DueWorldMinute);
            Assert.Equal(CitizenEventNames.SettlementDemandPriority, demand.Order.Priority);
            Assert.Equal(0, demand.Order.EntitySortKey);
            Assert.Equal("{\"version\":1}", demand.PayloadJson);
        });
    }

    [Fact]
    public async Task M3ToM4UpgradeRollsBackThenRetriesWithExactM3Preservation()
    {
        await WithDatabaseAsync(async path =>
        {
            var m3 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion).CreatePersistenceSnapshot();
            m3.Settlement!.FoodStored = 900;
            m3.Settlement.WoodStored = 17;
            var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
            var options = new DbContextOptionsBuilder<LittleAgesDbContext>().UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(WorldDatabase).Assembly.GetName().Name)).Options;
            await using (var context = new LittleAgesDbContext(options))
            {
                await context.Database.MigrateAsync();
                await new WorldCheckpointStore(context).CheckpointAsync(m3, DateTime.UtcNow);
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => WorldDatabase.OpenAsync(path, new WorldDatabaseOpenOptions(null, null, null, null, M4UpgradeFailurePoint.AfterRowsWritten)));
            await using (var check = new SqliteConnection(connectionString))
            {
                await check.OpenAsync();
                await using var command = check.CreateCommand();
                command.CommandText = "SELECT settlement_version, (SELECT COUNT(*) FROM structures), (SELECT COUNT(*) FROM structure_contributions), (SELECT COUNT(*) FROM scheduled_events WHERE event_name = 'settlement.evaluate-demand.v1') FROM world_meta;";
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(0, reader.GetInt32(0));
                Assert.Equal(0L, reader.GetInt64(1));
                Assert.Equal(0L, reader.GetInt64(2));
                Assert.Equal(0L, reader.GetInt64(3));
            }

            await using var retried = await WorldDatabase.OpenAsync(path);
            var upgraded = await retried.CreateCheckpointStore().LoadAsync();
            Assert.Equal(m3.Seed, upgraded.Seed);
            Assert.Equal(m3.WorldMinute, upgraded.WorldMinute);
            Assert.Equal(m3.World!.Fingerprint, upgraded.World!.Fingerprint);
            Assert.Equal(m3.Citizens, upgraded.Citizens);
            Assert.Equal(m3.ResourceStates, upgraded.ResourceStates);
            Assert.Equal(m3.Settlement!.FoodStored, upgraded.Settlement!.FoodStored);
            Assert.Equal(m3.Settlement.WoodStored, upgraded.Settlement.WoodStored);
            Assert.Equal(Math.Max(CitizenSimulationRules.BaseStorageCapacity, 917), upgraded.Settlement.BaseStorageCapacity);
            Assert.Equal(m3.WorldMinute.Value, upgraded.Settlement.DemandUpdatedMinute);
            Assert.Equal(m3.WorldMinute.Add(CitizenSimulationRules.ExposureGraceDurationMinutes).Value, upgraded.Settlement.ExposureConsequencesStartMinute);
            Assert.Equal(m3.Counters.NextScheduledEventSequence + 1, upgraded.Counters.NextScheduledEventSequence);
            Assert.Equal(m3.ScheduledEvents.OrderBy(x => x.Order), upgraded.ScheduledEvents.Where(x => x.Name != CitizenEventNames.SettlementEvaluateDemand).OrderBy(x => x.Order));
            Assert.Single(upgraded.ScheduledEvents, x => x.Name == CitizenEventNames.SettlementEvaluateDemand);
        });
    }

    [Theory]
    [InlineData(CitizenAction.Eat)]
    [InlineData(CitizenAction.Rest)]
    [InlineData(CitizenAction.GatherFood)]
    [InlineData(CitizenAction.Explore)]
    [InlineData(CitizenAction.Wander)]
    [InlineData(CitizenAction.Idle)]
    public async Task M3ToM4UpgradePreservesInFlightActionAndEvent(CitizenAction action)
    {
        await WithDatabaseAsync(async path =>
        {
            var m3 = CreateM3InFlightSnapshot(action);
            var expectedCitizen = m3.Citizens.Single(item => item.Id.Value == 1);
            var expectedEvent = m3.ScheduledEvents.Single(item => item.Order.EntitySortKey == 1 && item.Name != CitizenEventNames.SurvivalCheck);
            var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
            var options = new DbContextOptionsBuilder<LittleAgesDbContext>().UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(WorldDatabase).Assembly.GetName().Name)).Options;
            await using (var context = new LittleAgesDbContext(options))
            {
                await context.Database.MigrateAsync();
                await new WorldCheckpointStore(context).CheckpointAsync(m3, DateTime.UtcNow);
            }

            await using var database = await WorldDatabase.OpenAsync(path);
            var upgraded = await database.CreateCheckpointStore().LoadAsync();
            var actualCitizen = upgraded.Citizens.Single(item => item.Id.Value == 1);
            Assert.Equal(ActionKey(expectedCitizen), ActionKey(actualCitizen));
            Assert.Equal(expectedEvent, upgraded.ScheduledEvents.Single(item => item.Id == expectedEvent.Id));
            Assert.Equal(m3.Counters.NextScheduledEventSequence + 1, upgraded.Counters.NextScheduledEventSequence);
        });
    }

    [Theory]
    [InlineData("settlement0-with-structures", "PRAGMA ignore_check_constraints = ON; UPDATE world_meta SET simulation_rules_version = 'm3-rng1-survival1', settlement_version = 0;")]
    [InlineData("missing-demand", "DELETE FROM scheduled_events WHERE event_name = 'settlement.evaluate-demand.v1';")]
    [InlineData("invalid-structure-type", "PRAGMA ignore_check_constraints = ON; UPDATE structures SET type = 99 WHERE id = 21;")]
    [InlineData("invalid-structure-status", "PRAGMA ignore_check_constraints = ON; UPDATE structures SET status = 99 WHERE id = 21;")]
    [InlineData("invalid-structure-condition", "PRAGMA ignore_check_constraints = ON; UPDATE structures SET condition = 5 WHERE id = 21;")]
    [InlineData("invalid-structure-cost", "PRAGMA ignore_check_constraints = ON; UPDATE structures SET required_work = 0 WHERE id = 21;")]
    [InlineData("wrong-shelter-definition", "PRAGMA ignore_check_constraints = ON; UPDATE structures SET required_wood = 41 WHERE id = 21;")]
    [InlineData("wrong-stockpile-definition", "PRAGMA ignore_check_constraints = ON; UPDATE structures SET required_stone = 31 WHERE id = 22;")]
    [InlineData("wrong-workshop-definition", "PRAGMA ignore_check_constraints = ON; UPDATE structures SET required_work = 1201 WHERE id = 23;")]
    [InlineData("invalid-structure-material", "PRAGMA ignore_check_constraints = ON; UPDATE structures SET delivered_wood = required_wood + 1 WHERE id = 21;")]
    [InlineData("invalid-structure-work", "PRAGMA ignore_check_constraints = ON; UPDATE structures SET completed_work = required_work + 1 WHERE id = 21;")]
    [InlineData("complete-missing-wood", "PRAGMA ignore_check_constraints = ON; UPDATE structures SET delivered_wood = required_wood - 1 WHERE id = 21;")]
    [InlineData("complete-missing-stone", "PRAGMA ignore_check_constraints = ON; UPDATE structures SET delivered_stone = required_stone - 1 WHERE id = 22;")]
    [InlineData("complete-missing-work", "PRAGMA ignore_check_constraints = ON; UPDATE structures SET completed_work = required_work - 1 WHERE id = 21;")]
    [InlineData("complete-overdelivered-wood", "PRAGMA ignore_check_constraints = ON; UPDATE structures SET delivered_wood = required_wood + 1 WHERE id = 21;")]
    [InlineData("complete-overdelivered-stone", "PRAGMA ignore_check_constraints = ON; UPDATE structures SET delivered_stone = required_stone + 1 WHERE id = 22;")]
    [InlineData("complete-overdelivered-work", "PRAGMA ignore_check_constraints = ON; UPDATE structures SET completed_work = required_work + 1 WHERE id = 21;")]
    [InlineData("unknown-contributor", "PRAGMA foreign_keys = OFF; INSERT INTO structure_contributions (structure_id, citizen_id, construction_work, wood_delivered, stone_delivered) VALUES (23, 999999, 0, 0, 0);")]
    [InlineData("contribution-aggregate-mismatch", "PRAGMA ignore_check_constraints = ON; UPDATE structure_contributions SET construction_work = construction_work + 1 WHERE structure_id = 23 AND citizen_id = 1;")]
    [InlineData("home-workshop", "UPDATE citizens SET home_structure_id = 22 WHERE id = 1;")]
    [InlineData("home-under-construction", "UPDATE citizens SET home_structure_id = 23 WHERE id = 1;")]
    [InlineData("home-unknown", "UPDATE citizens SET home_structure_id = 999999 WHERE id = 1;")]
    [InlineData("shelter-occupancy", "UPDATE citizens SET home_structure_id = 21 WHERE id BETWEEN 1 AND 5;")]
    [InlineData("storage-overflow", "UPDATE settlement_state SET food_stored = 5000 WHERE id = 1;")]
    [InlineData("transit-over-requirement", "PRAGMA ignore_check_constraints = ON; UPDATE citizens SET current_action = 10, action_phase = 5, target_structure_id = 23, carried_resource_type = 2, carried_resource_quantity = 999 WHERE id = 1;")]
    public async Task M4CorruptionIsRejectedWithoutRepair(string _, string mutation)
    {
        await WithDatabaseAsync(async path =>
        {
            await using var database = await WorldDatabase.OpenAsync(path);
            await database.CreateCheckpointStore().CheckpointAsync(CreateStructuredSnapshot(), DateTime.UtcNow);
            await using var command = database.Context.Database.GetDbConnection().CreateCommand();
            command.CommandText = mutation;
            await command.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => database.CreateCheckpointStore().LoadAsync());
            command.CommandText = "SELECT COUNT(*) FROM world_meta;";
            Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
        });
    }

    private static SimulationPersistenceSnapshot CreateStructuredSnapshot()
    {
        var baseline = new SimulationEngine(new WorldSeed(42)).CreatePersistenceSnapshot();
        var sites = baseline.World!.Tiles.Where(tile => tile.Coordinate != baseline.World.StartingSite && tile.Buildable && baseline.World.GetResources(tile.Coordinate).Count == 0 && DeterministicPathfinder.Find(baseline.World, baseline.World.StartingSite, tile.Coordinate) is { Count: >= 2 }).Select(tile => tile.Coordinate).Take(3).ToArray();
        Assert.Equal(3, sites.Length);
        var shelter = Complete(new Structure(new StructureId(baseline.Counters.NextEntityId), StructureType.Shelter, sites[0], 0, 40, 10, 600), 40, 10, 600);
        var stockpile = Complete(new Structure(new StructureId(baseline.Counters.NextEntityId + 1), StructureType.Stockpile, sites[1], 0, 60, 30, 900), 60, 30, 900);
        var project = new Structure(new StructureId(baseline.Counters.NextEntityId + 2), StructureType.Workshop, sites[2], 0, 80, 50, 1200) { DeliveredWood = 4, DeliveredStone = 3 };
        baseline.Citizens[0].HomeStructureId = shelter.Id;
        baseline.Citizens[0].LifetimeConstructionMinutes = 30;
        baseline.Citizens[1].LifetimeHaulingMinutes = 12;
        baseline.Citizens[2].LifetimeForagingMinutes = 7;
        var contributions = new[]
        {
            new StructureContribution(shelter.Id, baseline.Citizens[0].Id, 600, 40, 10),
            new StructureContribution(stockpile.Id, baseline.Citizens[1].Id, 900, 60, 30),
            new StructureContribution(project.Id, baseline.Citizens[0].Id, 0, 4, 3)
        };
        return new SimulationPersistenceSnapshot(baseline.Seed, baseline.WorldMinute, baseline.WorldSchemaVersion, baseline.SimulationRulesVersion, baseline.ApplicationVersion, baseline.WorldConfiguration, CountersAfterStructures(baseline.Counters, new[] { shelter, stockpile, project }), baseline.ScheduledEvents, baseline.World, baseline.Citizens, baseline.CitizenGenerationVersion, baseline.ResourceStates, baseline.Settlement, baseline.SurvivalVersion, baseline.SettlementVersion, new[] { shelter, stockpile, project }, contributions);
    }

    private static Structure Complete(Structure structure, int wood, int stone, int work)
    {
        structure.DeliveredWood = wood;
        structure.DeliveredStone = stone;
        structure.CompletedWork = work;
        structure.Status = StructureStatus.Complete;
        structure.CompletedMinute = 0;
        return structure;
    }

    private static DeterministicCountersSnapshot CountersAfterStructures(DeterministicCountersSnapshot counters, Structure[] structures)
    {
        var nextEntityId = structures.Length == 0 ? counters.NextEntityId : Math.Max(counters.NextEntityId, checked(structures.Max(x => x.Id.Value) + 1));
        return counters with { NextEntityId = nextEntityId };
    }

    private static SimulationPersistenceSnapshot CreateM3InFlightSnapshot(CitizenAction action)
    {
        var baseline = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion).CreatePersistenceSnapshot();
        var citizen = baseline.Citizens[0];
        citizen.CurrentAction = action;
        citizen.ActionStartedMinute = WorldMinute.Zero;
        ScheduledEventSnapshot replacement;
        if (action is CitizenAction.Idle or CitizenAction.Rest or CitizenAction.Eat)
        {
            citizen.ActionPhase = CitizenActionPhase.Perform;
            citizen.ActionTarget = null;
            citizen.ActionCompletesMinute = new WorldMinute(action == CitizenAction.Eat ? CitizenSimulationRules.EatDurationMinutes : CitizenSimulationRules.RestDurationMinutes);
            replacement = Completion(baseline.ScheduledEvents.Single(item => item.Order.EntitySortKey == citizen.Id.Value && item.Name == CitizenEventNames.Decision), citizen);
        }
        else if (action is CitizenAction.Wander or CitizenAction.Explore)
        {
            var target = baseline.World!.Tiles.Select(tile => (tile.Coordinate, Path: DeterministicPathfinder.Find(baseline.World, citizen.Location, tile.Coordinate))).First(item => item.Path is { Count: >= 2 });
            citizen.ActionPhase = CitizenActionPhase.TravelToTarget;
            citizen.ActionTarget = target.Coordinate;
            citizen.ActionCompletesMinute = new WorldMinute(RemainingPathCost(target.Path!, baseline.World));
            replacement = Move(baseline.ScheduledEvents.Single(item => item.Order.EntitySortKey == citizen.Id.Value && item.Name == CitizenEventNames.Decision), citizen, target.Path!, baseline.World);
        }
        else
        {
            var resource = baseline.World!.Resources.First(item => item.Type == ResourceType.Food && DeterministicPathfinder.Find(baseline.World, item.Coordinate, baseline.World.StartingSite) is { Count: >= 2 });
            var path = DeterministicPathfinder.Find(baseline.World, resource.Coordinate, baseline.World.StartingSite)!;
            citizen.Location = resource.Coordinate;
            citizen.ActionPhase = CitizenActionPhase.ReturnToStockpile;
            citizen.ActionTarget = baseline.World.StartingSite;
            citizen.TargetResourceNodeId = resource.Id;
            citizen.CarriedResourceType = ResourceType.Food;
            citizen.CarriedResourceQuantity = 1;
            citizen.ActionCompletesMinute = new WorldMinute(RemainingPathCost(path, baseline.World));
            replacement = Move(baseline.ScheduledEvents.Single(item => item.Order.EntitySortKey == citizen.Id.Value && item.Name == CitizenEventNames.Decision), citizen, path, baseline.World);
        }
        var events = baseline.ScheduledEvents.Select(item => item.Order.EntitySortKey == citizen.Id.Value && item.Name == CitizenEventNames.Decision ? replacement : item).ToArray();
        return new SimulationPersistenceSnapshot(baseline.Seed, baseline.WorldMinute, baseline.WorldSchemaVersion, baseline.SimulationRulesVersion, baseline.ApplicationVersion, baseline.WorldConfiguration, baseline.Counters, events, baseline.World, baseline.Citizens, baseline.CitizenGenerationVersion, baseline.ResourceStates, baseline.Settlement, baseline.SurvivalVersion);
    }

    private static ScheduledEventSnapshot Completion(ScheduledEventSnapshot original, Citizen citizen) => original with { Name = CitizenEventNames.ActionComplete, Order = new ScheduledEventOrder(citizen.ActionCompletesMinute!.Value, CitizenEventNames.CompletionPriority, citizen.Id.Value, original.Order.Sequence), PayloadJson = $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}" };
    private static ScheduledEventSnapshot Move(ScheduledEventSnapshot original, Citizen citizen, IReadOnlyList<TileCoordinate> path, WorldMap world) => original with { Name = CitizenEventNames.MoveStep, Order = new ScheduledEventOrder(new WorldMinute(StepCost(path[0], path[1], world)), CitizenEventNames.MovementPriority, citizen.Id.Value, original.Order.Sequence), PayloadJson = $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}" };
    private static long StepCost(TileCoordinate from, TileCoordinate to, WorldMap world) => checked((long)(from.X == to.X || from.Y == to.Y ? 10 : 14) * world.GetTile(to).MovementCost);
    private static long RemainingPathCost(IReadOnlyList<TileCoordinate> path, WorldMap world) => Enumerable.Range(1, path.Count - 1).Sum(index => StepCost(path[index - 1], path[index], world));

    private static string StructureKey(Structure structure) => $"{structure.Id.Value}:{(int)structure.Type}:{(int)structure.Status}:{structure.Condition}:{structure.Location.X},{structure.Location.Y}:{structure.ConstructionStartedMinute}:{structure.CompletedMinute}:{structure.RequiredWood}:{structure.DeliveredWood}:{structure.RequiredStone}:{structure.DeliveredStone}:{structure.RequiredWork}:{structure.CompletedWork}";
    private static string ContributionKey(StructureContribution value) => $"{value.StructureId.Value}:{value.CitizenId.Value}:{value.ConstructionWork}:{value.WoodDelivered}:{value.StoneDelivered}";
    private static string CitizenM4Key(Citizen citizen) => $"{citizen.Id.Value}:{citizen.HomeStructureId?.Value}:{citizen.TargetStructureId?.Value}:{citizen.LifetimeForagingMinutes}:{citizen.LifetimeWoodcuttingMinutes}:{citizen.LifetimeStoneworkingMinutes}:{citizen.LifetimeConstructionMinutes}:{citizen.LifetimeHaulingMinutes}";
    private static string ActionKey(Citizen citizen) => $"{citizen.Id.Value}:{(int)citizen.CurrentAction}:{(int)citizen.ActionPhase}:{citizen.ActionSequence}:{citizen.ActionStartedMinute?.Value}:{citizen.ActionCompletesMinute?.Value}:{citizen.ActionTarget?.X},{citizen.ActionTarget?.Y}:{citizen.TargetResourceNodeId?.Value}:{citizen.TargetStructureId?.Value}:{citizen.CarriedResourceType}:{citizen.CarriedResourceQuantity}";

    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LittleAges-M4-Persistence-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "world.db");
        try { await test(path); }
        finally { foreach (var suffix in new[] { string.Empty, "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
