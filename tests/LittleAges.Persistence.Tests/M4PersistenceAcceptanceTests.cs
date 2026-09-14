using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Persistence.Tests;

/// <summary>Acceptance coverage for the complete M4 checkpoint contract, beyond row-level mapping tests.</summary>
public sealed class M4PersistenceAcceptanceTests
{
    [Fact]
    public async Task FullM4CheckpointReloadRetainsCanonicalSettlementState()
    {
        await WithDatabaseAsync(async path =>
        {
            var source = CreatePhaseSnapshot();
            source.Settlement!.FoodStored = 321;
            source.Settlement.WoodStored = 456;
            source.Settlement.StoneStored = 123;
            source.Citizens[6].LifetimeForagingMinutes = 11;
            source.Citizens[7].LifetimeWoodcuttingMinutes = 22;
            source.Citizens[8].LifetimeStoneworkingMinutes = 33;
            source.Citizens[9].LifetimeConstructionMinutes = 44;
            source.Citizens[10].LifetimeHaulingMinutes = 55;

            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(source, DateTime.UtcNow);
            var loaded = await store.LoadAsync();

            Assert.Equal(source.Seed, loaded.Seed);
            Assert.Equal(source.WorldMinute, loaded.WorldMinute);
            Assert.Equal(source.Counters, loaded.Counters);
            Assert.Equal(source.World!.Fingerprint, loaded.World!.Fingerprint);
            Assert.Equal(SettlementKey(source.Settlement), SettlementKey(loaded.Settlement!));
            Assert.Equal(source.ResourceStates.Select(ResourceKey), loaded.ResourceStates.Select(ResourceKey));
            Assert.Equal(source.Structures.Select(StructureKey), loaded.Structures.Select(StructureKey));
            Assert.Equal(source.StructureContributions.Select(ContributionKey), loaded.StructureContributions.Select(ContributionKey));
            Assert.Equal(source.Citizens.Select(CitizenKey), loaded.Citizens.Select(CitizenKey));
            Assert.Equal(source.ScheduledEvents.OrderBy(item => item.Order), loaded.ScheduledEvents.OrderBy(item => item.Order));
        });
    }

