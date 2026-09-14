using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

/// <summary>Black-box acceptance coverage for the public M4 settlement simulation contracts.</summary>
public sealed class M4SettlementAcceptanceTests
{
    [Fact]
    public void DemandUsesShelterThenStockpileThenWorkshopPriorityWithOneProject()
    {
        var shelterTie = RestoreWithCompletedShelters(4, food: 400, wood: 240, stone: 0);
        shelterTie.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SettlementDemandIntervalMinutes));
        Assert.Equal(StructureType.Shelter, Assert.Single(shelterTie.Structures, x => x.Status == StructureStatus.UnderConstruction).Type);

        var stockpile = RestoreWithCompletedShelters(5, food: 640, wood: 0, stone: 0);
        stockpile.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SettlementDemandIntervalMinutes));
        Assert.Equal(StructureType.Stockpile, Assert.Single(stockpile.Structures, x => x.Status == StructureStatus.UnderConstruction).Type);

        var workshop = RestoreWithCompletedShelters(5, food: 400, wood: 0, stone: 0);
        workshop.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SettlementDemandIntervalMinutes));
        Assert.Equal(StructureType.Workshop, Assert.Single(workshop.Structures, x => x.Status == StructureStatus.UnderConstruction).Type);
    }

    [Fact]
    public void DemandPlacementUsesCanonicalCostDistanceRowAndColumnOrderingAndExcludesInvalidTiles()
    {
        var first = new SimulationEngine(new WorldSeed(42));
        var second = new SimulationEngine(new WorldSeed(42));
        first.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SettlementDemandIntervalMinutes));
        second.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SettlementDemandIntervalMinutes));

        var actual = Assert.Single(first.Structures).Location;
        Assert.Equal(actual, Assert.Single(second.Structures).Location);
        var expected = ExpectedConstructionSite(first.World, Array.Empty<TileCoordinate>());
        Assert.Equal(expected, actual);
        Assert.NotEqual(first.World.StartingSite, actual);
        Assert.True(first.World.GetTile(actual).Walkable);
        Assert.True(first.World.GetTile(actual).Buildable);
        Assert.NotEqual(TerrainType.Freshwater, first.World.GetTile(actual).Terrain);
        Assert.Empty(first.World.GetResources(actual));
    }

    [Fact]
    public void HaulingUsesExactCarryCapacityReservesInTransitMaterialAndRecordsDelivery()
    {
        var source = new SimulationEngine(new WorldSeed(42));
        var snapshot = source.CreatePersistenceSnapshot();
        var site = ValidSites(source.World, 1)[0];
        var project = Shelter(new StructureId(snapshot.Counters.NextEntityId), site);
        snapshot.Settlement!.WoodStored = 40;
        snapshot.Settlement.FoodStored = 0;
        var routeToStockpile = RouteFromDeterministicNeighbor(source.World, source.World.StartingSite);
        snapshot.Citizens[0].Location = routeToStockpile[0];
        snapshot.Citizens[0].Skills.Hauling = 5_000;
        var haulingEvents = FreezeActions(snapshot.ScheduledEvents, snapshot);
        haulingEvents = ConfigureHaulAtStockpile(haulingEvents, snapshot, snapshot.Citizens[0], project.Id);

        var engine = SimulationEngine.FromPersistenceSnapshot(Copy(snapshot, events: haulingEvents, structures: new[] { project }));
        engine.AdvanceUntil(new WorldMinute(SimulationEngine.StepCost(routeToStockpile[0], routeToStockpile[1], source.World)));

        var first = engine.GetCitizen(snapshot.Citizens[0].Id)!;
        Assert.Equal(routeToStockpile[1], first.Location);
        Assert.Equal(CitizenActionPhase.TransportToConstruction, first.ActionPhase);
        Assert.Equal(25, first.CarriedResourceQuantity);
        Assert.Equal(15, engine.Settlement.WoodStored);

        var deliverySnapshot = source.CreatePersistenceSnapshot();
        var routeToProject = RouteFromDeterministicNeighbor(source.World, site);
        deliverySnapshot.Settlement!.FoodStored = 0;
        deliverySnapshot.Settlement.WoodStored = 0;
        var deliveryEvents = FreezeActions(deliverySnapshot.ScheduledEvents, deliverySnapshot);
        ConfigureConstructionTransport(deliverySnapshot.Citizens[0], project.Id, site, routeToProject[0], 25, source.World);
        ConfigureConstructionTransport(deliverySnapshot.Citizens[1], project.Id, site, routeToProject[0], 15, source.World);
        var deliveryDue = deliverySnapshot.WorldMinute.Add(SimulationEngine.StepCost(routeToProject[0], routeToProject[1], source.World));
        deliveryEvents = ReplaceEvent(deliveryEvents, deliverySnapshot.Citizens[0], CitizenEventNames.MoveStep, deliveryDue, CitizenEventNames.MovementPriority);
        deliveryEvents = ReplaceEvent(deliveryEvents, deliverySnapshot.Citizens[1], CitizenEventNames.MoveStep, deliveryDue, CitizenEventNames.MovementPriority);
        var delivery = SimulationEngine.FromPersistenceSnapshot(Copy(deliverySnapshot, events: deliveryEvents, structures: new[] { project }));
        for (var index = 0; index < 20 && delivery.GetStructure(project.Id)!.DeliveredWood < project.RequiredWood; index++) Assert.True(delivery.ProcessNextEvent());
        Assert.Equal(site, delivery.GetCitizen(new CitizenId(1))!.Location);
        Assert.Equal(site, delivery.GetCitizen(new CitizenId(2))!.Location);
        Assert.Equal(40, delivery.GetStructure(project.Id)!.DeliveredWood);
        Assert.Equal(0, delivery.GetStructure(project.Id)!.DeliveredStone);
        Assert.Equal(15, delivery.GetCitizen(new CitizenId(1))!.Skills.Hauling - deliverySnapshot.Citizens[0].Skills.Hauling);
        Assert.Equal(15, delivery.GetCitizen(new CitizenId(2))!.Skills.Hauling - deliverySnapshot.Citizens[1].Skills.Hauling);
        Assert.Equal(40, delivery.StructureContributions.Where(x => x.StructureId == project.Id).Sum(x => x.WoodDelivered));
    }

    [Fact]
    public void BuildShiftsUseExactWorkWorkshopMultiplierConcurrentBuildersAndSingleCompletion()
    {
        var source = new SimulationEngine(new WorldSeed(42));
        var snapshot = source.CreatePersistenceSnapshot();
        var sites = ValidSites(source.World, 2);
        var project = Shelter(new StructureId(snapshot.Counters.NextEntityId), sites[0]);
        project.DeliveredWood = project.RequiredWood;
        project.DeliveredStone = project.RequiredStone;
        var workshop = CompleteWorkshop(new StructureId(snapshot.Counters.NextEntityId + 1), sites[1], snapshot.Citizens[2].Id);
        snapshot.Citizens[0].Location = project.Location;
        snapshot.Citizens[0].Skills.Construction = 5_000;
        var multipliedEvents = ConfigureBuild(snapshot.ScheduledEvents, snapshot, snapshot.Citizens[0], project.Id);

        var multipliedContributions = workshop.Contributions.Append(new StructureContribution(project.Id, snapshot.Citizens[1].Id, 0, project.RequiredWood, project.RequiredStone)).ToArray();
        var multiplied = SimulationEngine.FromPersistenceSnapshot(Copy(snapshot, events: multipliedEvents, structures: new[] { project, workshop.Structure }, contributions: multipliedContributions));
        multiplied.AdvanceUntil(new WorldMinute(CitizenSimulationRules.ConstructionShiftDurationMinutes));
        Assert.Equal(131, multiplied.GetStructure(project.Id)!.CompletedWork);
        Assert.Equal(5_025, multiplied.GetCitizen(snapshot.Citizens[0].Id)!.Skills.Construction);
        Assert.Equal(180, multiplied.GetCitizen(snapshot.Citizens[0].Id)!.LifetimeConstructionMinutes);

        var concurrentSnapshot = source.CreatePersistenceSnapshot();
        var concurrent = Shelter(new StructureId(concurrentSnapshot.Counters.NextEntityId), sites[0]);
        concurrent.DeliveredWood = concurrent.RequiredWood;
        concurrent.DeliveredStone = concurrent.RequiredStone;
        concurrent.CompletedWork = 400;
        concurrentSnapshot.Citizens[0].Location = concurrent.Location;
        concurrentSnapshot.Citizens[1].Location = concurrent.Location;
        var concurrentEvents = ConfigureBuild(concurrentSnapshot.ScheduledEvents, concurrentSnapshot, concurrentSnapshot.Citizens[0], concurrent.Id);
        concurrentEvents = ConfigureBuild(concurrentEvents, concurrentSnapshot, concurrentSnapshot.Citizens[1], concurrent.Id);
        var engine = SimulationEngine.FromPersistenceSnapshot(Copy(concurrentSnapshot, events: concurrentEvents, structures: new[] { concurrent }, contributions: new[] { new StructureContribution(concurrent.Id, concurrentSnapshot.Citizens[2].Id, 400, concurrent.RequiredWood, concurrent.RequiredStone) }));
        engine.AdvanceUntil(new WorldMinute(CitizenSimulationRules.ConstructionShiftDurationMinutes));
        var complete = engine.GetStructure(concurrent.Id)!;
        Assert.Equal(StructureStatus.Complete, complete.Status);
        Assert.Equal(600, complete.CompletedWork);
        Assert.Equal(CitizenSimulationRules.ConstructionShiftDurationMinutes, complete.CompletedMinute);
        Assert.Equal(200, engine.StructureContributions.Where(x => x.StructureId == concurrent.Id && x.CitizenId != concurrentSnapshot.Citizens[2].Id).Sum(x => x.ConstructionWork));
    }

    [Fact]
    public void ShelterAssignmentIsIdOrderedHasCapacityFourAndReconcilesOnNewCompletion()
    {
        var source = new SimulationEngine(new WorldSeed(42));
        var snapshot = source.CreatePersistenceSnapshot();
        var sites = ValidSites(source.World, 2);
        var existing = CompleteShelter(new StructureId(snapshot.Counters.NextEntityId), sites[0], snapshot.Citizens[0].Id);
        var completing = Shelter(new StructureId(snapshot.Counters.NextEntityId + 1), sites[1]);
        completing.DeliveredWood = completing.RequiredWood;
        completing.DeliveredStone = completing.RequiredStone;
        completing.CompletedWork = 500;
        snapshot.Citizens[0].Location = completing.Location;
        var completionEvents = ConfigureBuild(snapshot.ScheduledEvents, snapshot, snapshot.Citizens[0], completing.Id);

        var engine = SimulationEngine.FromPersistenceSnapshot(Copy(snapshot, events: completionEvents, structures: new[] { existing.Structure, completing }, contributions: existing.Contributions.Append(new StructureContribution(completing.Id, snapshot.Citizens[1].Id, 500, completing.RequiredWood, completing.RequiredStone)).ToArray()));
        engine.AdvanceUntil(new WorldMinute(CitizenSimulationRules.ConstructionShiftDurationMinutes));

        Assert.Equal(existing.Structure.Id, engine.GetCitizen(new CitizenId(1))!.HomeStructureId);
        Assert.Equal(existing.Structure.Id, engine.GetCitizen(new CitizenId(4))!.HomeStructureId);
        Assert.Equal(completing.Id, engine.GetCitizen(new CitizenId(5))!.HomeStructureId);
        Assert.Equal(completing.Id, engine.GetCitizen(new CitizenId(8))!.HomeStructureId);
        Assert.Null(engine.GetCitizen(new CitizenId(9))!.HomeStructureId);
    }

    [Fact]
    public void RestReducesShelterNeedOnlyAtCompletedAssignedHome()
    {
        var source = new SimulationEngine(new WorldSeed(42));
        var snapshot = source.CreatePersistenceSnapshot();
        var shelter = CompleteShelter(new StructureId(snapshot.Counters.NextEntityId), ValidSites(source.World, 1)[0], snapshot.Citizens[0].Id);
        var sheltered = snapshot.Citizens[0];
        var unhoused = snapshot.Citizens[1];
        sheltered.HomeStructureId = shelter.Structure.Id;
        sheltered.Location = shelter.Structure.Location;
        unhoused.Location = source.World.StartingSite;
        sheltered.Needs = new CitizenNeeds(0, 0, 10_000, 0);
        unhoused.Needs = new CitizenNeeds(0, 0, 10_000, 0);
        var restEvents = ConfigureRest(snapshot.ScheduledEvents, snapshot, sheltered);
        restEvents = ConfigureRest(restEvents, snapshot, unhoused);

        var engine = SimulationEngine.FromPersistenceSnapshot(Copy(snapshot, events: restEvents, structures: new[] { shelter.Structure }, contributions: shelter.Contributions));
        engine.AdvanceUntil(new WorldMinute(CitizenSimulationRules.RestDurationMinutes));
        Assert.Equal(3_000, engine.GetCitizen(sheltered.Id)!.Needs.Shelter);
        Assert.Equal(10_000, engine.GetCitizen(unhoused.Id)!.Needs.Shelter);
    }

    [Fact]
    public void ExposureHonorsGraceWinterResilienceAndCausePrecedence()
    {
        var beforeGrace = ExposureEngine(0, health: 10_000, shelter: 10_000);
        beforeGrace.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SurvivalCheckIntervalMinutes));
        Assert.Equal(10_000, beforeGrace.GetCitizen(new CitizenId(1))!.Health);

        var normal = ExposureEngine(0, health: 10_000, shelter: 10_000, exposureStart: 0);
        normal.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SurvivalCheckIntervalMinutes));
        var normalCitizen = normal.GetCitizen(new CitizenId(1))!;
        var normalDamage = CitizenSimulationRules.ExposureDamagePerCheck * (10_000 - normalCitizen.Traits.Resilience / 4) / 10_000;
        Assert.Equal(10_000 - normalDamage, normalCitizen.Health);

        var winterMinute = (long)WorldCalendar.DaysPerSeason * WorldCalendar.MinutesPerDay * 3;
        var winter = ExposureEngine(winterMinute, health: 10_000, shelter: 10_000, exposureStart: winterMinute);
        winter.AdvanceUntil(new WorldMinute(winterMinute + CitizenSimulationRules.SurvivalCheckIntervalMinutes));
        var winterCitizen = winter.GetCitizen(new CitizenId(1))!;
        var winterDamage = CitizenSimulationRules.WinterExposureDamagePerCheck * (10_000 - winterCitizen.Traits.Resilience / 4) / 10_000;
        Assert.Equal(10_000 - winterDamage, winterCitizen.Health);
        Assert.True(winterDamage > normalDamage);

        var exposureDeath = ExposureEngine(0, health: 1, shelter: 10_000, exposureStart: 0);
        exposureDeath.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SurvivalCheckIntervalMinutes));
        Assert.Equal("exposure", exposureDeath.GetCitizen(new CitizenId(1))!.DeathCause);

        var deprivation = ExposureEngine(0, health: 1, shelter: 10_000, hunger: 10_000, rest: 10_000, exposureStart: 0);
        deprivation.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SurvivalCheckIntervalMinutes));
        Assert.Equal("deprivation", deprivation.GetCitizen(new CitizenId(1))!.DeathCause);
    }

    [Fact]
    public void OccupationsHonorThresholdShareAndStableTieOrderingForEveryLabel()
    {
        var citizen = new Citizen(new CitizenId(1), 0, "Ada", "Tester", 0, new TileCoordinate(0, 0), new CitizenTraits(0, 0, 0, 0, 0, 0), new CitizenSkills(0, 0, 0, 0, 0, 0));
        Assert.Equal(CitizenOccupation.Generalist, citizen.Occupation);

        SetWork(citizen, 400, 0, 0, 0, 0); Assert.Equal("Forager", citizen.Occupation);
        SetWork(citizen, 0, 400, 0, 0, 0); Assert.Equal("Lumberjack", citizen.Occupation);
        SetWork(citizen, 0, 0, 400, 0, 0); Assert.Equal("Stoneworker", citizen.Occupation);
        SetWork(citizen, 0, 0, 0, 400, 0); Assert.Equal("Builder", citizen.Occupation);
        SetWork(citizen, 0, 0, 0, 0, 400); Assert.Equal("Hauler", citizen.Occupation);
        SetWork(citizen, 359, 0, 0, 0, 0); Assert.Equal(CitizenOccupation.Generalist, citizen.Occupation);
        SetWork(citizen, 390, 200, 150, 140, 120); Assert.Equal(CitizenOccupation.Generalist, citizen.Occupation);
        SetWork(citizen, 400, 400, 0, 0, 0); Assert.Equal("Forager", citizen.Occupation);
    }

    private static SimulationEngine RestoreWithCompletedShelters(int count, int food, int wood, int stone)
    {
        var source = new SimulationEngine(new WorldSeed(42));
        var snapshot = source.CreatePersistenceSnapshot();
        var sites = ValidSites(source.World, count);
        var structures = sites.Select((site, index) => CompleteShelter(new StructureId(snapshot.Counters.NextEntityId + index), site, snapshot.Citizens[index].Id)).ToArray();
        var settlement = new SettlementState(food, wood, stone, CitizenSimulationRules.BaseStorageCapacity, 0, CitizenSimulationRules.ExposureGraceDurationMinutes);
        var frozenEvents = FreezeActions(snapshot.ScheduledEvents, snapshot);
        return SimulationEngine.FromPersistenceSnapshot(Copy(snapshot, events: frozenEvents, settlement: settlement, structures: structures.Select(x => x.Structure).ToArray(), contributions: structures.SelectMany(x => x.Contributions).ToArray()));
    }

    private static SimulationEngine ExposureEngine(long initialMinute, int health, int shelter, int hunger = 0, int rest = 0, long? exposureStart = null)
    {
        var source = new SimulationEngine(new WorldSeed(42), new WorldMinute(initialMinute));
        var snapshot = source.CreatePersistenceSnapshot();
        var citizen = snapshot.Citizens[0];
        citizen.Health = health;
        citizen.Needs = new CitizenNeeds(hunger, rest, shelter, 0);
        citizen.NeedsUpdatedMinute = initialMinute;
        citizen.HealthUpdatedMinute = initialMinute;
        var settlement = new SettlementState(snapshot.Settlement!.FoodStored, snapshot.Settlement.WoodStored, snapshot.Settlement.StoneStored, snapshot.Settlement.BaseStorageCapacity, snapshot.Settlement.DemandUpdatedMinute, exposureStart ?? snapshot.Settlement.ExposureConsequencesStartMinute);
        var frozenEvents = FreezeActions(snapshot.ScheduledEvents, snapshot);
        return SimulationEngine.FromPersistenceSnapshot(Copy(snapshot, events: frozenEvents, settlement: settlement));
    }

    private static TileCoordinate ExpectedConstructionSite(WorldMap world, IReadOnlyCollection<TileCoordinate> occupied)
    {
        var costs = DeterministicPathfinder.ComputeTravelCosts(world, world.StartingSite);
        var resourceLocations = world.Resources.Select(x => x.Coordinate).ToHashSet();
        return world.Tiles.Where(x => x.Walkable && x.Buildable && x.Terrain != TerrainType.Freshwater && x.Coordinate != world.StartingSite && !occupied.Contains(x.Coordinate) && !resourceLocations.Contains(x.Coordinate) && costs.ContainsKey(x.Coordinate))
            .Select(x => (x.Coordinate, Cost: costs[x.Coordinate], Distance: Math.Abs(x.Coordinate.X - world.StartingSite.X) + Math.Abs(x.Coordinate.Y - world.StartingSite.Y)))
            .OrderBy(x => x.Cost).ThenBy(x => x.Distance).ThenBy(x => x.Coordinate.Y).ThenBy(x => x.Coordinate.X).First().Coordinate;
    }

    private static IReadOnlyList<TileCoordinate> RouteFromDeterministicNeighbor(WorldMap world, TileCoordinate destination)
    {
        var directions = new[] { (X: 0, Y: -1), (X: 1, Y: 0), (X: 0, Y: 1), (X: -1, Y: 0), (X: 1, Y: -1), (X: 1, Y: 1), (X: -1, Y: 1), (X: -1, Y: -1) };
        foreach (var (x, y) in directions)
        {
            var startX = destination.X + x;
            var startY = destination.Y + y;
            if (startX < 0 || startX >= world.Width || startY < 0 || startY >= world.Height) continue;
            var start = new TileCoordinate(startX, startY);
            if (!world.GetTile(start).Walkable) continue;
            var path = DeterministicPathfinder.Find(world, start, destination);
            if (path is not { Count: > 1 }) continue;
            Assert.Equal(start, path[0]);
            Assert.Equal(destination, path[^1]);
            Assert.All(path, coordinate => Assert.True(world.GetTile(coordinate).Walkable));
            return path;
        }

        throw new Xunit.Sdk.XunitException($"No walkable deterministic neighbor route exists for {destination}.");
    }

    private static ScheduledEventSnapshot[] ConfigureHaulAtStockpile(IReadOnlyList<ScheduledEventSnapshot> events, SimulationPersistenceSnapshot snapshot, Citizen citizen, StructureId structureId)
    {
        citizen.CurrentAction = CitizenAction.HaulConstruction;
        citizen.ActionPhase = CitizenActionPhase.TravelToStockpile;
        citizen.ActionTarget = snapshot.World!.StartingSite;
        citizen.TargetStructureId = structureId;
        citizen.ActionStartedMinute = snapshot.WorldMinute;
        var path = DeterministicPathfinder.Find(snapshot.World!, citizen.Location, snapshot.World.StartingSite)!;
        citizen.ActionCompletesMinute = snapshot.WorldMinute.Add(SimulationEngine.RemainingPathCost(path, snapshot.World));
        var due = snapshot.WorldMinute.Add(SimulationEngine.StepCost(path[0], path[1], snapshot.World));
        return ReplaceEvent(events, citizen, CitizenEventNames.MoveStep, due, CitizenEventNames.MovementPriority);
    }

    private static ScheduledEventSnapshot[] ConfigureBuild(IReadOnlyList<ScheduledEventSnapshot> events, SimulationPersistenceSnapshot snapshot, Citizen citizen, StructureId structureId)
    {
        citizen.CurrentAction = CitizenAction.Build;
        citizen.ActionPhase = CitizenActionPhase.Perform;
        citizen.ActionTarget = null;
        citizen.TargetStructureId = structureId;
        citizen.ActionStartedMinute = snapshot.WorldMinute;
        citizen.ActionCompletesMinute = snapshot.WorldMinute.Add(CitizenSimulationRules.ConstructionShiftDurationMinutes);
        return ReplaceEvent(events, citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
    }

    private static ScheduledEventSnapshot[] ConfigureRest(IReadOnlyList<ScheduledEventSnapshot> events, SimulationPersistenceSnapshot snapshot, Citizen citizen)
    {
        citizen.CurrentAction = CitizenAction.Rest;
        citizen.ActionPhase = CitizenActionPhase.Perform;
        citizen.ActionTarget = null;
        citizen.TargetResourceNodeId = null;
        citizen.TargetStructureId = null;
        citizen.ActionStartedMinute = snapshot.WorldMinute;
        citizen.ActionCompletesMinute = snapshot.WorldMinute.Add(CitizenSimulationRules.RestDurationMinutes);
        return ReplaceEvent(events, citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
    }

    private static ScheduledEventSnapshot[] ReplaceEvent(IReadOnlyList<ScheduledEventSnapshot> source, Citizen citizen, string name, WorldMinute due, int priority)
    {
        var index = source.ToList().FindIndex(item => item.Name is not CitizenEventNames.SurvivalCheck && item.Order.EntitySortKey == citizen.Id.Value);
        Assert.True(index >= 0);
        var events = source.ToArray();
        var current = events[index];
        events[index] = current with { Name = name, Order = new ScheduledEventOrder(due, priority, citizen.Id.Value, current.Order.Sequence) };
        return events;
    }

    private static ScheduledEventSnapshot[] FreezeActions(IReadOnlyList<ScheduledEventSnapshot> events, SimulationPersistenceSnapshot snapshot)
    {
        var frozen = events.ToArray();
        foreach (var citizen in snapshot.Citizens)
        {
            citizen.CurrentAction = CitizenAction.Idle;
            citizen.ActionPhase = CitizenActionPhase.Perform;
            citizen.ActionTarget = null;
            citizen.TargetResourceNodeId = null;
            citizen.TargetStructureId = null;
            citizen.ActionStartedMinute = snapshot.WorldMinute;
            citizen.ActionCompletesMinute = snapshot.WorldMinute.Add(10_000);
            frozen = ReplaceEvent(frozen, citizen, CitizenEventNames.ActionComplete, citizen.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
        }
        return frozen;
    }

    private static void ConfigureConstructionTransport(Citizen citizen, StructureId structureId, TileCoordinate target, TileCoordinate location, int carriedWood, WorldMap world)
    {
        citizen.Location = location;
        citizen.CurrentAction = CitizenAction.HaulConstruction;
        citizen.ActionPhase = CitizenActionPhase.TransportToConstruction;
        citizen.ActionTarget = target;
        citizen.TargetStructureId = structureId;
        citizen.CarriedResourceType = ResourceType.Wood;
        citizen.CarriedResourceQuantity = carriedWood;
        citizen.ActionStartedMinute = WorldMinute.Zero;
        citizen.ActionCompletesMinute = new WorldMinute(SimulationEngine.StepCost(location, target, world));
    }

    private static Structure Shelter(StructureId id, TileCoordinate site) => new(id, StructureType.Shelter, site, 0, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork);
    private static (Structure Structure, IReadOnlyList<StructureContribution> Contributions) CompleteShelter(StructureId id, TileCoordinate site, CitizenId contributor)
    {
        var structure = Shelter(id, site); structure.DeliveredWood = structure.RequiredWood; structure.DeliveredStone = structure.RequiredStone; structure.CompletedWork = structure.RequiredWork; structure.Status = StructureStatus.Complete; structure.CompletedMinute = 0;
        return (structure, new[] { new StructureContribution(id, contributor, structure.RequiredWork, structure.RequiredWood, structure.RequiredStone) });
    }
    private static (Structure Structure, IReadOnlyList<StructureContribution> Contributions) CompleteWorkshop(StructureId id, TileCoordinate site, CitizenId contributor)
    {
        var structure = new Structure(id, StructureType.Workshop, site, 0, CitizenSimulationRules.WorkshopRequiredWood, CitizenSimulationRules.WorkshopRequiredStone, CitizenSimulationRules.WorkshopRequiredWork) { DeliveredWood = CitizenSimulationRules.WorkshopRequiredWood, DeliveredStone = CitizenSimulationRules.WorkshopRequiredStone, CompletedWork = CitizenSimulationRules.WorkshopRequiredWork, Status = StructureStatus.Complete, CompletedMinute = 0 };
        return (structure, new[] { new StructureContribution(id, contributor, structure.RequiredWork, structure.RequiredWood, structure.RequiredStone) });
    }
    private static TileCoordinate[] ValidSites(WorldMap world, int count) => world.Tiles.Where(tile => tile.Coordinate != world.StartingSite && tile.Buildable && world.GetResources(tile.Coordinate).Count == 0).Select(tile => tile.Coordinate).Take(count).ToArray();
    private static void SetWork(Citizen citizen, long foraging, long wood, long stone, long construction, long hauling)
    {
        citizen.LifetimeForagingMinutes = foraging; citizen.LifetimeWoodcuttingMinutes = wood; citizen.LifetimeStoneworkingMinutes = stone; citizen.LifetimeConstructionMinutes = construction; citizen.LifetimeHaulingMinutes = hauling;
    }
    private static SimulationPersistenceSnapshot Copy(SimulationPersistenceSnapshot value, IReadOnlyList<ScheduledEventSnapshot>? events = null, SettlementState? settlement = null, IReadOnlyList<Structure>? structures = null, IReadOnlyList<StructureContribution>? contributions = null)
    {
        var effectiveStructures = structures ?? value.Structures;
        return new(value.Seed, value.WorldMinute, value.WorldSchemaVersion, value.SimulationRulesVersion, value.ApplicationVersion, value.WorldConfiguration, CountersAfterStructures(value.Counters, effectiveStructures), events ?? value.ScheduledEvents, value.World, value.Citizens, value.CitizenGenerationVersion, value.ResourceStates, settlement ?? value.Settlement, value.SurvivalVersion, value.SettlementVersion, effectiveStructures, contributions ?? value.StructureContributions);
    }
    private static DeterministicCountersSnapshot CountersAfterStructures(DeterministicCountersSnapshot counters, IReadOnlyList<Structure> structures)
    {
        var nextEntityId = structures.Count == 0 ? counters.NextEntityId : Math.Max(counters.NextEntityId, checked(structures.Max(x => x.Id.Value) + 1));
        return counters with { NextEntityId = nextEntityId };
    }
}
