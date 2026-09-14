using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M4SettlementCoreTests
{
    [Fact]
    public void M4EnumsAndCompatibilityConstantsAreExact()
    {
        Assert.Equal(10, (int)CitizenAction.HaulConstruction); Assert.Equal(11, (int)CitizenAction.Build);
        Assert.Equal(4, (int)CitizenActionPhase.TravelToStockpile); Assert.Equal(5, (int)CitizenActionPhase.TransportToConstruction); Assert.Equal(6, (int)CitizenActionPhase.WaitingForStorage);
        Assert.Equal(1, (int)StructureType.Shelter); Assert.Equal(2, (int)StructureType.Stockpile); Assert.Equal(3, (int)StructureType.Workshop);
        Assert.Equal(1, (int)StructureStatus.UnderConstruction); Assert.Equal(2, (int)StructureStatus.Complete);
        Assert.Equal("m4-rng1-settlement1", SimulationEngine.CurrentSimulationRulesVersion); Assert.Equal(800, CitizenSimulationRules.BaseStorageCapacity);
        Assert.Equal(40, CitizenSimulationRules.ShelterRequiredWood); Assert.Equal(10, CitizenSimulationRules.ShelterRequiredStone); Assert.Equal(600, CitizenSimulationRules.ShelterRequiredWork);
    }

    [Fact]
    public void DemandCreatesOneDeterministicallyValidShelter()
    {
        var engine = new SimulationEngine(new WorldSeed(42));
        engine.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SettlementDemandIntervalMinutes));
        var project = Assert.Single(engine.Structures);
        Assert.Equal(StructureType.Shelter, project.Type); Assert.Equal(StructureStatus.UnderConstruction, project.Status);
        Assert.NotEqual(engine.World.StartingSite, project.Location); Assert.True(engine.World.GetTile(project.Location).Buildable); Assert.Empty(engine.World.GetResources(project.Location));
    }

    [Fact]
    public void M4SnapshotAcceptsConstructionHaulingState()
    {
        var source = new SimulationEngine(new WorldSeed(42)); var baseSnapshot = source.CreatePersistenceSnapshot(); var citizen = baseSnapshot.Citizens[0];
        var site = source.World.Tiles.First(x => x.Buildable && x.Coordinate != source.World.StartingSite && source.World.GetResources(x.Coordinate).Count == 0).Coordinate;
        var route = source.World.Tiles.Select(x => DeterministicPathfinder.Find(source.World, x.Coordinate, source.World.StartingSite)).First(x => x is { Count: > 1 })!;
        citizen.Location = route[0]; citizen.CurrentAction = CitizenAction.HaulConstruction; citizen.ActionPhase = CitizenActionPhase.TravelToStockpile; citizen.TargetStructureId = new StructureId(baseSnapshot.Counters.NextEntityId); citizen.ActionTarget = source.World.StartingSite; citizen.ActionStartedMinute = new WorldMinute(0); citizen.ActionCompletesMinute = new WorldMinute(RouteCost(route, source.World));
        var project = new Structure(citizen.TargetStructureId.Value, StructureType.Shelter, site, 0, 40, 10, 600);
        var firstStepCost = StepCost(route[0], route[1], source.World);
        var events = baseSnapshot.ScheduledEvents.Select(x => x.Order.EntitySortKey == citizen.Id.Value && x.Name == CitizenEventNames.Decision ? x with { Name = CitizenEventNames.MoveStep, Order = new ScheduledEventOrder(new WorldMinute(firstStepCost), CitizenEventNames.MovementPriority, x.Order.EntitySortKey, x.Order.Sequence) } : x).ToArray();
        var snapshot = Copy(baseSnapshot, events: events, citizens: baseSnapshot.Citizens, structures: new[] { project });
        var restored = SimulationEngine.FromPersistenceSnapshot(snapshot);
        var actual = Assert.IsType<Citizen>(restored.GetCitizen(citizen.Id));
        Assert.Equal(CitizenAction.HaulConstruction, actual.CurrentAction); Assert.Equal(project.Id, actual.TargetStructureId);
    }

    [Fact]
    public void FullStorageRetainsCargoAndRetriesWithoutLoss()
    {
        var source = new SimulationEngine(new WorldSeed(42)); var baseSnapshot = source.CreatePersistenceSnapshot(); var citizen = baseSnapshot.Citizens[0]; var node = source.World.Resources.First(x => x.Type == ResourceType.Wood); var initialQuantity = source.GetResourceState(node.Id)!.CurrentQuantity;
        foreach (var other in baseSnapshot.Citizens.Skip(1)) { other.CurrentAction = CitizenAction.Idle; other.ActionPhase = CitizenActionPhase.Perform; other.ActionStartedMinute = new WorldMinute(0); other.ActionCompletesMinute = new WorldMinute(10_000); }
        citizen.Location = source.World.StartingSite; citizen.CurrentAction = CitizenAction.GatherWood; citizen.ActionPhase = CitizenActionPhase.WaitingForStorage; citizen.TargetResourceNodeId = node.Id; citizen.CarriedResourceType = ResourceType.Wood; citizen.CarriedResourceQuantity = 10; citizen.ActionStartedMinute = new WorldMinute(0); citizen.ActionCompletesMinute = new WorldMinute(CitizenSimulationRules.StorageRetryIntervalMinutes);
        baseSnapshot.Settlement!.WoodStored = CitizenSimulationRules.BaseStorageCapacity - baseSnapshot.Settlement.FoodStored;
        var events = baseSnapshot.ScheduledEvents.Select(x => x.Name == CitizenEventNames.Decision && x.Order.EntitySortKey == citizen.Id.Value ? x with { Name = CitizenEventNames.ActionComplete, Order = new ScheduledEventOrder(new WorldMinute(CitizenSimulationRules.StorageRetryIntervalMinutes), CitizenEventNames.CompletionPriority, x.Order.EntitySortKey, x.Order.Sequence) } : x.Name == CitizenEventNames.Decision ? x with { Name = CitizenEventNames.ActionComplete, Order = new ScheduledEventOrder(new WorldMinute(10_000), CitizenEventNames.CompletionPriority, x.Order.EntitySortKey, x.Order.Sequence) } : x).ToArray();
        var engine = SimulationEngine.FromPersistenceSnapshot(Copy(baseSnapshot, events: events));
        engine.AdvanceUntil(new WorldMinute(CitizenSimulationRules.StorageRetryIntervalMinutes));
        Assert.Equal(10, engine.GetCitizen(citizen.Id)!.CarriedResourceQuantity); Assert.Equal(CitizenActionPhase.WaitingForStorage, engine.GetCitizen(citizen.Id)!.ActionPhase);
        Assert.Equal(initialQuantity, engine.GetResourceState(node.Id)!.CurrentQuantity);
        var reloaded = SimulationEngine.FromPersistenceSnapshot(engine.CreatePersistenceSnapshot());
        reloaded.AdvanceUntil(new WorldMinute(CitizenSimulationRules.StorageRetryIntervalMinutes * 2));
        Assert.Equal(10, reloaded.GetCitizen(citizen.Id)!.CarriedResourceQuantity); Assert.Equal(CitizenActionPhase.WaitingForStorage, reloaded.GetCitizen(citizen.Id)!.ActionPhase);
        Assert.Equal(CitizenSimulationRules.BaseStorageCapacity - reloaded.Settlement.FoodStored, reloaded.Settlement.WoodStored); Assert.Equal(initialQuantity, reloaded.GetResourceState(node.Id)!.CurrentQuantity);
        var retryReloaded = SimulationEngine.FromPersistenceSnapshot(reloaded.CreatePersistenceSnapshot());
        retryReloaded.Settlement.WoodStored -= 10;
        retryReloaded.AdvanceUntil(new WorldMinute(CitizenSimulationRules.StorageRetryIntervalMinutes * 3));
        Assert.Equal(0, retryReloaded.GetCitizen(citizen.Id)!.CarriedResourceQuantity); Assert.Equal(CitizenSimulationRules.BaseStorageCapacity - retryReloaded.Settlement.FoodStored, retryReloaded.Settlement.WoodStored);
        Assert.Equal(initialQuantity, retryReloaded.GetResourceState(node.Id)!.CurrentQuantity);
    }

    [Fact]
    public void FullyReservedConstructionMaterialIsNotOfferedToAnotherHauler()
    {
        var source = new SimulationEngine(new WorldSeed(42));
        var snapshot = source.CreatePersistenceSnapshot();
        var site = NearbyValidSite(source.World);
        var project = new Structure(new StructureId(snapshot.Counters.NextEntityId), StructureType.Shelter, site, 0, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork) { DeliveredWood = CitizenSimulationRules.ShelterRequiredWood - CitizenSimulationRules.BaseConstructionCarryCapacity };
        var candidate = snapshot.Citizens[0];
        var carrier = snapshot.Citizens[1];
        var route = DeterministicPathfinder.Find(source.World, carrier.Location, site)!;
        carrier.CurrentAction = CitizenAction.HaulConstruction; carrier.ActionPhase = CitizenActionPhase.TransportToConstruction; carrier.TargetStructureId = project.Id; carrier.ActionTarget = site; carrier.ActionStartedMinute = snapshot.WorldMinute; carrier.ActionCompletesMinute = snapshot.WorldMinute.Add(RouteCost(route, source.World)); carrier.CarriedResourceType = ResourceType.Wood; carrier.CarriedResourceQuantity = CitizenSimulationRules.BaseConstructionCarryCapacity;
        var frozen = FreezeOtherInitialDecisions(snapshot, candidate.Id, carrier.Id);
        var events = frozen.Select(item => item.Name == CitizenEventNames.Decision && item.Order.EntitySortKey == carrier.Id.Value ? item with { Name = CitizenEventNames.MoveStep, Order = new ScheduledEventOrder(snapshot.WorldMinute.Add(StepCost(route[0], route[1], source.World)), CitizenEventNames.MovementPriority, item.Order.EntitySortKey, item.Order.Sequence) } : item).ToArray();
        var settlement = new SettlementState(snapshot.Settlement!.FoodStored, CitizenSimulationRules.BaseConstructionCarryCapacity, snapshot.Settlement.StoneStored, snapshot.Settlement.BaseStorageCapacity, snapshot.Settlement.DemandUpdatedMinute, snapshot.Settlement.ExposureConsequencesStartMinute);
        var engine = SimulationEngine.FromPersistenceSnapshot(Copy(snapshot, events: events, settlement: settlement, structures: new[] { project }, contributions: new[] { new StructureContribution(project.Id, candidate.Id, 0, project.DeliveredWood, 0) }));

        Assert.DoesNotContain(engine.EvaluateDecision(candidate.Id), value => value.Action == CitizenAction.HaulConstruction);
        Assert.Equal(1, engine.AdvanceUntil(snapshot.WorldMinute));
        Assert.NotEqual(CitizenAction.HaulConstruction, engine.GetCitizen(candidate.Id)!.CurrentAction);
        Assert.Equal(CitizenSimulationRules.BaseConstructionCarryCapacity, engine.Settlement.WoodStored);
        Assert.Equal(CitizenSimulationRules.BaseConstructionCarryCapacity, engine.GetCitizen(carrier.Id)!.CarriedResourceQuantity);
        Assert.Equal(1, engine.ProcessedEventCount);
    }

    [Fact]
    public void HaulerDeterministicallyFallsBackToAvailableStoneWhenWoodIsUnavailable()
    {
        var source = new SimulationEngine(new WorldSeed(42));
        var snapshot = source.CreatePersistenceSnapshot();
        var site = NearbyValidSite(source.World);
        var project = new Structure(new StructureId(snapshot.Counters.NextEntityId), StructureType.Shelter, site, 0, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork);
        var candidate = snapshot.Citizens[0];
        var events = FreezeOtherInitialDecisions(snapshot, candidate.Id);
        var citizens = snapshot.Citizens.Select(value => value.Id == candidate.Id ? CitizenWithDecisionTraits(value) : value).ToArray();
        var settlement = new SettlementState(0, 0, CitizenSimulationRules.ShelterRequiredStone, snapshot.Settlement!.BaseStorageCapacity, snapshot.Settlement.DemandUpdatedMinute, snapshot.Settlement.ExposureConsequencesStartMinute);
        var engine = SimulationEngine.FromPersistenceSnapshot(Copy(snapshot, events: events, citizens: citizens, settlement: settlement, structures: new[] { project }));

        Assert.Contains(engine.EvaluateDecision(candidate.Id), value => value.Action == CitizenAction.HaulConstruction);
        Assert.Equal(1, engine.AdvanceUntil(snapshot.WorldMinute));
        var hauling = engine.GetCitizen(candidate.Id)!;
        Assert.Equal(CitizenAction.HaulConstruction, hauling.CurrentAction); Assert.Equal(CitizenActionPhase.TransportToConstruction, hauling.ActionPhase);
        Assert.Equal(ResourceType.Stone, hauling.CarriedResourceType); Assert.Equal(CitizenSimulationRules.ShelterRequiredStone, hauling.CarriedResourceQuantity);
        Assert.Equal(0, engine.Settlement.StoneStored); Assert.Equal(0, engine.Settlement.WoodStored);
        Assert.True(engine.PendingEventCount > 0); Assert.Equal(1, engine.ProcessedEventCount);
    }

    [Fact]
    public void ExposureUsesTheResilienceAdjustedBaseDamage()
    {
        var source = new SimulationEngine(new WorldSeed(42)); var snapshot = source.CreatePersistenceSnapshot(); var citizen = snapshot.Citizens[0];
        citizen.Needs = new CitizenNeeds(0, 0, CitizenSimulationRules.ShelterCriticalThreshold, 0); citizen.NeedsUpdatedMinute = 0; citizen.Health = 10000; citizen.HealthUpdatedMinute = 0; snapshot.Settlement!.ExposureConsequencesStartMinute = 0;
        var engine = SimulationEngine.FromPersistenceSnapshot(snapshot); engine.AdvanceUntil(new WorldMinute(CitizenSimulationRules.SurvivalCheckIntervalMinutes));
        var expected = CitizenSimulationRules.ExposureDamagePerCheck * (10000 - citizen.Traits.Resilience / 4) / 10000;
        Assert.Equal(10000 - expected, engine.GetCitizen(citizen.Id)!.Health);
    }

    [Fact]
    public void SettlementFingerprintIsIndependentOfAdvanceChunking()
    {
        var whole = new SimulationEngine(new WorldSeed(42)); var chunked = new SimulationEngine(new WorldSeed(42));
        whole.AdvanceUntil(new WorldMinute(720)); chunked.AdvanceUntil(new WorldMinute(360)); chunked.AdvanceUntil(new WorldMinute(720));
        Assert.Equal(whole.ComputeSettlementFingerprint(), chunked.ComputeSettlementFingerprint());
    }

    [Fact]
    public void PostGraceSnapshotRemainsValidAfterTheGraceWindowHasElapsed()
    {
        var engine = new SimulationEngine(new WorldSeed(42), new WorldMinute(CitizenSimulationRules.ExposureGraceDurationMinutes + 1));
        var current = engine.CreatePersistenceSnapshot();
        var settlement = new SettlementState(current.Settlement!.FoodStored, current.Settlement.WoodStored, current.Settlement.StoneStored, current.Settlement.BaseStorageCapacity, current.Settlement.DemandUpdatedMinute, CitizenSimulationRules.ExposureGraceDurationMinutes);
        var snapshot = Copy(current, settlement: settlement);

        Assert.True(snapshot.WorldMinute.Value > snapshot.Settlement!.ExposureConsequencesStartMinute);
        var restored = SimulationEngine.FromPersistenceSnapshot(snapshot);
        Assert.Equal(snapshot.WorldMinute, restored.CurrentMinute);
        Assert.Equal(snapshot.Settlement.ExposureConsequencesStartMinute, restored.Settlement.ExposureConsequencesStartMinute);
    }

    [Fact]
    public void M4SnapshotRejectsConstructionAndRestTargetsThatDoNotMatchTheirRequiredState()
    {
        var source = new SimulationEngine(new WorldSeed(42));
        var snapshot = source.CreatePersistenceSnapshot();
        var sites = ValidSites(source.World, 2);
        var completedShelter = CompletedShelter(new StructureId(snapshot.Counters.NextEntityId), sites[0], snapshot.Citizens[0].Id);
        var buildCitizen = snapshot.Citizens[0];
        buildCitizen.CurrentAction = CitizenAction.Build; buildCitizen.ActionPhase = CitizenActionPhase.Perform; buildCitizen.TargetStructureId = completedShelter.Structure.Id; buildCitizen.ActionStartedMinute = snapshot.WorldMinute; buildCitizen.ActionCompletesMinute = snapshot.WorldMinute.Add(CitizenSimulationRules.ConstructionShiftDurationMinutes);
        var buildEvents = ReplaceDecisionWithActionComplete(snapshot.ScheduledEvents, buildCitizen, buildCitizen.ActionCompletesMinute.Value);
        Assert.Throws<ArgumentException>(() => Copy(snapshot, events: buildEvents, structures: new[] { completedShelter.Structure }, contributions: completedShelter.Contributions));

        var haulSnapshot = source.CreatePersistenceSnapshot();
        var haulCitizen = haulSnapshot.Citizens[0];
        var route = DeterministicPathfinder.Find(source.World, haulCitizen.Location, source.World.StartingSite)!;
        if (route.Count < 2) route = source.World.Tiles.Select(tile => DeterministicPathfinder.Find(source.World, tile.Coordinate, source.World.StartingSite)).First(path => path is { Count: > 1 })!;
        haulCitizen.Location = route[0]; haulCitizen.CurrentAction = CitizenAction.HaulConstruction; haulCitizen.ActionPhase = CitizenActionPhase.TravelToStockpile; haulCitizen.TargetStructureId = completedShelter.Structure.Id; haulCitizen.ActionTarget = source.World.StartingSite; haulCitizen.ActionStartedMinute = haulSnapshot.WorldMinute; haulCitizen.ActionCompletesMinute = haulSnapshot.WorldMinute.Add(RouteCost(route, source.World));
        var haulEvents = ReplaceDecisionWithMoveStep(haulSnapshot.ScheduledEvents, haulCitizen, haulSnapshot.WorldMinute.Add(StepCost(route[0], route[1], source.World)));
        Assert.Throws<ArgumentException>(() => Copy(haulSnapshot, events: haulEvents, structures: new[] { completedShelter.Structure }, contributions: completedShelter.Contributions));

        var restSnapshot = source.CreatePersistenceSnapshot();
        var restCitizen = restSnapshot.Citizens[0];
        var restRoute = DeterministicPathfinder.Find(source.World, restCitizen.Location, completedShelter.Structure.Location)!;
        if (restRoute.Count < 2) restRoute = source.World.Tiles.Select(tile => DeterministicPathfinder.Find(source.World, tile.Coordinate, completedShelter.Structure.Location)).First(path => path is { Count: > 1 })!;
        restCitizen.Location = restRoute[0]; restCitizen.HomeStructureId = completedShelter.Structure.Id; restCitizen.CurrentAction = CitizenAction.Rest; restCitizen.ActionPhase = CitizenActionPhase.TravelToTarget; restCitizen.ActionTarget = completedShelter.Structure.Location; restCitizen.TargetStructureId = null; restCitizen.ActionStartedMinute = restSnapshot.WorldMinute; restCitizen.ActionCompletesMinute = restSnapshot.WorldMinute.Add(RouteCost(restRoute, source.World));
        var restEvents = ReplaceDecisionWithMoveStep(restSnapshot.ScheduledEvents, restCitizen, restSnapshot.WorldMinute.Add(StepCost(restRoute[0], restRoute[1], source.World)));
        Assert.Throws<ArgumentException>(() => Copy(restSnapshot, events: restEvents, structures: new[] { completedShelter.Structure }, contributions: completedShelter.Contributions));
    }

    [Fact]
    public void FullStorageSuppressesGatherCandidatesAndConstructionUsesExactWeightedTravelPenalty()
    {
        var source = new SimulationEngine(new WorldSeed(42));
        var fullStorage = new SettlementState(CitizenSimulationRules.BaseStorageCapacity, 0, 0, CitizenSimulationRules.BaseStorageCapacity, 0, CitizenSimulationRules.ExposureGraceDurationMinutes);
        var fullEngine = SimulationEngine.FromPersistenceSnapshot(Copy(source.CreatePersistenceSnapshot(), settlement: fullStorage));
        var fullCandidates = fullEngine.EvaluateDecision(fullEngine.Citizens[0].Id).Select(value => value.Action);
        Assert.DoesNotContain(CitizenAction.GatherFood, fullCandidates); Assert.DoesNotContain(CitizenAction.GatherWood, fullCandidates); Assert.DoesNotContain(CitizenAction.GatherStone, fullCandidates);

        var haulingSnapshot = source.CreatePersistenceSnapshot();
        var site = ValidSites(source.World, 1)[0];
        var haulingProject = new Structure(new StructureId(haulingSnapshot.Counters.NextEntityId), StructureType.Shelter, site, 0, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork);
        var haulingSettlement = new SettlementState(0, CitizenSimulationRules.ShelterRequiredWood, 0, CitizenSimulationRules.BaseStorageCapacity, 0, CitizenSimulationRules.ExposureGraceDurationMinutes);
        var haulingEngine = SimulationEngine.FromPersistenceSnapshot(Copy(haulingSnapshot, settlement: haulingSettlement, structures: new[] { haulingProject }));
        var haulingCitizen = haulingEngine.Citizens[0];
        var haulingPath = DeterministicPathfinder.Find(haulingEngine.World, haulingCitizen.Location, site)!;
        Assert.Equal(RouteCost(haulingPath, haulingEngine.World), haulingEngine.EvaluateDecision(haulingCitizen.Id).Single(value => value.Action == CitizenAction.HaulConstruction).TravelPenalty);

        var buildingSnapshot = source.CreatePersistenceSnapshot();
        var buildingProject = new Structure(new StructureId(buildingSnapshot.Counters.NextEntityId), StructureType.Shelter, site, 0, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork) { DeliveredWood = CitizenSimulationRules.ShelterRequiredWood, DeliveredStone = CitizenSimulationRules.ShelterRequiredStone };
        var buildingEngine = SimulationEngine.FromPersistenceSnapshot(Copy(buildingSnapshot, structures: new[] { buildingProject }, contributions: new[] { new StructureContribution(buildingProject.Id, buildingSnapshot.Citizens[0].Id, 0, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone) }));
        var buildingCitizen = buildingEngine.Citizens[0];
        var buildingPath = DeterministicPathfinder.Find(buildingEngine.World, buildingCitizen.Location, site)!;
        Assert.Equal(RouteCost(buildingPath, buildingEngine.World), buildingEngine.EvaluateDecision(buildingCitizen.Id).Single(value => value.Action == CitizenAction.Build).TravelPenalty);
    }

    [Fact]
    public void ReadSnapshotDeepCopiesAndOrdersStructuresAndContributions()
    {
        var baseline = new SimulationEngine(new WorldSeed(42)).CreatePersistenceSnapshot();
        var sites = baseline.World!.Tiles.Where(tile => tile.Coordinate != baseline.World.StartingSite && tile.Buildable && baseline.World.GetResources(tile.Coordinate).Count == 0).Select(tile => tile.Coordinate).Take(3).ToArray();
        Assert.Equal(3, sites.Length);
        var shelter = Complete(new Structure(new StructureId(10001), StructureType.Shelter, sites[0], 0, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork), CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork);
        var stockpile = Complete(new Structure(new StructureId(10002), StructureType.Stockpile, sites[1], 0, CitizenSimulationRules.StockpileRequiredWood, CitizenSimulationRules.StockpileRequiredStone, CitizenSimulationRules.StockpileRequiredWork), CitizenSimulationRules.StockpileRequiredWood, CitizenSimulationRules.StockpileRequiredStone, CitizenSimulationRules.StockpileRequiredWork);
        var project = new Structure(new StructureId(10003), StructureType.Workshop, sites[2], 0, CitizenSimulationRules.WorkshopRequiredWood, CitizenSimulationRules.WorkshopRequiredStone, CitizenSimulationRules.WorkshopRequiredWork) { DeliveredWood = 4, DeliveredStone = 3, CompletedWork = 10 };
        baseline.Citizens[0].LifetimeConstructionMinutes = 30;
        baseline.Citizens[1].LifetimeHaulingMinutes = 12;
        var contributions = new[]
        {
            new StructureContribution(project.Id, baseline.Citizens[0].Id, 10, 4, 3),
            new StructureContribution(stockpile.Id, baseline.Citizens[1].Id, CitizenSimulationRules.StockpileRequiredWork, CitizenSimulationRules.StockpileRequiredWood, CitizenSimulationRules.StockpileRequiredStone),
            new StructureContribution(shelter.Id, baseline.Citizens[0].Id, CitizenSimulationRules.ShelterRequiredWork, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone)
        };
        var snapshot = new SimulationPersistenceSnapshot(baseline.Seed, baseline.WorldMinute, baseline.WorldSchemaVersion, baseline.SimulationRulesVersion, baseline.ApplicationVersion, baseline.WorldConfiguration, baseline.Counters, baseline.ScheduledEvents, baseline.World, baseline.Citizens, baseline.CitizenGenerationVersion, baseline.ResourceStates, baseline.Settlement, baseline.SurvivalVersion, baseline.SettlementVersion, new[] { project, stockpile, shelter }, contributions);
        var engine = SimulationEngine.FromPersistenceSnapshot(snapshot);
        var fingerprint = engine.ComputeSettlementFingerprint();

        var read = engine.CreateReadSnapshot();

        var readSettlement = Assert.IsType<SettlementState>(read.Settlement);
        Assert.NotSame(readSettlement, engine.Settlement);
        Assert.Equal(engine.Settlement.BaseStorageCapacity, readSettlement.BaseStorageCapacity);
        Assert.Equal(engine.Settlement.DemandUpdatedMinute, readSettlement.DemandUpdatedMinute);
        Assert.Equal(engine.Settlement.ExposureConsequencesStartMinute, readSettlement.ExposureConsequencesStartMinute);
        Assert.Equal(new long[] { 10001, 10002, 10003 }, read.Structures.Select(x => x.Id.Value));
        Assert.Equal(new[] { (10001L, baseline.Citizens[0].Id.Value), (10002L, baseline.Citizens[1].Id.Value), (10003L, baseline.Citizens[0].Id.Value) }, read.StructureContributions.Select(x => (x.StructureId.Value, x.CitizenId.Value)));
        Assert.NotSame(read.Structures[0], engine.GetStructure(read.Structures[0].Id));
        Assert.NotSame(read.StructureContributions[0], engine.StructureContributions[0]);

        read.Structures[0].DeliveredWood = 0;
        read.Structures[0].CompletedWork = 0;
        read.StructureContributions[0].ConstructionWork = 0;
        read.StructureContributions[0].WoodDelivered = 0;
        readSettlement.BaseStorageCapacity = 0;
        readSettlement.DemandUpdatedMinute = 1;
        readSettlement.ExposureConsequencesStartMinute = 2;

        Assert.Equal(fingerprint, engine.ComputeSettlementFingerprint());
        Assert.Equal(CitizenSimulationRules.BaseStorageCapacity, engine.Settlement.BaseStorageCapacity);
        Assert.Equal(0, engine.Settlement.DemandUpdatedMinute);
        Assert.Equal(CitizenSimulationRules.ExposureGraceDurationMinutes, engine.Settlement.ExposureConsequencesStartMinute);
        Assert.Equal(CitizenSimulationRules.ShelterRequiredWood, engine.GetStructure(shelter.Id)!.DeliveredWood);
        Assert.Equal(CitizenSimulationRules.ShelterRequiredWork, engine.GetStructure(shelter.Id)!.CompletedWork);
        Assert.Equal(CitizenSimulationRules.ShelterRequiredWork, engine.StructureContributions.Single(x => x.StructureId == shelter.Id && x.CitizenId == baseline.Citizens[0].Id).ConstructionWork);
        Assert.Equal(CitizenSimulationRules.ShelterRequiredWood, engine.StructureContributions.Single(x => x.StructureId == shelter.Id && x.CitizenId == baseline.Citizens[0].Id).WoodDelivered);
    }

    [Fact]
    public void M4SnapshotRejectsOffCadenceSurvivalAndInvalidActionEvents()
    {
        var snapshot = new SimulationEngine(new WorldSeed(42)).CreatePersistenceSnapshot();
        var survival = snapshot.ScheduledEvents.First(x => x.Name == CitizenEventNames.SurvivalCheck);
        var badSurvival = snapshot.ScheduledEvents.Select(x => x.Id == survival.Id ? x with { Order = new ScheduledEventOrder(survival.Order.DueWorldMinute.Add(1), survival.Order.Priority, survival.Order.EntitySortKey, survival.Order.Sequence) } : x).ToArray();
        Assert.Throws<ArgumentException>(() => Copy(snapshot, events: badSurvival));
        var decision = snapshot.ScheduledEvents.First(x => x.Name == CitizenEventNames.Decision);
        var badAction = snapshot.ScheduledEvents.Select(x => x.Id == decision.Id ? x with { Order = new ScheduledEventOrder(new WorldMinute(1), decision.Order.Priority, decision.Order.EntitySortKey, decision.Order.Sequence) } : x).ToArray();
        Assert.Throws<ArgumentException>(() => Copy(snapshot, events: badAction));
    }

    [Fact]
    public void M4SnapshotRejectsMissingOrMalformedDemandEvent()
    {
        var snapshot = new SimulationEngine(new WorldSeed(42)).CreatePersistenceSnapshot();
        Assert.Throws<ArgumentException>(() => Copy(snapshot, events: snapshot.ScheduledEvents.Where(x => x.Name != CitizenEventNames.SettlementEvaluateDemand).ToArray()));
        var demand = snapshot.ScheduledEvents.Single(x => x.Name == CitizenEventNames.SettlementEvaluateDemand);
        var badPriority = snapshot.ScheduledEvents.Select(x => x.Id == demand.Id ? x with { Order = new ScheduledEventOrder(demand.Order.DueWorldMinute, demand.Order.Priority + 1, demand.Order.EntitySortKey, demand.Order.Sequence) } : x).ToArray();
        Assert.Throws<ArgumentException>(() => Copy(snapshot, events: badPriority));
        var badDue = snapshot.ScheduledEvents.Select(x => x.Id == demand.Id ? x with { Order = new ScheduledEventOrder(demand.Order.DueWorldMinute.Add(1), demand.Order.Priority, demand.Order.EntitySortKey, demand.Order.Sequence) } : x).ToArray();
        Assert.Throws<ArgumentException>(() => Copy(snapshot, events: badDue));
        var badPayload = snapshot.ScheduledEvents.Select(x => x.Id == demand.Id ? x with { PayloadJson = "{}" } : x).ToArray();
        Assert.Throws<ArgumentException>(() => Copy(snapshot, events: badPayload));
    }

    private static long RouteCost(IReadOnlyList<TileCoordinate> route, WorldMap world) => Enumerable.Range(1, route.Count - 1).Sum(index => StepCost(route[index - 1], route[index], world));
    private static long StepCost(TileCoordinate from, TileCoordinate to, WorldMap world) => ((from.X == to.X || from.Y == to.Y) ? 10L : 14L) * world.GetTile(to).MovementCost;
    private static Structure Complete(Structure structure, int wood, int stone, int work)
    {
        structure.Status = StructureStatus.Complete;
        structure.CompletedMinute = 0;
        structure.DeliveredWood = wood;
        structure.DeliveredStone = stone;
        structure.CompletedWork = work;
        return structure;
    }

    private static TileCoordinate[] ValidSites(WorldMap world, int count) => world.Tiles.Where(tile => tile.Coordinate != world.StartingSite && tile.Buildable && world.GetResources(tile.Coordinate).Count == 0).Select(tile => tile.Coordinate).Take(count).ToArray();
    private static TileCoordinate NearbyValidSite(WorldMap world) => world.Tiles.Where(tile => tile.Coordinate != world.StartingSite && tile.Buildable && world.GetResources(tile.Coordinate).Count == 0).OrderBy(tile => Math.Abs(tile.Coordinate.X - world.StartingSite.X) + Math.Abs(tile.Coordinate.Y - world.StartingSite.Y)).ThenBy(tile => tile.Coordinate.ToIndex(world.Width)).First().Coordinate;
    private static (Structure Structure, IReadOnlyList<StructureContribution> Contributions) CompletedShelter(StructureId id, TileCoordinate site, CitizenId contributor)
    {
        var structure = Complete(new Structure(id, StructureType.Shelter, site, 0, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork), CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork);
        return (structure, new[] { new StructureContribution(structure.Id, contributor, CitizenSimulationRules.ShelterRequiredWork, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone) });
    }
    private static ScheduledEventSnapshot[] ReplaceDecisionWithActionComplete(IReadOnlyList<ScheduledEventSnapshot> events, Citizen citizen, WorldMinute due) => events.Select(item => item.Order.EntitySortKey == citizen.Id.Value && item.Name == CitizenEventNames.Decision ? item with { Name = CitizenEventNames.ActionComplete, Order = new ScheduledEventOrder(due, CitizenEventNames.CompletionPriority, item.Order.EntitySortKey, item.Order.Sequence) } : item).ToArray();
    private static ScheduledEventSnapshot[] ReplaceDecisionWithMoveStep(IReadOnlyList<ScheduledEventSnapshot> events, Citizen citizen, WorldMinute due) => events.Select(item => item.Order.EntitySortKey == citizen.Id.Value && item.Name == CitizenEventNames.Decision ? item with { Name = CitizenEventNames.MoveStep, Order = new ScheduledEventOrder(due, CitizenEventNames.MovementPriority, item.Order.EntitySortKey, item.Order.Sequence) } : item).ToArray();
    private static Citizen CitizenWithDecisionTraits(Citizen value) => new(value.Id, value.FounderOrdinal, value.GivenName, value.FamilyName, value.BirthMinute, value.Location, new CitizenTraits(10_000, 0, 0, 0, 0, value.Traits.Resilience), new CitizenSkills(value.Skills.Foraging, value.Skills.Woodcutting, value.Skills.Stoneworking, value.Skills.Construction, value.Skills.Hauling, value.Skills.Domestic), value.Needs) { Health = value.Health, NeedsUpdatedMinute = value.NeedsUpdatedMinute, HealthUpdatedMinute = value.HealthUpdatedMinute };
    private static ScheduledEventSnapshot[] FreezeOtherInitialDecisions(SimulationPersistenceSnapshot snapshot, params CitizenId[] activeCitizens)
    {
        var active = activeCitizens.Select(value => value.Value).ToHashSet();
        foreach (var citizen in snapshot.Citizens.Where(value => !active.Contains(value.Id.Value))) { citizen.CurrentAction = CitizenAction.Idle; citizen.ActionPhase = CitizenActionPhase.Perform; citizen.ActionStartedMinute = snapshot.WorldMinute; citizen.ActionCompletesMinute = snapshot.WorldMinute.Add(10_000); }
        return snapshot.ScheduledEvents.Select(item => item.Name == CitizenEventNames.Decision && !active.Contains(item.Order.EntitySortKey) ? item with { Name = CitizenEventNames.ActionComplete, Order = new ScheduledEventOrder(snapshot.WorldMinute.Add(10_000), CitizenEventNames.CompletionPriority, item.Order.EntitySortKey, item.Order.Sequence) } : item).ToArray();
    }
    private static SimulationPersistenceSnapshot Copy(SimulationPersistenceSnapshot value, IReadOnlyList<ScheduledEventSnapshot>? events = null, IReadOnlyList<Citizen>? citizens = null, SettlementState? settlement = null, IReadOnlyList<Structure>? structures = null, IReadOnlyList<StructureContribution>? contributions = null)
        => new(value.Seed, value.WorldMinute, value.WorldSchemaVersion, value.SimulationRulesVersion, value.ApplicationVersion, value.WorldConfiguration, value.Counters, events ?? value.ScheduledEvents, value.World, citizens ?? value.Citizens, value.CitizenGenerationVersion, value.ResourceStates, settlement ?? value.Settlement, value.SurvivalVersion, value.SettlementVersion, structures ?? value.Structures, contributions ?? value.StructureContributions);
}