    [Fact]
    public async Task FailedM4CheckpointRollsBackEveryMutableRowGroup()
    {
        await WithDatabaseAsync(async path =>
        {
            var original = CreatePhaseSnapshot();
            var replacement = CreatePhaseSnapshot();
            replacement.Settlement!.FoodStored = 799;
            replacement.Settlement.WoodStored = 1;
            replacement.Settlement.StoneStored = 2;
            replacement.ResourceStates[0].CurrentQuantity--;
            replacement.Citizens[6].Skills.Construction = checked(replacement.Citizens[6].Skills.Construction + 25);
            replacement.Citizens[6].LifetimeConstructionMinutes = 99;
            var project = replacement.Structures.Single(item => item.Status == StructureStatus.UnderConstruction);
            project.DeliveredStone = project.RequiredStone - 1;
            var projectContribution = replacement.StructureContributions.Single(item => item.StructureId == project.Id && item.CitizenId == replacement.Citizens[6].Id);
            projectContribution.StoneDelivered = project.DeliveredStone;
            var alteredEvents = replacement.ScheduledEvents.Select(item => item.Name == CitizenEventNames.SettlementEvaluateDemand
                ? item with { PayloadJson = "{\"version\":1}" }
                : item).ToArray();
            replacement = Rebuild(replacement, scheduledEvents: alteredEvents);

            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(original, DateTime.UtcNow);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.CheckpointAsync(replacement, DateTime.UtcNow, CheckpointFailurePoint.AfterRowsWritten));
            var loaded = await store.LoadAsync();

            Assert.Equal(SettlementKey(original.Settlement!), SettlementKey(loaded.Settlement!));
            Assert.Equal(original.ResourceStates.Select(ResourceKey), loaded.ResourceStates.Select(ResourceKey));
            Assert.Equal(original.Structures.Select(StructureKey), loaded.Structures.Select(StructureKey));
            Assert.Equal(original.StructureContributions.Select(ContributionKey), loaded.StructureContributions.Select(ContributionKey));
            Assert.Equal(original.Citizens.Select(CitizenKey), loaded.Citizens.Select(CitizenKey));
            Assert.Equal(original.ScheduledEvents.OrderBy(item => item.Order), loaded.ScheduledEvents.OrderBy(item => item.Order));
        });
    }

    [Fact]
    public async Task BlockedDepositReloadPreservesCargoAndSchedulesExactRetry()
    {
        await WithDatabaseAsync(async path =>
        {
            var source = new SimulationEngine(new WorldSeed(42)).CreatePersistenceSnapshot();
            source.Settlement!.FoodStored = CitizenSimulationRules.BaseStorageCapacity;
            var citizen = source.Citizens[0];
            citizen.CurrentAction = CitizenAction.GatherFood;
            citizen.ActionPhase = CitizenActionPhase.WaitingForStorage;
            citizen.TargetResourceNodeId = source.World!.Resources.First(node => node.Type == ResourceType.Food).Id;
            citizen.CarriedResourceType = ResourceType.Food;
            citizen.CarriedResourceQuantity = 7;
            citizen.ActionStartedMinute = WorldMinute.Zero;
            citizen.ActionCompletesMinute = new WorldMinute(CitizenSimulationRules.StorageRetryIntervalMinutes);
            source = Rebuild(source, scheduledEvents: ReplaceActionEvent(source, citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value.Value, CitizenEventNames.CompletionPriority));

            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(source, DateTime.UtcNow);
            var restored = SimulationEngine.FromPersistenceSnapshot(await store.LoadAsync());
            restored.AdvanceUntil(new WorldMinute(CitizenSimulationRules.StorageRetryIntervalMinutes));

            var blocked = restored.GetCitizen(citizen.Id)!;
            Assert.Equal(CitizenActionPhase.WaitingForStorage, blocked.ActionPhase);
            Assert.Equal(ResourceType.Food, blocked.CarriedResourceType);
            Assert.Equal(7, blocked.CarriedResourceQuantity);
            Assert.Equal(new WorldMinute(CitizenSimulationRules.StorageRetryIntervalMinutes * 2), blocked.ActionCompletesMinute);
            var retry = Assert.Single(restored.CreatePersistenceSnapshot().ScheduledEvents, item => item.Name == CitizenEventNames.ActionComplete && item.Order.EntitySortKey == citizen.Id.Value);
            Assert.Equal(new WorldMinute(CitizenSimulationRules.StorageRetryIntervalMinutes * 2), retry.Order.DueWorldMinute);
        });
    }

    [Fact]
    public async Task ConstructionTransportReloadRebuildsRouteAndDeliversExactCargo()
    {
        await WithDatabaseAsync(async path =>
        {
            var source = CreateTransportSnapshot(out var haulerId, out var projectId, out var completionMinute);
            var initialHaulingSkill = source.Citizens.Single(item => item.Id == haulerId).Skills.Hauling;
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(source, DateTime.UtcNow);
            var restored = SimulationEngine.FromPersistenceSnapshot(await store.LoadAsync());

            restored.AdvanceUntil(completionMinute);

            var hauler = restored.GetCitizen(haulerId)!;
            var project = restored.GetStructure(projectId)!;
            Assert.Equal(20, project.DeliveredWood);
            Assert.Null(hauler.CarriedResourceType);
            Assert.Equal(0, hauler.CarriedResourceQuantity);
            Assert.NotEqual(CitizenAction.HaulConstruction, hauler.CurrentAction);
            Assert.Equal(completionMinute.Value, hauler.LifetimeHaulingMinutes);
            Assert.Equal(initialHaulingSkill + 15, hauler.Skills.Hauling);
        });
    }

    [Fact]
    public async Task M4ActionPhaseMatrixRoundTripsTravelBuildHaulWaitAndRestHome()
    {
        await WithDatabaseAsync(async path =>
        {
            var source = CreatePhaseSnapshot();
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(source, DateTime.UtcNow);
            var loaded = await store.LoadAsync();

            Assert.Equal(source.Citizens.Take(6).Select(ActionKey), loaded.Citizens.Take(6).Select(ActionKey));
            Assert.Equal(new[]
            {
                CitizenActionPhase.TravelToStockpile,
                CitizenActionPhase.TransportToConstruction,
                CitizenActionPhase.WaitingForStorage,
                CitizenActionPhase.TravelToTarget,
                CitizenActionPhase.Perform,
                CitizenActionPhase.TravelToTarget
            }, loaded.Citizens.Take(6).Select(item => item.ActionPhase));
            Assert.Equal(source.ScheduledEvents.Where(item => item.Order.EntitySortKey is >= 1 and <= 6).OrderBy(item => item.Order), loaded.ScheduledEvents.Where(item => item.Order.EntitySortKey is >= 1 and <= 6).OrderBy(item => item.Order));
        });
    }

    private static SimulationPersistenceSnapshot CreateTransportSnapshot(out CitizenId haulerId, out StructureId projectId, out WorldMinute completionMinute)
    {
        var baseline = new SimulationEngine(new WorldSeed(42)).CreatePersistenceSnapshot();
        var world = baseline.World!;
        var site = Sites(world, 1)[0];
        var project = new Structure(new StructureId(baseline.Counters.NextEntityId), StructureType.Shelter, site, 0, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork);
        var hauler = baseline.Citizens[0];
        var path = DeterministicPathfinder.Find(world, hauler.Location, site)!;
        Assert.True(path.Count >= 2);
        hauler.CurrentAction = CitizenAction.HaulConstruction;
        hauler.ActionPhase = CitizenActionPhase.TransportToConstruction;
        hauler.TargetStructureId = project.Id;
        hauler.ActionTarget = site;
        hauler.CarriedResourceType = ResourceType.Wood;
        hauler.CarriedResourceQuantity = 20;
        hauler.ActionStartedMinute = WorldMinute.Zero;
        completionMinute = new WorldMinute(RemainingPathCost(path, world));
        hauler.ActionCompletesMinute = completionMinute;
        haulerId = hauler.Id;
        projectId = project.Id;
        return Rebuild(baseline, structures: new[] { project }, scheduledEvents: ReplaceActionEvent(baseline, hauler, CitizenEventNames.MoveStep, StepCost(path[0], path[1], world), CitizenEventNames.MovementPriority));
    }

    private static SimulationPersistenceSnapshot CreatePhaseSnapshot()
    {
        var baseline = new SimulationEngine(new WorldSeed(42)).CreatePersistenceSnapshot();
        var world = baseline.World!;
        var sites = Sites(world, 3);
        var shelter = Complete(new Structure(new StructureId(baseline.Counters.NextEntityId), StructureType.Shelter, sites[0], 0, 40, 10, 600));
        var stockpile = Complete(new Structure(new StructureId(baseline.Counters.NextEntityId + 1), StructureType.Stockpile, sites[1], 0, 60, 30, 900));
        var project = new Structure(new StructureId(baseline.Counters.NextEntityId + 2), StructureType.Workshop, sites[2], 0, 80, 50, 1200) { DeliveredWood = 79, DeliveredStone = 50 };
        var citizens = baseline.Citizens;
        citizens[5].HomeStructureId = shelter.Id;
        var events = baseline.ScheduledEvents;

        SetTravel(world, citizens[0], CitizenAction.HaulConstruction, CitizenActionPhase.TravelToStockpile, sites[2], world.StartingSite, project.Id, out var haulToStockpilePath);
        events = ReplaceActionEvent(baseline, citizens[0], CitizenEventNames.MoveStep, StepCost(haulToStockpilePath[0], haulToStockpilePath[1], world));

        SetTravel(world, citizens[1], CitizenAction.HaulConstruction, CitizenActionPhase.TransportToConstruction, world.StartingSite, sites[2], project.Id, out var transportPath, ResourceType.Wood, 1);
        events = ReplaceActionEvent(events, citizens[1], CitizenEventNames.MoveStep, StepCost(transportPath[0], transportPath[1], world));

        var waiting = citizens[2];
        waiting.CurrentAction = CitizenAction.GatherFood; waiting.ActionPhase = CitizenActionPhase.WaitingForStorage; waiting.TargetResourceNodeId = world.Resources.First(item => item.Type == ResourceType.Food).Id; waiting.CarriedResourceType = ResourceType.Food; waiting.CarriedResourceQuantity = 1; waiting.ActionStartedMinute = WorldMinute.Zero; waiting.ActionCompletesMinute = new WorldMinute(60);
        events = ReplaceActionEvent(events, waiting, CitizenEventNames.ActionComplete, 60, CitizenEventNames.CompletionPriority);

        SetTravel(world, citizens[3], CitizenAction.Build, CitizenActionPhase.TravelToTarget, world.StartingSite, sites[2], project.Id, out var buildPath);
        events = ReplaceActionEvent(events, citizens[3], CitizenEventNames.MoveStep, StepCost(buildPath[0], buildPath[1], world));

        var builder = citizens[4];
        builder.Location = sites[2]; builder.CurrentAction = CitizenAction.Build; builder.ActionPhase = CitizenActionPhase.Perform; builder.TargetStructureId = project.Id; builder.ActionStartedMinute = WorldMinute.Zero; builder.ActionCompletesMinute = new WorldMinute(CitizenSimulationRules.ConstructionShiftDurationMinutes);
        events = ReplaceActionEvent(events, builder, CitizenEventNames.ActionComplete, CitizenSimulationRules.ConstructionShiftDurationMinutes, CitizenEventNames.CompletionPriority);

        SetTravel(world, citizens[5], CitizenAction.Rest, CitizenActionPhase.TravelToTarget, world.StartingSite, sites[0], shelter.Id, out var restPath);
        events = ReplaceActionEvent(events, citizens[5], CitizenEventNames.MoveStep, StepCost(restPath[0], restPath[1], world));

        var contributions = new[]
        {
            new StructureContribution(shelter.Id, citizens[6].Id, 600, 40, 10),
            new StructureContribution(stockpile.Id, citizens[7].Id, 900, 60, 30),
            new StructureContribution(project.Id, citizens[6].Id, 0, 79, 50)
        };
        return Rebuild(baseline, scheduledEvents: events, structures: new[] { shelter, stockpile, project }, contributions: contributions);
    }

    private static void SetTravel(WorldMap world, Citizen citizen, CitizenAction action, CitizenActionPhase phase, TileCoordinate location, TileCoordinate target, StructureId structureId, out IReadOnlyList<TileCoordinate> path, ResourceType? cargoType = null, int cargoQuantity = 0)
    {
        citizen.Location = location; citizen.CurrentAction = action; citizen.ActionPhase = phase; citizen.TargetStructureId = structureId; citizen.ActionTarget = target; citizen.CarriedResourceType = cargoType; citizen.CarriedResourceQuantity = cargoQuantity; citizen.ActionStartedMinute = WorldMinute.Zero;
        path = DeterministicPathfinder.Find(world, location, target)!;
        Assert.True(path.Count >= 2);
        citizen.ActionCompletesMinute = new WorldMinute(RemainingPathCost(path, world));
    }

    private static SimulationPersistenceSnapshot Rebuild(SimulationPersistenceSnapshot source, IReadOnlyList<ScheduledEventSnapshot>? scheduledEvents = null, IReadOnlyList<Structure>? structures = null, IReadOnlyList<StructureContribution>? contributions = null)
    {
        var effectiveStructures = structures ?? source.Structures;
        return new(source.Seed, source.WorldMinute, source.WorldSchemaVersion, source.SimulationRulesVersion, source.ApplicationVersion, source.WorldConfiguration, CountersAfterStructures(source.Counters, effectiveStructures), scheduledEvents ?? source.ScheduledEvents, source.World, source.Citizens, source.CitizenGenerationVersion, source.ResourceStates, source.Settlement, source.SurvivalVersion, source.SettlementVersion, effectiveStructures, contributions ?? source.StructureContributions);
    }
    private static DeterministicCountersSnapshot CountersAfterStructures(DeterministicCountersSnapshot counters, IReadOnlyList<Structure> structures)
    {
        var nextEntityId = structures.Count == 0 ? counters.NextEntityId : Math.Max(counters.NextEntityId, checked(structures.Max(x => x.Id.Value) + 1));
        return counters with { NextEntityId = nextEntityId };
    }

    private static ScheduledEventSnapshot[] ReplaceActionEvent(SimulationPersistenceSnapshot source, Citizen citizen, string name, long due, int priority = CitizenEventNames.MovementPriority) => ReplaceActionEvent(source.ScheduledEvents, citizen, name, due, priority);
    private static ScheduledEventSnapshot[] ReplaceActionEvent(IReadOnlyList<ScheduledEventSnapshot> events, Citizen citizen, string name, long due, int priority = CitizenEventNames.MovementPriority) => events.Select(item => item.Name == CitizenEventNames.Decision && item.Order.EntitySortKey == citizen.Id.Value ? item with { Name = name, Order = new ScheduledEventOrder(new WorldMinute(due), priority, citizen.Id.Value, item.Order.Sequence), PayloadJson = $"{{\"citizenId\":\"{citizen.Id.Value}\",\"actionSequence\":{citizen.ActionSequence}}}" } : item).ToArray();
    private static Structure Complete(Structure value) { value.DeliveredWood = value.RequiredWood; value.DeliveredStone = value.RequiredStone; value.CompletedWork = value.RequiredWork; value.Status = StructureStatus.Complete; value.CompletedMinute = 0; return value; }
    private static TileCoordinate[] Sites(WorldMap world, int count) => world.Tiles.Where(tile => tile.Coordinate != world.StartingSite && tile.Buildable && world.GetResources(tile.Coordinate).Count == 0 && DeterministicPathfinder.Find(world, world.StartingSite, tile.Coordinate) is { Count: >= 2 }).Select(tile => tile.Coordinate).Take(count).ToArray();
    private static long StepCost(TileCoordinate from, TileCoordinate to, WorldMap world) => checked((long)(from.X == to.X || from.Y == to.Y ? 10 : 14) * world.GetTile(to).MovementCost);
    private static long RemainingPathCost(IReadOnlyList<TileCoordinate> path, WorldMap world) => Enumerable.Range(1, path.Count - 1).Sum(index => StepCost(path[index - 1], path[index], world));
    private static string SettlementKey(SettlementState value) => $"{value.FoodStored}:{value.WoodStored}:{value.StoneStored}:{value.BaseStorageCapacity}:{value.DemandUpdatedMinute}:{value.ExposureConsequencesStartMinute}";
    private static string ResourceKey(ResourceState value) => $"{value.ResourceNodeId.Value}:{value.CurrentQuantity}";
    private static string StructureKey(Structure value) => $"{value.Id.Value}:{value.Type}:{value.Status}:{value.Condition}:{value.Location}:{value.ConstructionStartedMinute}:{value.CompletedMinute}:{value.RequiredWood}:{value.DeliveredWood}:{value.RequiredStone}:{value.DeliveredStone}:{value.RequiredWork}:{value.CompletedWork}";
    private static string ContributionKey(StructureContribution value) => $"{value.StructureId.Value}:{value.CitizenId.Value}:{value.ConstructionWork}:{value.WoodDelivered}:{value.StoneDelivered}";
    private static string CitizenKey(Citizen value) => $"{value.Id.Value}:{value.Location}:{value.HomeStructureId?.Value}:{value.TargetStructureId?.Value}:{value.Skills.Construction}:{value.LifetimeForagingMinutes}:{value.LifetimeWoodcuttingMinutes}:{value.LifetimeStoneworkingMinutes}:{value.LifetimeConstructionMinutes}:{value.LifetimeHaulingMinutes}:{ActionKey(value)}";
    private static string ActionKey(Citizen value) => $"{value.Id.Value}:{value.CurrentAction}:{value.ActionPhase}:{value.ActionSequence}:{value.ActionStartedMinute?.Value}:{value.ActionCompletesMinute?.Value}:{value.ActionTarget}:{value.TargetResourceNodeId?.Value}:{value.TargetStructureId?.Value}:{value.CarriedResourceType}:{value.CarriedResourceQuantity}";

    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LittleAges-M4-Persistence-Acceptance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "world.db");
        try { await test(path); }
        finally { foreach (var suffix in new[] { string.Empty, "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
