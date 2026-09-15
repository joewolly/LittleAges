using System.Reflection;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M5PersistenceMatrixTests
{
    [Fact]
    public async Task PopulatedSparseRelationshipAndHouseholdSqliteCheckpointRoundTripsExactly()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LittleAges-M5-Matrix", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "world.db");
        Directory.CreateDirectory(directory);
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
            var citizens = Citizens(engine);
            var first = citizens[1];
            var second = citizens[2];
            var relationship = new RelationshipState(first.Id, second.Id, 7_000, 6_000, 5_000, 0, 0, 1);
            typeof(SimulationEngine).GetMethod("TryFormPartnership", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [first, second, relationship]);
            Relationships(engine).Add((first.Id.Value, second.Id.Value), relationship);
            var source = engine.CreatePersistenceSnapshot();

            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(source, DateTime.UtcNow);
            var loaded = await store.LoadAsync();
            var reloaded = SimulationEngine.FromPersistenceSnapshot(loaded);

            Assert.Equal(source.Relationships, loaded.Relationships);
            Assert.Equal(source.Households.Select(HouseholdKey), loaded.Households.Select(HouseholdKey));
            Assert.Equal(engine.SocialFingerprint, reloaded.SocialFingerprint);
            Assert.Equal(source.Counters.NextHistoricalEventId, loaded.Counters.NextHistoricalEventId);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SocializeHousingRepairCheckpointReloadKeepsOneActionFlowAndOneSurvivalEvent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LittleAges-M5-RestHome", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "world.db");
        Directory.CreateDirectory(directory);
        try
        {
            var engine = PrepareRestHomeScenario();
            var first = Citizens(engine)[1];
            var second = Citizens(engine)[2];
            var snapshot = engine.CreatePersistenceSnapshot();
            var repairedCitizenEvents = snapshot.ScheduledEvents.Where(item => item.Order.EntitySortKey == second.Id.Value).ToArray();
            Assert.Single(repairedCitizenEvents, item => item.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete);
            Assert.Single(repairedCitizenEvents, item => item.Name == CitizenEventNames.SurvivalCheck);
            Assert.Equal(CitizenAction.None, second.CurrentAction);
            Assert.Equal(CitizenActionPhase.None, second.ActionPhase);
            Assert.Equal(first.Id, second.PartnerId);

            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(snapshot, new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc));
            var loaded = await store.LoadAsync();
            var loadedSecondEvents = loaded.ScheduledEvents.Where(item => item.Order.EntitySortKey == second.Id.Value).ToArray();
            Assert.Single(loadedSecondEvents, item => item.Name is CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete);
            Assert.Single(loadedSecondEvents, item => item.Name == CitizenEventNames.SurvivalCheck);
            Assert.Equal(snapshot.Citizens, loaded.Citizens);
            Assert.Equal(snapshot.ScheduledEvents, loaded.ScheduledEvents);
            Assert.Equal(snapshot.Households.Select(HouseholdKey), loaded.Households.Select(HouseholdKey));
            _ = SimulationEngine.FromPersistenceSnapshot(loaded);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PopulatedM5CheckpointRollsBackAfterRowsWrittenAndRetriesDifferentSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LittleAges-M5-Rollback", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "world.db");
        Directory.CreateDirectory(directory);
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
            var citizens = Citizens(engine);
            var first = citizens[1];
            var second = citizens[2];
            var relationship = new RelationshipState(first.Id, second.Id, 7_000, 6_000, 5_000, 0, 0, 1);
            typeof(SimulationEngine).GetMethod("TryFormPartnership", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [first, second, relationship]);
            Relationships(engine).Add((first.Id.Value, second.Id.Value), relationship);
            var sites = engine.World.Tiles
                .Where(tile => tile.Buildable && tile.Coordinate != engine.World.StartingSite && engine.World.GetResources(tile.Coordinate).Count == 0)
                .Select(tile => tile.Coordinate)
                .Take(2)
                .ToArray();
            Assert.Equal(2, sites.Length);
            var counters = Counters(engine);
            var shelter = CompletedShelter(counters.AllocateStructureId(), sites[0]);
            Structures(engine).Add(shelter.Id.Value, shelter);
            StructureContributions(engine).Add((shelter.Id.Value, first.Id.Value), new StructureContribution(shelter.Id, first.Id, shelter.CompletedWork, shelter.DeliveredWood, shelter.DeliveredStone));
            var household = Assert.Single(Households(engine).Values);
            household.DwellingStructureId = shelter.Id;
            first.HomeStructureId = shelter.Id;
            second.HomeStructureId = shelter.Id;
            var snapshotA = engine.CreatePersistenceSnapshot();

            var changedEngine = SimulationEngine.FromPersistenceSnapshot(snapshotA);
            changedEngine.AdvanceUntil(new WorldMinute(WorldCalendar.MinutesPerDay));
            var changedCounters = Counters(changedEngine);
            var stockpile = CompletedStockpile(changedCounters.AllocateStructureId(), sites[1]);
            Structures(changedEngine).Add(stockpile.Id.Value, stockpile);
            StructureContributions(changedEngine).Add((stockpile.Id.Value, first.Id.Value), new StructureContribution(stockpile.Id, first.Id, stockpile.CompletedWork, stockpile.DeliveredWood, stockpile.DeliveredStone));
            var snapshotB = changedEngine.CreatePersistenceSnapshot();
            Assert.NotEqual(SimulationEngine.FromPersistenceSnapshot(snapshotA).SocialFingerprint, SimulationEngine.FromPersistenceSnapshot(snapshotB).SocialFingerprint);
            Assert.NotEqual(snapshotA.WorldMinute, snapshotB.WorldMinute);
            Assert.NotEqual(snapshotA.Counters, snapshotB.Counters);
            Assert.NotEqual(snapshotA.ScheduledEvents, snapshotB.ScheduledEvents);
            Assert.NotEqual(snapshotA.Citizens, snapshotB.Citizens);
            Assert.NotEqual(snapshotA.Structures.Count, snapshotB.Structures.Count);
            Assert.NotEqual(string.Join("|", snapshotA.Structures.Select(StructureKey)), string.Join("|", snapshotB.Structures.Select(StructureKey)));
            Assert.Equal(StructureType.Shelter, Assert.Single(snapshotA.Structures).Type);
            Assert.Contains(snapshotB.Structures, structure => structure.Type == StructureType.Stockpile);
            Assert.NotEmpty(snapshotA.Citizens);
            Assert.NotEmpty(snapshotB.Citizens);
            Assert.NotEmpty(snapshotA.ScheduledEvents);
            Assert.NotEmpty(snapshotB.ScheduledEvents);
            Assert.NotEmpty(snapshotA.ResourceStates);
            Assert.NotEmpty(snapshotB.ResourceStates);
            Assert.NotNull(snapshotA.Settlement);
            Assert.NotNull(snapshotB.Settlement);
            Assert.True(snapshotA.Counters.NextEntityId > 0);
            Assert.True(snapshotB.Counters.NextEntityId > 0);
            Assert.NotEmpty(snapshotA.StructureContributions);
            Assert.NotEmpty(snapshotB.StructureContributions);
            Assert.NotEmpty(snapshotA.Relationships);
            Assert.NotEmpty(snapshotB.Relationships);
            Assert.NotEmpty(snapshotA.Households);
            Assert.NotEmpty(snapshotB.Households);
            Assert.Contains(snapshotA.Citizens, citizen => citizen.PartnerId is not null && citizen.HouseholdId is not null);
            Assert.Contains(snapshotB.Citizens, citizen => citizen.PartnerId is not null && citizen.HouseholdId is not null);

            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            var checkpointA = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);
            var checkpointB = new DateTime(2026, 9, 15, 0, 2, 0, DateTimeKind.Utc);
            await store.CheckpointAsync(snapshotA, checkpointA);
            Assert.Equal(checkpointA, (await database.Context.WorldMeta.AsNoTracking().SingleAsync()).LastCheckpointUtc);

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.CheckpointAsync(snapshotB, new DateTime(2026, 9, 15, 0, 1, 0, DateTimeKind.Utc), CheckpointFailurePoint.AfterRowsWritten));

            var restoredA = await store.LoadAsync();
            AssertSnapshotEqual(snapshotA, restoredA);
            Assert.Equal(checkpointA, (await database.Context.WorldMeta.AsNoTracking().SingleAsync()).LastCheckpointUtc);

            await store.CheckpointAsync(snapshotB, checkpointB);
            var restoredB = await store.LoadAsync();
            AssertSnapshotEqual(snapshotB, restoredB);
            Assert.Equal(checkpointB, (await database.Context.WorldMeta.AsNoTracking().SingleAsync()).LastCheckpointUtc);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertSnapshotEqual(SimulationPersistenceSnapshot expected, SimulationPersistenceSnapshot actual)
    {
        Assert.Equal(expected.Seed, actual.Seed);
        Assert.Equal(expected.WorldMinute, actual.WorldMinute);
        Assert.Equal(expected.WorldSchemaVersion, actual.WorldSchemaVersion);
        Assert.Equal(expected.SimulationRulesVersion, actual.SimulationRulesVersion);
        Assert.Equal(expected.ApplicationVersion, actual.ApplicationVersion);
        Assert.Equal(expected.WorldConfiguration, actual.WorldConfiguration);
        Assert.Equal(expected.Counters, actual.Counters);
        Assert.Equal(expected.ScheduledEvents, actual.ScheduledEvents);
        Assert.Equal(expected.World!.Fingerprint, actual.World!.Fingerprint);
        Assert.Equal(expected.CitizenGenerationVersion, actual.CitizenGenerationVersion);
        Assert.Equal(expected.Citizens, actual.Citizens);
        Assert.Equal(expected.ResourceStates, actual.ResourceStates);
        Assert.Equal(expected.Settlement, actual.Settlement);
        Assert.Equal(expected.SurvivalVersion, actual.SurvivalVersion);
        Assert.Equal(expected.SettlementVersion, actual.SettlementVersion);
        Assert.Equal(expected.Structures.Select(StructureKey).ToArray(), actual.Structures.Select(StructureKey).ToArray());
        Assert.Equal(expected.StructureContributions.Select(ContributionKey), actual.StructureContributions.Select(ContributionKey));
        Assert.Equal(expected.SocialVersion, actual.SocialVersion);
        Assert.Equal(expected.Relationships, actual.Relationships);
        Assert.Equal(expected.Households.Select(HouseholdKey), actual.Households.Select(HouseholdKey));
    }


    private static string HouseholdKey(Household household) => $"{household.Id.Value}:{household.CreatedMinute}:{household.DissolvedMinute}:{household.DwellingStructureId?.Value}";
    private static string StructureKey(Structure structure) => $"{structure.Id.Value}:{(int)structure.Type}:{(int)structure.Status}:{structure.Condition}:{structure.Location.X},{structure.Location.Y}:{structure.ConstructionStartedMinute}:{structure.CompletedMinute}:{structure.RequiredWood}:{structure.DeliveredWood}:{structure.RequiredStone}:{structure.DeliveredStone}:{structure.RequiredWork}:{structure.CompletedWork}";
    private static string ContributionKey(StructureContribution value) => $"{value.StructureId.Value}:{value.CitizenId.Value}:{value.ConstructionWork}:{value.WoodDelivered}:{value.StoneDelivered}";
    private static Structure CompletedShelter(StructureId id, TileCoordinate site) => new(id, StructureType.Shelter, site, 0, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork)
    {
        Status = StructureStatus.Complete,
        CompletedMinute = 0,
        DeliveredWood = CitizenSimulationRules.ShelterRequiredWood,
        DeliveredStone = CitizenSimulationRules.ShelterRequiredStone,
        CompletedWork = CitizenSimulationRules.ShelterRequiredWork
    };
    private static Structure CompletedStockpile(StructureId id, TileCoordinate site) => new(id, StructureType.Stockpile, site, 0, CitizenSimulationRules.StockpileRequiredWood, CitizenSimulationRules.StockpileRequiredStone, CitizenSimulationRules.StockpileRequiredWork)
    {
        Status = StructureStatus.Complete,
        CompletedMinute = 0,
        DeliveredWood = CitizenSimulationRules.StockpileRequiredWood,
        DeliveredStone = CitizenSimulationRules.StockpileRequiredStone,
        CompletedWork = CitizenSimulationRules.StockpileRequiredWork
    };
    private static SimulationEngine PrepareRestHomeScenario()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        var citizens = Citizens(engine);
        var first = citizens[1];
        var second = citizens[2];
        var donor = citizens[3];
        var counters = Assert.IsType<DeterministicCounters>(typeof(SimulationEngine).GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        var sites = engine.World.Tiles.Where(tile => tile.Buildable && tile.Coordinate != engine.World.StartingSite && engine.World.GetResources(tile.Coordinate).Count == 0 && DeterministicPathfinder.Find(engine.World, second.Location, tile.Coordinate) is { Count: >= 2 }).Select(tile => tile.Coordinate).Take(2).ToArray();
        Assert.Equal(2, sites.Length);
        var source = CompletedShelter(counters.AllocateStructureId(), sites[0]);
        var destination = CompletedShelter(counters.AllocateStructureId(), sites[1]);
        Structures(engine).Add(source.Id.Value, source);
        Structures(engine).Add(destination.Id.Value, destination);
        var contributions = StructureContributions(engine);
        foreach (var shelter in new[] { source, destination }) contributions.Add((shelter.Id.Value, first.Id.Value), new StructureContribution(shelter.Id, first.Id, shelter.CompletedWork, shelter.DeliveredWood, shelter.DeliveredStone));
        donor.HomeStructureId = source.Id;
        second.HomeStructureId = source.Id;
        second.CurrentAction = CitizenAction.Rest;
        second.ActionPhase = CitizenActionPhase.TravelToTarget;
        second.TargetStructureId = source.Id;
        second.ActionTarget = source.Location;
        second.ActionStartedMinute = engine.CurrentMinute;
        var path = Assert.IsAssignableFrom<IReadOnlyList<TileCoordinate>>(DeterministicPathfinder.Find(engine.World, second.Location, source.Location));
        second.ActionCompletesMinute = engine.CurrentMinute.Add(RemainingPathCost(path, engine.World));
        RemoveActionEvent(engine, second.Id);
        ScheduleCitizen(engine, second, CitizenEventNames.MoveStep, engine.CurrentMinute.Add(StepCost(path[0], path[1], engine.World)), CitizenEventNames.MovementPriority);
        ActivePaths(engine)[(second.Id.Value, second.ActionSequence)] = path;
        Assert.True(ActivePaths(engine).ContainsKey((second.Id.Value, second.ActionSequence)));
        RemoveActionEvent(engine, first.Id);
        first.CurrentAction = CitizenAction.Socialize;
        first.ActionPhase = CitizenActionPhase.Perform;
        first.TargetCitizenId = second.Id;
        first.ActionStartedMinute = engine.CurrentMinute;
        first.ActionCompletesMinute = engine.CurrentMinute.Add(CitizenSimulationRules.SocializeDurationMinutes);
        Relationships(engine).Add((first.Id.Value, second.Id.Value), new RelationshipState(first.Id, second.Id, 10_000, 10_000, 10_000, 0, 0, 1));
        var completeAction = typeof(SimulationEngine).GetMethod("CompleteAction", BindingFlags.Instance | BindingFlags.NonPublic, binder: null, types: new[] { typeof(Citizen), typeof(bool) }, modifiers: null)!;
        completeAction.Invoke(engine, new object?[] { first, false });
        Assert.False(ActivePaths(engine).ContainsKey((second.Id.Value, second.ActionSequence)));
        return engine;
    }
    private static void ScheduleCitizen(SimulationEngine engine, Citizen citizen, string name, WorldMinute due, int priority) => typeof(SimulationEngine).GetMethod("ScheduleCitizen", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [citizen, name, due, priority]);
    private static void RemoveActionEvent(SimulationEngine engine, CitizenId citizenId)
    {
        var events = Assert.IsAssignableFrom<System.Collections.IEnumerable>(typeof(SimulationEngine).GetField("_scheduledEvents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        var remove = events.GetType().GetMethod("Remove")!;
        foreach (var item in events.Cast<object>().Where(item =>
        {
            var name = item.GetType().GetProperty("Name")!.GetValue(item) as string;
            var order = (ScheduledEventOrder)item.GetType().GetProperty("Order")!.GetValue(item)!;
            return name is (CitizenEventNames.Decision or CitizenEventNames.MoveStep or CitizenEventNames.ActionComplete) && order.EntitySortKey == citizenId.Value;
        }).ToArray()) remove.Invoke(events, [item]);
    }
    private static long RemainingPathCost(IReadOnlyList<TileCoordinate> path, WorldMap world) => (long)typeof(SimulationEngine).GetMethod("RemainingPathCost", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [path, world])!;
    private static long StepCost(TileCoordinate from, TileCoordinate to, WorldMap world) => (long)typeof(SimulationEngine).GetMethod("StepCost", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [from, to, world])!;
    private static DeterministicCounters Counters(SimulationEngine engine) => Assert.IsType<DeterministicCounters>(typeof(SimulationEngine).GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static Dictionary<(long, long), IReadOnlyList<TileCoordinate>> ActivePaths(SimulationEngine engine) => Assert.IsType<Dictionary<(long, long), IReadOnlyList<TileCoordinate>>>(typeof(SimulationEngine).GetField("_activePaths", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static Dictionary<long, Structure> Structures(SimulationEngine engine) => Assert.IsType<Dictionary<long, Structure>>(typeof(SimulationEngine).GetField("_structures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static Dictionary<(long, long), StructureContribution> StructureContributions(SimulationEngine engine) => Assert.IsType<Dictionary<(long, long), StructureContribution>>(typeof(SimulationEngine).GetField("_structureContributions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static Dictionary<long, Household> Households(SimulationEngine engine) => Assert.IsType<Dictionary<long, Household>>(typeof(SimulationEngine).GetField("_households", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static IEnumerable<ScheduledEventSnapshot> ScheduledEvents(SimulationEngine engine) =>
        Assert.IsAssignableFrom<System.Collections.IEnumerable>(typeof(SimulationEngine).GetField("_scheduledEvents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine))
            .Cast<object>()
            .Select(item => new ScheduledEventSnapshot(
                (ScheduledEventId)item.GetType().GetProperty("Id")!.GetValue(item)!,
                (ScheduledEventOrder)item.GetType().GetProperty("Order")!.GetValue(item)!,
                (string)item.GetType().GetProperty("Name")!.GetValue(item)!,
                (string)item.GetType().GetProperty("PayloadJson")!.GetValue(item)!));
    private static Dictionary<long, Citizen> Citizens(SimulationEngine engine) => Assert.IsType<Dictionary<long, Citizen>>(typeof(SimulationEngine).GetField("_citizens", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static Dictionary<(long, long), RelationshipState> Relationships(SimulationEngine engine) => Assert.IsType<Dictionary<(long, long), RelationshipState>>(typeof(SimulationEngine).GetField("_relationships", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
}
