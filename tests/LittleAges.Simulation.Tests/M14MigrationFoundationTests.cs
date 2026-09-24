using System.Text.Json;
using System.Reflection;
using System.Collections;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M14MigrationFoundationTests
{
    [Fact]
    public void M14StartsWithOneSiteAndMatchesM13FirstSettlementSystems()
    {
        var seed = new WorldSeed(42);
        var m13 = new SimulationEngine(seed, simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
        var m14 = new SimulationEngine(seed, simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        var m13Snapshot = m13.CreatePersistenceSnapshot();
        var m14Snapshot = m14.CreatePersistenceSnapshot();

        Assert.Equal(SimulationEngine.MigrationSimulationRulesVersion, SimulationEngine.CurrentSimulationRulesVersion);
        Assert.True(SimulationEngine.UnifiedSimulationRulesEnabled(SimulationEngine.MigrationSimulationRulesVersion));
        Assert.True(SimulationEngine.LivingSystemsEnabled(SimulationEngine.MigrationSimulationRulesVersion));
        Assert.True(SimulationEngine.SocialSystemsEnabled(SimulationEngine.MigrationSimulationRulesVersion));
        Assert.True(SimulationEngine.HistorySystemsEnabled(SimulationEngine.MigrationSimulationRulesVersion));
        Assert.True(SimulationEngine.GrowthSystemsEnabled(SimulationEngine.MigrationSimulationRulesVersion));
        Assert.True(SimulationEngine.AgricultureSystemsEnabled(SimulationEngine.MigrationSimulationRulesVersion));
        Assert.True(SimulationEngine.EconomySystemsEnabled(SimulationEngine.MigrationSimulationRulesVersion));

        Assert.Equal(m13.World!.Fingerprint, m14Snapshot.World!.Fingerprint);
        Assert.Equal(CommonState(m13Snapshot), CommonState(m14Snapshot));
        Assert.Equal(m13Snapshot.LivingStateJson, m14Snapshot.LivingStateJson);

        var state = Assert.IsType<MigrationWorldState>(m14Snapshot.MigrationState);
        Assert.Null(state.DaughterSettlement);
        Assert.All(state.CitizenResidences, residence => Assert.Equal(1, residence.SettlementId));
        Assert.All(state.HouseholdResidences, residence => Assert.Equal(1, residence.SettlementId));
        Assert.All(state.StructureOwners, residence => Assert.Equal(1, residence.SettlementId));
        Assert.All(state.FacilityOwners, residence => Assert.Equal(1, residence.SettlementId));
        Assert.All(state.WorkOrderOwners, residence => Assert.Equal(1, residence.SettlementId));

        var projection = m14.CreateMigrationReadSnapshot();
        var site = Assert.Single(projection.Settlements);
        Assert.Equal(1, site.Id);
        Assert.Equal(m14.World.StartingSite, site.Site);
        Assert.Equal(MigrationSettlementStockState.From(m14.Settlement), site.CommunalStock);
        Assert.Equal(LivingWorldCodec.Deserialize(m14Snapshot.LivingStateJson!).Stock, site.LivingGoods);
        Assert.Equal(m14Snapshot.Citizens.Select(x => x.Id.Value), site.CitizenIds);
        Assert.Equal(m14Snapshot.Households.Select(x => x.Id.Value), site.HouseholdIds);
        Assert.Equal(m14Snapshot.Structures.Select(x => x.Id.Value), site.StructureIds);
        Assert.Equal(projection.Fingerprint, m14.CreateMigrationReadSnapshot().Fingerprint);

        // Existing Living IDs remain in their shared Living namespace, but ownership never restarts them per site.
        Assert.Equal(state.FacilityOwners.Select(x => x.EntityId).Distinct().Count(), state.FacilityOwners.Count);
        Assert.Equal(state.WorkOrderOwners.Select(x => x.EntityId).Distinct().Count(), state.WorkOrderOwners.Count);

        var oneDay = new WorldMinute(1_440);
        m13.AdvanceUntil(oneDay);
        m14.AdvanceUntil(oneDay);
        Assert.Equal(CommonState(m13.CreatePersistenceSnapshot()), CommonState(m14.CreatePersistenceSnapshot()));
    }

    [Fact]
    public void Seed17M14KeepsCitizen13AliveThroughPriorDeathMinute()
    {
        var engine = new SimulationEngine(new WorldSeed(17),
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(34_200));
        var citizen = engine.Citizens.Single(x => x.CitizenId.Value == 13);
        Assert.True(citizen.IsAlive, $"Citizen 13 died at minute {citizen.DeathMinute} from {citizen.DeathCause}.");
    }

    [Fact]
    public void Seed17M14LivingOutputReservesSpaceForCarriedM12CitizenCargo()
    {
        var engine = new SimulationEngine(new WorldSeed(17),
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        var storageBoundary = new WorldMinute(5 * WorldCalendar.MinutesPerYear +
            90L * WorldCalendar.MinutesPerDay);
        engine.AdvanceUntil(storageBoundary);

        LivingValidation.Validate(engine.CreatePersistenceSnapshot());
    }

    [Fact]
    public void M14DaughterPrioritizesItsInitialFarmAfterShelterChecksAndPreservesBaselineDemandOrder()
    {
        var seed = new WorldSeed(913);
        var m13 = new SimulationEngine(seed,
            simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
        AddSheltersForDemandChecks(m13, 1, m13.World!.StartingSite, migration: false);
        Assert.Equal(StructureType.Marketplace, InvokePrivate(m13, "SelectSettlementDemand"));

        var fixture = CreateDaughterFixture(seed);
        var engine = SimulationEngine.FromPersistenceSnapshot(fixture.Snapshot);
        AddSheltersForDemandChecks(engine, 1, engine.World.StartingSite, migration: true);
        AddSheltersForDemandChecks(engine, 2, fixture.DaughterSite, migration: true);

        // The original site still completes its existing Marketplace-before-Granary sequence.
        Assert.Equal(StructureType.Granary, InvokePrivate(engine, "SelectSettlementDemand", 1L));

        var structures = GetPrivateField<Dictionary<long, Structure>>(engine, "_structures");
        var migrationState = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        var daughterMarketplace = structures.Values.Single(x => x.Type == StructureType.Marketplace &&
            (long)InvokePrivate(engine, "SiteIdForStructure", x.Id.Value)! == 2);
        structures.Remove(daughterMarketplace.Id.Value);
        SetPrivateField(engine, "_migrationState", new MigrationWorldState(migrationState.Version,
            migrationState.CitizenResidences, migrationState.HouseholdResidences,
            migrationState.StructureOwners.Where(x => x.EntityId != daughterMarketplace.Id.Value).ToArray(),
            migrationState.FacilityOwners, migrationState.WorkOrderOwners, migrationState.DaughterSettlement,
            migrationState.InTransitParties, migrationState.FoundingPressure, migrationState.LastRelocations,
            migrationState.LastVisitAttemptYear));

        Assert.DoesNotContain(structures.Values, x => x.Type == StructureType.Farm &&
            x.Status == StructureStatus.Complete &&
            (long)InvokePrivate(engine, "SiteIdForStructure", x.Id.Value)! == 2);
        var farmSite = Assert.IsType<TileCoordinate>(InvokePrivate(engine, "SelectFarmSite", 2L));
        Assert.Equal(StructureType.Farm, InvokePrivate(engine, "SelectSettlementDemand", 2L));

        // The first Granary now precedes the daughter's Marketplace after the initial Farm.
        var counters = GetPrivateField<DeterministicCounters>(engine, "_counters");
        var farmId = counters.AllocateStructureId();
        var farm = new Structure(farmId, StructureType.Farm, farmSite, engine.CurrentMinute.Value,
            AgricultureRules.FarmWood, AgricultureRules.FarmStone, AgricultureRules.FarmWork)
        {
            Status = StructureStatus.Complete,
            CompletedMinute = engine.CurrentMinute.Value,
            DeliveredWood = AgricultureRules.FarmWood,
            DeliveredStone = AgricultureRules.FarmStone,
            CompletedWork = AgricultureRules.FarmWork
        };
        structures.Add(farmId.Value, farm);
        migrationState = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        SetPrivateField(engine, "_migrationState", WithAdditionalStructureOwners(migrationState, [farm], [2]));
        Assert.Equal(StructureType.Granary, InvokePrivate(engine, "SelectSettlementDemand", 2L));

        var granaryId = counters.AllocateStructureId();
        var granarySite = FindFreeBuildableTile(engine, 2, fixture.DaughterSite, structures.Values.ToArray(), []);
        var granary = new Structure(granaryId, StructureType.Granary, granarySite, engine.CurrentMinute.Value,
            AgricultureRules.GranaryWood, AgricultureRules.GranaryStone, AgricultureRules.GranaryWork)
        {
            Status = StructureStatus.Complete,
            CompletedMinute = engine.CurrentMinute.Value,
            DeliveredWood = AgricultureRules.GranaryWood,
            DeliveredStone = AgricultureRules.GranaryStone,
            CompletedWork = AgricultureRules.GranaryWork
        };
        structures.Add(granaryId.Value, granary);
        migrationState = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        SetPrivateField(engine, "_migrationState", WithAdditionalStructureOwners(migrationState, [granary], [2]));
        Assert.Equal(StructureType.Marketplace, InvokePrivate(engine, "SelectSettlementDemand", 2L));
        MoveOneLivingHouseholdToSite(engine, 2L, fixture.DaughterSite);
        Assert.Equal(StructureType.Farm, InvokePrivate(engine, "SelectAgricultureDemand", 2L));
    }

    [Fact]
    public void FoundingSiteRequiresReachableDaughterFoodAndFarmlandAndChoosesTheNearestViableSeed17Site()
    {
        var engine = new SimulationEngine(new WorldSeed(17),
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        var members = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens").Values
            .Where(x => x.IsAlive).OrderBy(x => x.Id.Value).Take(2).ToArray();
        var site = Assert.IsType<TileCoordinate>(InvokePrivate(engine, "FindFoundingSite", (object)members));
        var originCosts = LivingTravelCosts.Compute(engine.World, engine.World.StartingSite);
        var siteCosts = LivingTravelCosts.Compute(engine.World, site);
        var nearbyOwnedFood = engine.World.Resources.Count(node => node.Type == ResourceType.Food &&
            Math.Abs(node.Coordinate.X - site.X) + Math.Abs(node.Coordinate.Y - site.Y) <= 6 &&
            originCosts.TryGetValue(node.Coordinate, out var originCost) &&
            siteCosts.TryGetValue(node.Coordinate, out var siteCost) && siteCost < originCost);
        var reachableOwnedFarmland = engine.World.Tiles.Any(tile => tile.Coordinate != site &&
            AgricultureRules.Suitable(tile) && originCosts.TryGetValue(tile.Coordinate, out var originCost) &&
            siteCosts.TryGetValue(tile.Coordinate, out var siteCost) && siteCost < originCost);

        Assert.True(nearbyOwnedFood >= 2, $"Selected {site} has only {nearbyOwnedFood} nearby daughter-owned food nodes.");
        Assert.Equal(new TileCoordinate(124, 150), site);
        Assert.True(reachableOwnedFarmland);
    }

    [Fact]
    public void FoundingSiteFallsBackToNoSiteWhenNoDaughterFarmlandIsAvailable()
    {
        var engine = new SimulationEngine(new WorldSeed(42),
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        var structures = GetPrivateField<Dictionary<long, Structure>>(engine, "_structures");
        var occupied = structures.Values.Select(x => x.Location).ToHashSet();
        var resources = engine.World.Resources.Select(x => x.Coordinate).ToHashSet();
        var counters = GetPrivateField<DeterministicCounters>(engine, "_counters");
        foreach (var tile in engine.World.Tiles.Where(x => AgricultureRules.Suitable(x) &&
                     !occupied.Contains(x.Coordinate) && !resources.Contains(x.Coordinate)))
        {
            var id = counters.AllocateStructureId();
            structures.Add(id.Value, new Structure(id, StructureType.Shelter, tile.Coordinate,
                engine.CurrentMinute.Value, StructureDefinitions.ShelterRequiredWood,
                StructureDefinitions.ShelterRequiredStone, StructureDefinitions.ShelterRequiredWork));
        }

        Assert.Null(InvokePrivate(engine, "FindFoundingSite", (object)engine.Citizens.ToArray()));
    }

    [Fact]
    public void DaughterHarvestWinsLowFoodWorkWhileOriginKeepsItsExistingGatherDecision()
    {
        var fixture = CreateDaughterFixture(new WorldSeed(917));
        var engine = SimulationEngine.FromPersistenceSnapshot(fixture.Snapshot);
        var autumnMinute = new WorldMinute(5L * WorldCalendar.MinutesPerYear +
            2L * WorldCalendar.DaysPerSeason * WorldCalendar.MinutesPerDay + 10);
        SetCurrentMinute(engine, autumnMinute);

        var daughterFarmSite = Assert.IsType<TileCoordinate>(InvokePrivate(engine, "SelectFarmSite", 2L));
        var daughterFarm = AddCompletedHarvestFarm(engine, 2L, daughterFarmSite, 100);
        var daughter = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens").Values
            .First(x => x.IsAlive && SiteIdForCitizen(engine, x) == 2);
        daughter.Location = fixture.DaughterSite;
        daughter.Needs = new CitizenNeeds(5000, 0, 0, 0);
        daughter.NeedsUpdatedMinute = autumnMinute.Value;
        ((SettlementState)InvokePrivate(engine, "SettlementFor", 2L)!).FoodStored = 0;

        var daughterDecisions = Assert.IsType<List<CitizenDecisionEvaluation>>(
            InvokePrivate(engine, "EvaluateGrowthDecision", daughter));
        Assert.Contains(daughterDecisions, decision => decision.Action == CitizenAction.HaulHarvest &&
            decision.BaseUtility >= 34200);
        Assert.DoesNotContain(daughterDecisions, decision => decision.Action == CitizenAction.GatherFood);
        Assert.Equal(daughterFarm.Id, ((Structure)InvokePrivate(engine, "SelectFarmTarget", daughter,
            CitizenAction.HaulHarvest)!).Id);

        var originSite = Assert.IsType<TileCoordinate>(InvokePrivate(engine, "SelectFarmSite", 1L));
        AddCompletedHarvestFarm(engine, 1L, originSite, 100);
        var origin = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens").Values
            .First(x => x.IsAlive && SiteIdForCitizen(engine, x) == 1);
        origin.Location = engine.World.StartingSite;
        origin.Needs = new CitizenNeeds(5000, 0, 0, 0);
        origin.NeedsUpdatedMinute = autumnMinute.Value;
        ((SettlementState)InvokePrivate(engine, "SettlementFor", 1L)!).FoodStored = 0;

        var originDecisions = Assert.IsType<List<CitizenDecisionEvaluation>>(
            InvokePrivate(engine, "EvaluateGrowthDecision", origin));
        Assert.Contains(originDecisions, decision => decision.Action == CitizenAction.GatherFood);
        Assert.Contains(originDecisions, decision => decision.Action == CitizenAction.HaulHarvest &&
            decision.BaseUtility < 34200);
    }

    [Fact]
    public void M14DaughterHarvestTransfersLargerPhysicalLoadsWithoutChangingOriginOrM13()
    {
        var autumnMinute = new WorldMinute(5L * WorldCalendar.MinutesPerYear +
            2L * WorldCalendar.DaysPerSeason * WorldCalendar.MinutesPerDay + 10);
        var daughterFixture = CreateDaughterFixture(new WorldSeed(918));
        var m14 = SimulationEngine.FromPersistenceSnapshot(daughterFixture.Snapshot);
        SetCurrentMinute(m14, autumnMinute);
        var daughterFarmSite = Assert.IsType<TileCoordinate>(InvokePrivate(m14, "SelectFarmSite", 2L));
        var daughterFarm = AddCompletedHarvestFarm(m14, 2L, daughterFarmSite, 300);
        var daughter = GetPrivateField<Dictionary<long, Citizen>>(m14, "_citizens").Values
            .First(x => x.IsAlive && SiteIdForCitizen(m14, x) == 2 && x.AgeYears(autumnMinute) >= 13);
        var daughterTransfer = HarvestAndDeliver(m14, 2L, daughter, daughterFarm);

        var originSite = Assert.IsType<TileCoordinate>(InvokePrivate(m14, "SelectFarmSite", 1L));
        var originFarm = AddCompletedHarvestFarm(m14, 1L, originSite, 300);
        var origin = GetPrivateField<Dictionary<long, Citizen>>(m14, "_citizens").Values
            .First(x => x.IsAlive && SiteIdForCitizen(m14, x) == 1 && x.AgeYears(autumnMinute) >= 13);
        var originTransfer = HarvestAndDeliver(m14, 1L, origin, originFarm);

        var m13 = new SimulationEngine(new WorldSeed(918),
            simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
        SetCurrentMinute(m13, autumnMinute);
        var m13FarmSite = Assert.IsType<TileCoordinate>(InvokePrivate(m13, "SelectFarmSite"));
        var m13Farm = AddCompletedHarvestFarm(m13, 1L, m13FarmSite, 300);
        var m13Farmer = GetPrivateField<Dictionary<long, Citizen>>(m13, "_citizens").Values
            .First(x => x.IsAlive && x.AgeYears(autumnMinute) >= 13);
        var m13Transfer = HarvestAndDeliver(m13, 1L, m13Farmer, m13Farm);

        Assert.Equal((120, 108, 12), daughterTransfer);
        Assert.Equal((40, 36, 4), originTransfer);
        Assert.Equal(originTransfer, m13Transfer);
    }

    [Fact]
    public void M14SnapshotRejectsMissingResidenceAndOverCapacityDaughterStock()
    {
        var snapshot = new SimulationEngine(new WorldSeed(71),
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion).CreatePersistenceSnapshot();
        var state = snapshot.MigrationState!;

        var missingCitizen = new MigrationWorldState(state.Version, state.CitizenResidences.Skip(1).ToArray(),
            state.HouseholdResidences, state.StructureOwners, state.FacilityOwners, state.WorkOrderOwners,
            state.DaughterSettlement, state.InTransitParties);
        Assert.Throws<ArgumentException>(() => CopySnapshot(snapshot, missingCitizen.ToCanonicalJson()));

        var daughterSite = FindOtherWalkableTile(snapshot.World!);
        var allZeroLivingStock = Enum.GetValues<LivingGood>().Select(good => new LivingStock(good, 0)).ToArray();
        var daughterWithExcessStock = new MigrationWorldState(state.Version, state.CitizenResidences,
            state.HouseholdResidences, state.StructureOwners, state.FacilityOwners, state.WorkOrderOwners,
            new MigrationDaughterSettlementState(daughterSite,
                new MigrationSettlementStockState(1, 0, 0, 0, snapshot.WorldMinute.Value, snapshot.WorldMinute.Value),
                allZeroLivingStock), state.InTransitParties);
        Assert.Throws<ArgumentException>(() => CopySnapshot(snapshot, daughterWithExcessStock.ToCanonicalJson()));
    }

    [Fact]
    public void M14AllowsOneConstructionProjectPerSiteButKeepsM13GlobalLimit()
    {
        var fixture = CreateDaughterFixture(new WorldSeed(914));
        var snapshot = fixture.Snapshot;
        Assert.DoesNotContain(snapshot.Structures, x => x.Status == StructureStatus.UnderConstruction);
        var engine = SimulationEngine.FromPersistenceSnapshot(snapshot);
        var firstId = snapshot.Counters.NextEntityId;
        var originLocation = FindFreeBuildableTile(engine, 1, engine.World.StartingSite, snapshot.Structures, []);
        var daughterLocation = FindFreeBuildableTile(engine, 2, fixture.DaughterSite, snapshot.Structures, [originLocation]);
        var projects = new[]
        {
            CreateActiveShelter(new StructureId(firstId), originLocation),
            CreateActiveShelter(new StructureId(firstId + 1), daughterLocation)
        };
        var twoSiteStructures = snapshot.Structures.Concat(projects).ToArray();
        var twoSiteState = WithAdditionalStructureOwners(snapshot.MigrationState!, projects, [1, 2]);
        var accepted = CopySnapshot(snapshot, twoSiteState.ToCanonicalJson(), structures: twoSiteStructures,
            counters: WithNextEntityId(snapshot.Counters, firstId + 2));
        Assert.Equal(2, accepted.Structures.Count(x => x.Status == StructureStatus.UnderConstruction));
        Assert.Equal(firstId, SimulationEngine.FromPersistenceSnapshot(accepted).ActiveConstructionProject?.Id.Value);

        var sameSiteLocation = FindFreeBuildableTile(engine, 2, fixture.DaughterSite, twoSiteStructures,
            [originLocation, daughterLocation]);
        var sameSiteProject = CreateActiveShelter(new StructureId(firstId + 2), sameSiteLocation);
        var threeProjects = twoSiteStructures.Append(sameSiteProject).ToArray();
        var threeProjectState = WithAdditionalStructureOwners(snapshot.MigrationState!, projects.Append(sameSiteProject).ToArray(), [1, 2, 2]);
        Assert.Throws<ArgumentException>(() => CopySnapshot(snapshot, threeProjectState.ToCanonicalJson(),
            structures: threeProjects, counters: WithNextEntityId(snapshot.Counters, firstId + 3)));

        var legacy = new SimulationEngine(snapshot.Seed,
            simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion).CreatePersistenceSnapshot();
        var legacyFirstId = legacy.Counters.NextEntityId;
        var legacyFirstLocation = FindFreeBuildableTile(SimulationEngine.FromPersistenceSnapshot(legacy), 1,
            legacy.World!.StartingSite, legacy.Structures, []);
        var legacySecondLocation = FindFreeBuildableTile(SimulationEngine.FromPersistenceSnapshot(legacy), 1,
            legacy.World.StartingSite, legacy.Structures, [legacyFirstLocation]);
        var legacyProjects = new[]
        {
            CreateActiveShelter(new StructureId(legacyFirstId), legacyFirstLocation),
            CreateActiveShelter(new StructureId(legacyFirstId + 1), legacySecondLocation)
        };
        Assert.Throws<ArgumentException>(() => CopySnapshot(legacy,
            structures: legacy.Structures.Concat(legacyProjects).ToArray(),
            counters: WithNextEntityId(legacy.Counters, legacyFirstId + 2)));
    }

    [Theory]
    [InlineData("SelectConstructionSite")]
    [InlineData("SelectFarmSite")]
    public void M14SiteSelectorsAvoidOriginOwnedStructuresInsideDaughterPartition(string selector)
    {
        var fixture = CreateDaughterFixture(new WorldSeed(915));
        var engine = SimulationEngine.FromPersistenceSnapshot(fixture.Snapshot);
        var occupied = (TileCoordinate)InvokePrivate(engine, selector, 2L)!;
        Assert.Equal(2, SiteIdForLocation(engine, occupied));

        var counters = GetPrivateField<DeterministicCounters>(engine, "_counters");
        var blocker = CreateActiveShelter(counters.AllocateStructureId(), occupied);
        var structures = GetPrivateField<Dictionary<long, Structure>>(engine, "_structures");
        structures.Add(blocker.Id.Value, blocker);
        var migration = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        SetPrivateField(engine, "_migrationState", WithAdditionalStructureOwners(migration, [blocker], [1]));

        var selected = (TileCoordinate)InvokePrivate(engine, selector, 2L)!;

        Assert.NotEqual(occupied, selected);
        Assert.Equal(occupied, blocker.Location);
        Assert.Equal(1, (long)InvokePrivate(engine, "SiteIdForStructure", blocker.Id.Value)!);
        Assert.Equal(2, SiteIdForLocation(engine, blocker.Location));
    }

    [Fact]
    public void M14DaughterSiteUsesOnlyItsFoodWorkFarmsAndBarterCounterparties()
    {
        var fixture = CreateDaughterFixture(new WorldSeed(913));
        MigrationValidation.Validate(fixture.Snapshot);
        var engine = SimulationEngine.FromPersistenceSnapshot(fixture.Snapshot);
        var citizens = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens");
        var siteOneCitizen = citizens.Values.First(x => SiteIdForCitizen(engine, x) == 1);
        var siteTwoCitizen = citizens.Values.First(x => SiteIdForCitizen(engine, x) == 2);
        siteOneCitizen.Needs = new CitizenNeeds(6000, 0, 0, 0);
        siteTwoCitizen.Needs = new CitizenNeeds(6000, 0, 0, 0);

        InvokePrivate(engine, "Eat", siteOneCitizen);
        var afterSiteOneMeal = engine.CreateMigrationReadSnapshot();
        Assert.Equal(290, afterSiteOneMeal.Settlements.Single(x => x.Id == 1).CommunalStock.FoodStored);
        Assert.Equal(100, afterSiteOneMeal.Settlements.Single(x => x.Id == 2).CommunalStock.FoodStored);
        InvokePrivate(engine, "Eat", siteTwoCitizen);
        var afterSiteTwoMeal = engine.CreateMigrationReadSnapshot();
        Assert.Equal(290, afterSiteTwoMeal.Settlements.Single(x => x.Id == 1).CommunalStock.FoodStored);
        Assert.Equal(90, afterSiteTwoMeal.Settlements.Single(x => x.Id == 2).CommunalStock.FoodStored);

        var daughterStock = Assert.IsType<SettlementState>(InvokePrivate(engine, "SettlementFor", 2L));
        InvokePrivate(engine, "ReservePublicWork", siteTwoCitizen);
        Assert.Equal(86, daughterStock.FoodStored);
        InvokePrivate(engine, "ReleasePublicWork", siteTwoCitizen);
        Assert.Equal(90, daughterStock.FoodStored);

        var resource = Assert.IsType<ResourceNode>(InvokePrivate(engine, "SelectResourceTarget", siteTwoCitizen, ResourceType.Food));
        Assert.Equal(2, SiteIdForResource(engine, resource));
        var farmSite = Assert.IsType<TileCoordinate>(InvokePrivate(engine, "SelectFarmSite", 2L));
        Assert.Equal(2, SiteIdForLocation(engine, farmSite));

        InvokePrivate(engine, "PlanLivingEconomy");
        var planned = engine.CreatePersistenceSnapshot();
        var living = LivingWorldCodec.Deserialize(planned.LivingStateJson!);
        var orderOwners = planned.MigrationState!.WorkOrderOwners.ToDictionary(x => x.EntityId, x => x.SettlementId);
        var siteOneOrders = living.Orders.Where(x => orderOwners[x.Id] == 1).ToArray();
        var siteTwoOrders = living.Orders.Where(x => orderOwners[x.Id] == 2).ToArray();
        Assert.NotEmpty(siteOneOrders);
        Assert.NotEmpty(siteTwoOrders);
        Assert.All(siteOneOrders, x => Assert.Equal(fixture.Snapshot.World!.StartingSite, x.SupplyLocation));
        Assert.All(siteTwoOrders, x => Assert.Equal(fixture.DaughterSite, x.SupplyLocation));

        InvokePrivate(engine, "UpdateEconomy");
        var economy = engine.CreatePersistenceSnapshot().Economy!;
        var householdSites = fixture.Snapshot.MigrationState!.HouseholdResidences.ToDictionary(x => x.EntityId, x => x.SettlementId);
        var reservedTrades = economy.Trades.Where(x => x.Status == BarterStatus.Reserved).ToArray();
        Assert.Contains(reservedTrades, x => householdSites[x.HouseholdA] == 1 && householdSites[x.HouseholdB] == 1);
        Assert.Contains(reservedTrades, x => householdSites[x.HouseholdA] == 2 && householdSites[x.HouseholdB] == 2);
        Assert.All(reservedTrades, x => Assert.Equal(householdSites[x.HouseholdA], householdSites[x.HouseholdB]));

        var final = engine.CreatePersistenceSnapshot();
        MigrationValidation.Validate(final);
        Assert.Equal(2, engine.CreateMigrationReadSnapshot().Settlements.Count);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(4)]
    public void M14DaughterDecisionAdmitsFoodGatherWithPartialStorage(int freeStorage)
    {
        var fixture = CreateDaughterFixture(new WorldSeed(9143));
        var source = fixture.Snapshot;
        var migration = source.MigrationState!;
        var householdSites = migration.HouseholdResidences.ToDictionary(x => x.EntityId, x => x.SettlementId);
        var adult = source.Citizens.Where(citizen => citizen.IsAlive && citizen.HouseholdId is { } household &&
                householdSites[household.Value] == 2 && citizen.AgeYears(source.WorldMinute) >= 13)
            .OrderBy(citizen => citizen.Id.Value).First();
        var sourceEconomy = source.Economy!;
        var privateDaughterGoods = checked((int)sourceEconomy.Households
            .Where(stock => householdSites[stock.HouseholdId] == 2)
            .Sum(stock => (long)stock.Holdings.Total));
        var privateOriginGoods = sourceEconomy.Households.Where(stock => householdSites[stock.HouseholdId] == 1)
            .Aggregate(new Goods(), (sum, stock) => sum.Plus(stock.Holdings));
        var originStock = source.Settlement!;
        var originalDaughterStock = migration.DaughterSettlement!.CommunalStock;
        var daughterWood = checked(4_000 - privateDaughterGoods - freeStorage - 40);
        var daughterStock = new MigrationSettlementStockState(0, daughterWood, 40, 4_000,
            source.WorldMinute.Value, source.WorldMinute.Value);
        var projectId = new StructureId(source.Counters.NextEntityId);
        var project = new Structure(projectId, StructureType.Granary, fixture.DaughterSite, source.WorldMinute.Value,
            AgricultureRules.GranaryWood, AgricultureRules.GranaryStone, AgricultureRules.GranaryWork)
        {
            DeliveredWood = AgricultureRules.GranaryWood,
            DeliveredStone = AgricultureRules.GranaryStone
        };
        var projectOwners = migration.StructureOwners.Append(new MigrationEntityResidence(projectId.Value, 2)).ToArray();
        var daughterScenarioState = WithDaughterStock(migration, daughterStock, projectOwners);
        var adjustedHouseholds = sourceEconomy.Households.Select(stock => householdSites[stock.HouseholdId] == 1
            ? stock with { Holdings = new Goods() }
            : stock).ToArray();
        var productionDelta = new Goods(
            Food: checked(4_000 - originStock.FoodStored - originalDaughterStock.FoodStored - privateOriginGoods.Food),
            Wood: checked(daughterWood + AgricultureRules.GranaryWood - originStock.WoodStored - originalDaughterStock.WoodStored - privateOriginGoods.Wood),
            Stone: checked(40 + AgricultureRules.GranaryStone - originStock.StoneStored - originalDaughterStock.StoneStored - privateOriginGoods.Stone));
        var adjustedEconomy = sourceEconomy with
        {
            Households = Array.AsReadOnly(adjustedHouseholds),
            Produced = sourceEconomy.Produced.Plus(productionDelta)
        };
        var daughterScenario = CopySnapshot(source, daughterScenarioState.ToCanonicalJson(),
            structures: source.Structures.Append(project).ToArray(),
            settlement: new SettlementState(4_000, 0, 0, originStock.BaseStorageCapacity,
                originStock.DemandUpdatedMinute, originStock.ExposureConsequencesStartMinute),
            economy: adjustedEconomy,
            counters: source.Counters with { NextEntityId = checked(projectId.Value + 1) },
            contributions: source.StructureContributions.Append(new StructureContribution(project.Id, adult.Id,
                woodDelivered: AgricultureRules.GranaryWood, stoneDelivered: AgricultureRules.GranaryStone)).ToArray());
        MigrationValidation.Validate(daughterScenario);

        var engine = SimulationEngine.FromPersistenceSnapshot(daughterScenario);
        var siteTwoAdult = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens")[adult.Id.Value];
        siteTwoAdult.Skills.Foraging = 13_000;
        siteTwoAdult.Needs = new CitizenNeeds(9_000, 0, 0, 0);
        siteTwoAdult.NeedsUpdatedMinute = engine.CurrentMinute.Value;

        var decisions = engine.EvaluateDecision(siteTwoAdult.Id);
        Assert.Equal(freeStorage, (int)InvokePrivate(engine, "AvailableStorage", ResourceType.Food, 2L)!);
        Assert.Equal(25, (int)InvokePrivate(engine, "GrowthGatherYield", siteTwoAdult, CitizenAction.GatherFood)!);
        Assert.True(freeStorage < (int)InvokePrivate(engine, "GrowthGatherYield", siteTwoAdult, CitizenAction.GatherFood)!);
        Assert.Contains(decisions, decision => decision.Action == CitizenAction.GatherFood);
        Assert.DoesNotContain(decisions, decision => decision.Action == CitizenAction.Eat);
        Assert.Contains(decisions, decision => decision.Action == CitizenAction.Build);
        Assert.Equal(CitizenAction.GatherFood, SimulationEngine.SelectDecision(decisions));

        var target = Assert.IsType<ResourceNode>(InvokePrivate(engine, "SelectResourceTarget", siteTwoAdult, ResourceType.Food));
        Assert.Equal(2, SiteIdForResource(engine, target));
        var daughterRuntime = Assert.IsType<SettlementState>(InvokePrivate(engine, "SettlementFor", 2L));
        var savedWood = daughterRuntime.WoodStored;
        var savedStone = daughterRuntime.StoneStored;
        engine.Settlement.FoodStored = 0;
        daughterRuntime.FoodStored = 400;
        daughterRuntime.WoodStored = 0;
        daughterRuntime.StoneStored = 0;
        var mealDecisions = engine.EvaluateDecision(siteTwoAdult.Id);
        Assert.Contains(mealDecisions, decision => decision.Action == CitizenAction.Eat);
        Assert.Equal(CitizenAction.Eat, SimulationEngine.SelectDecision(mealDecisions));

        engine.Settlement.FoodStored = 4_000;
        daughterRuntime.FoodStored = 0;
        daughterRuntime.WoodStored = savedWood;
        daughterRuntime.StoneStored = savedStone;
        InvokePrivate(engine, "Decide", siteTwoAdult);
        Assert.Equal(CitizenAction.GatherFood, siteTwoAdult.CurrentAction);
        Assert.Equal(target.Id, siteTwoAdult.TargetResourceNodeId);
    }

    [Fact]
    public void M13GrowthGatherStillAdmitsAnExactYieldStorageFit()
    {
        var engine = new SimulationEngine(new WorldSeed(9143),
            simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
        var citizens = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens");
        foreach (var citizen in citizens.Values.Where(x => x.IsAlive))
        {
            citizen.CurrentAction = CitizenAction.Idle;
            citizen.CarriedResourceType = null;
            citizen.CarriedResourceQuantity = 0;
        }

        var adult = citizens.Values.Where(x => x.IsAlive && x.AgeYears(engine.CurrentMinute) >= 18)
            .OrderBy(x => x.Id.Value).First();
        adult.Needs = new CitizenNeeds(9_000, 0, 0, 0);
        adult.NeedsUpdatedMinute = engine.CurrentMinute.Value;
        engine.Settlement.FoodStored = 0;
        var available = (int)InvokePrivate(engine, "AvailableStorage", ResourceType.Food, 1L)!;
        var expectedYield = (int)InvokePrivate(engine, "GrowthGatherYield", adult, CitizenAction.GatherFood)!;
        Assert.True(available > expectedYield);
        engine.Settlement.WoodStored = checked(engine.Settlement.WoodStored + available - expectedYield);
        Assert.Equal(expectedYield, (int)InvokePrivate(engine, "AvailableStorage", ResourceType.Food, 1L)!);

        var decisions = engine.EvaluateDecision(adult.Id);
        Assert.Contains(decisions, decision => decision.Action == CitizenAction.GatherFood);
    }

    [Fact]
    public void M14DaughterFoodReserveDoesNotDoubleCountStoredFoodOrShrinkAfterEating()
    {
        var fixture = CreateDaughterFixture(new WorldSeed(9144));
        var engine = SimulationEngine.FromPersistenceSnapshot(fixture.Snapshot);
        var siteTwoCitizens = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens").Values
            .Where(citizen => citizen.IsAlive && SiteIdForCitizen(engine, citizen) == 2)
            .OrderBy(citizen => citizen.Id.Value).ToArray();
        var residents = siteTwoCitizens;
        var population = (int)InvokePrivate(engine, "PopulationAt", 2L)!;
        Assert.Equal(residents.Length, population);
        Assert.True(population > 0);

        var migration = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        var householdSites = migration.HouseholdResidences.ToDictionary(x => x.EntityId, x => x.SettlementId);
        var householdStocks = GetPrivateField<SortedDictionary<long, HouseholdStock>>(engine, "_householdStocks");
        foreach (var householdId in householdStocks.Keys.ToArray())
            if (householdSites[householdId] == 2)
                householdStocks[householdId] = householdStocks[householdId] with { Holdings = new Goods() };

        var daughter = Assert.IsType<SettlementState>(InvokePrivate(engine, "SettlementFor", 2L));
        daughter.BaseStorageCapacity = 4_000;
        daughter.FoodStored = 200;
        var foodReserve = checked(population * 120);
        daughter.WoodStored = checked(4_000 - foodReserve - 640);
        daughter.StoneStored = 0;
        var originFoodBefore = engine.Settlement.FoodStored;
        var originWoodAvailable = (int)InvokePrivate(engine, "AvailableStorage", ResourceType.Wood, 1L)!;
        var goodsBefore = (long)daughter.StorageUsed;
        var foodConsumedBefore = GetPrivateField<long>(engine, "_foodConsumed");

        var expectedFoodRoom = checked(4_000 - daughter.FoodStored - daughter.WoodStored);
        Assert.Equal(expectedFoodRoom, (int)InvokePrivate(engine, "AvailableStorage", ResourceType.Food, 2L)!);
        Assert.Equal(640, (int)InvokePrivate(engine, "AvailableStorage", ResourceType.Wood, 2L)!);
        Assert.True(originWoodAvailable > 640);

        var resident = residents[0];
        resident.Needs = new CitizenNeeds(5_000, 0, 0, 0);
        resident.NeedsUpdatedMinute = engine.CurrentMinute.Value;
        InvokePrivate(engine, "Eat", resident);

        var consumed = GetPrivateField<long>(engine, "_foodConsumed") - foodConsumedBefore;
        Assert.True(consumed > 0);
        Assert.Equal(goodsBefore, checked((long)daughter.StorageUsed + consumed));
        Assert.Equal(640, (int)InvokePrivate(engine, "AvailableStorage", ResourceType.Wood, 2L)!);
        Assert.Equal(checked(expectedFoodRoom + (int)consumed), (int)InvokePrivate(engine, "AvailableStorage", ResourceType.Food, 2L)!);
        Assert.Equal(originFoodBefore, engine.Settlement.FoodStored);
        Assert.Equal(originWoodAvailable, (int)InvokePrivate(engine, "AvailableStorage", ResourceType.Wood, 1L)!);
    }

    [Fact]
    public void FoundingPressureUsesNearOriginLandRatherThanDistantFoundingSites()
    {
        var engine = new SimulationEngine(new WorldSeed(913),
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        var households = GetPrivateField<Dictionary<long, Household>>(engine, "_households");
        var citizens = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens");
        var structures = GetPrivateField<Dictionary<long, Structure>>(engine, "_structures");
        var household = households.Values.Where(x => x.DissolvedMinute is null).OrderBy(x => x.Id.Value)
            .First(x => citizens.Values.Count(c => c.IsAlive && c.HouseholdId == x.Id) is > 0 and <= CitizenSimulationRules.ShelterCapacityPerBuilding);
        var members = citizens.Values.Where(x => x.IsAlive && x.HouseholdId == household.Id).OrderBy(x => x.Id.Value).ToArray();
        var occupied = structures.Values.Select(x => x.Location).ToHashSet();
        var resources = engine.World.Resources.Select(x => x.Coordinate).ToHashSet();
        var costs = LivingTravelCosts.Compute(engine.World, engine.World.StartingSite);
        var localTiles = engine.World.Tiles.Where(x => x.Walkable && x.Buildable && x.Terrain != TerrainType.Freshwater &&
            x.Coordinate != engine.World.StartingSite && !occupied.Contains(x.Coordinate) && !resources.Contains(x.Coordinate) &&
            costs.TryGetValue(x.Coordinate, out var cost) && cost is > 0 and < 32).Select(x => x.Coordinate).ToArray();
        Assert.NotEmpty(localTiles);
        Assert.True((bool)InvokePrivate(engine, "HasLocalUsableLand")!);

        var shelterId = structures.Keys.DefaultIfEmpty(0).Max() + 10_000;
        var shelter = new Structure(new StructureId(shelterId), StructureType.Shelter, engine.World.StartingSite,
            0, StructureDefinitions.ShelterRequiredWood, StructureDefinitions.ShelterRequiredStone,
            StructureDefinitions.ShelterRequiredWork)
        {
            Status = StructureStatus.Complete,
            CompletedMinute = 0,
            DeliveredWood = StructureDefinitions.ShelterRequiredWood,
            DeliveredStone = StructureDefinitions.ShelterRequiredStone,
            CompletedWork = StructureDefinitions.ShelterRequiredWork
        };
        structures.Add(shelterId, shelter);
        household.DwellingStructureId = shelter.Id;
        foreach (var member in members) member.HomeStructureId = shelter.Id;

        var fakeId = shelterId + 1;
        foreach (var tile in localTiles)
        {
            var blocker = new Structure(new StructureId(fakeId++), StructureType.Shelter, tile, 0,
                StructureDefinitions.ShelterRequiredWood, StructureDefinitions.ShelterRequiredStone,
                StructureDefinitions.ShelterRequiredWork);
            structures.Add(blocker.Id.Value, blocker);
        }

        Assert.False((bool)InvokePrivate(engine, "HasLocalUsableLand")!);
        Assert.NotNull(InvokePrivate(engine, "FindFoundingSite", (object)Array.Empty<Citizen>()));
        InvokePrivate(engine, "ObserveMigrationFoundingPressure");

        var migration = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        Assert.Contains(migration.FoundingPressure!, x => x.HouseholdId == household.Id.Value);
    }

    [Fact]
    public void FoundingDepartureBeginsAtYearFiveAndTransfersExactlyOneHouseholdProvisionStack()
    {
        var engine = new SimulationEngine(new WorldSeed(42),
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        var (expectedHousehold, expectedMembers) = EnsureTwoMemberHousehold(engine);
        var minuteYearFour = new WorldMinute(4L * WorldCalendar.MinutesPerYear);
        var minuteYearFive = new WorldMinute(5L * WorldCalendar.MinutesPerYear);
        SetCurrentMinute(engine, minuteYearFour);
        engine.Settlement.FoodStored = 100_000;
        var initialState = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        var seasonMinutes = (long)WorldCalendar.DaysPerSeason * WorldCalendar.MinutesPerDay;
        var pressure = engine.Households.Where(x => x.DissolvedMinute is null)
            .Select(x => new MigrationFoundingPressureState(x.Id.Value, 0L)).ToArray();
        SetPrivateField(engine, "_migrationState", new MigrationWorldState(initialState.Version,
            initialState.CitizenResidences, initialState.HouseholdResidences, initialState.StructureOwners,
            initialState.FacilityOwners, initialState.WorkOrderOwners, initialState.DaughterSettlement,
            initialState.InTransitParties, pressure));

        InvokePrivate(engine, "EvaluateMigrationFounding");
        Assert.Empty(GetPrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        SetCurrentMinute(engine, minuteYearFive);
        var migration = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        SetPrivateField(engine, "_migrationState", new MigrationWorldState(migration.Version,
            migration.CitizenResidences, migration.HouseholdResidences, migration.StructureOwners,
            migration.FacilityOwners, migration.WorkOrderOwners, migration.DaughterSettlement,
            migration.InTransitParties, pressure.Select(x => x with { SinceMinute = minuteYearFive.Value - seasonMinutes }).ToArray()));
        var expectedAdults = expectedMembers.Count(x => x.IsAlive && x.AgeYears(minuteYearFive) >= 18);
        Assert.True(expectedAdults >= 2);
        var expectedSite = (TileCoordinate)InvokePrivate(engine, "FindFoundingSite", (object)expectedMembers)!;
        var rate = NeedsProjection.HungerRatePerMinute;
        var hungerPoints = checked((long)rate * 14 * WorldCalendar.MinutesPerDay);
        var foodPerTraveler = checked((hungerPoints * CitizenSimulationRules.MealFoodUnits +
            CitizenSimulationRules.FullHungerReduction - 1) / CitizenSimulationRules.FullHungerReduction);
        var expectedProvisions = checked(foodPerTraveler * expectedMembers.Length);
        Assert.True(engine.Settlement.FoodStored >= expectedProvisions);
        var originFoodBefore = engine.Settlement.FoodStored;

        InvokePrivate(engine, "EvaluateMigrationFounding");

        var departedState = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        var party = Assert.Single(departedState.InTransitParties);
        Assert.Null(departedState.DaughterSettlement);
        Assert.Equal(expectedHousehold.Id.Value, party.HouseholdId);
        Assert.Equal(expectedSite, party.DestinationSite);
        Assert.Equal(expectedMembers.Select(x => x.Id.Value).Order(), party.CitizenIds);
        Assert.Equal(expectedProvisions, Assert.Single(party.Cargo).Quantity);
        Assert.Equal(originFoodBefore - expectedProvisions, engine.Settlement.FoodStored);
        Assert.All(expectedMembers, member =>
        {
            Assert.Equal(CitizenAction.Explore, member.CurrentAction);
            Assert.Equal(CitizenActionPhase.TravelToTarget, member.ActionPhase);
            Assert.Equal(expectedSite, member.ActionTarget);
        });
    }

    [Fact]
    public void StaggeredFoundingArrivalWaitsForHouseholdBeforeCreatingDaughter()
    {
        var engine = new SimulationEngine(new WorldSeed(42),
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        var (household, members) = EnsureTwoMemberHousehold(engine);
        SetCurrentMinute(engine, new WorldMinute(18L * WorldCalendar.MinutesPerYear));
        foreach (var member in members) Assert.True(member.AgeYears(engine.CurrentMinute) >= 18);
        var destination = (TileCoordinate)InvokePrivate(engine, "FindFoundingSite", (object)members)!;
        var partyId = 9_000_001L;
        var cargo = new[] { new MigrationCargoStackState(partyId + 1, MigrationCargoGood.Food, 15, MigrationCargoPurpose.Provisions) };
        members[0].Location = destination;
        members[0].CurrentAction = CitizenAction.Explore;
        members[0].ActionTarget = destination;
        members[1].CurrentAction = CitizenAction.Explore;
        members[1].ActionTarget = destination;
        var party = new MigrationTransitPartyState(partyId, household.Id.Value, 1, null, destination, destination,
            members.Select(x => x.Id.Value).ToArray(), cargo, 10, engine.CurrentMinute.Value);
        InvokePrivate(engine, "SetMigrationParty", party);
        ClearScheduledEvents(engine);

        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", members[0])!);
        var waitingState = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        Assert.Null(waitingState.DaughterSettlement);
        Assert.Single(waitingState.InTransitParties);
        var wakeup = Assert.Single(((IEnumerable)GetPrivateField<object>(engine, "_scheduledEvents")).Cast<object>());
        var wakeupName = (string)wakeup.GetType().GetProperty("Name")!.GetValue(wakeup)!;
        var order = (ScheduledEventOrder)wakeup.GetType().GetProperty("Order")!.GetValue(wakeup)!;
        Assert.Equal(CitizenEventNames.ActionComplete, wakeupName);
        Assert.Equal(engine.CurrentMinute.Add(CitizenSimulationRules.SurvivalCheckIntervalMinutes), order.DueWorldMinute);

        members[1].Location = destination;
        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", members[1])!);
        var founded = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        Assert.NotNull(founded.DaughterSettlement);
        Assert.Empty(founded.InTransitParties);
        Assert.All(founded.CitizenResidences.Where(x => members.Any(member => member.Id.Value == x.EntityId)),
            x => Assert.Equal(MigrationDaughterSettlementState.SettlementId, x.SettlementId));
    }

    [Fact]
    public void FoundingCapacityIncludesFounderPrivateStockAndDestinationCargo()
    {
        var engine = new SimulationEngine(new WorldSeed(42),
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        var (household, members) = EnsureTwoMemberHousehold(engine);
        InvokePrivate(engine, "ReconcileEconomicHouseholds");
        var householdStocks = GetPrivateField<SortedDictionary<long, HouseholdStock>>(engine, "_householdStocks");
        var priorHoldings = householdStocks[household.Id.Value].Holdings;
        var founderHoldings = new Goods(Wood: EconomyRules.FoundingStorageCapacity + 1000L, Stone: 2000);
        householdStocks[household.Id.Value] = householdStocks[household.Id.Value] with { Holdings = founderHoldings };
        var produced = GetPrivateField<Goods>(engine, "_producedGoods");
        SetPrivateField(engine, "_producedGoods", produced with
        {
            Food = checked(produced.Food + founderHoldings.Food - priorHoldings.Food),
            Wood = checked(produced.Wood + founderHoldings.Wood - priorHoldings.Wood),
            Stone = checked(produced.Stone + founderHoldings.Stone - priorHoldings.Stone)
        });
        engine.Settlement.BaseStorageCapacity = 100_000;
        var before = engine.CreatePersistenceSnapshot();
        var beforeStored = before.Economy!.StoredGoods(before.Settlement!).Total;

        var provisions = checked(RelocationProvisions(engine.CurrentMinute.Value) * members.Length);
        Assert.True((bool)InvokePrivate(engine, "TryWithdrawFoundingFood", household.Id.Value, 1L, provisions)!);
        var destination = (TileCoordinate)InvokePrivate(engine, "FindFoundingSite", (object)members)!;
        foreach (var member in members) member.Location = destination;
        var counters = GetPrivateField<DeterministicCounters>(engine, "_counters");
        var party = new MigrationTransitPartyState(counters.AllocateMigrationPartyId(), household.Id.Value, 1, null,
            destination, destination, members.Select(x => x.Id.Value).ToArray(),
            [new MigrationCargoStackState(counters.AllocateMigrationCargoStackId(), MigrationCargoGood.Food,
                provisions, MigrationCargoPurpose.Provisions)], 0, engine.CurrentMinute.Value,
            foundingAdultArrived: true, journeyKind: MigrationJourneyKind.Founding);
        InvokePrivate(engine, "SetMigrationParty", party);

        InvokePrivate(engine, "CompleteFoundingSettlement", party, members);

        var founded = engine.CreatePersistenceSnapshot();
        MigrationValidation.Validate(founded);
        var daughter = founded.MigrationState!.DaughterSettlement!;
        var movedPrivateStock = founded.Economy!.Households.Single(x => x.HouseholdId == household.Id.Value).Holdings;
        var daughterStored = checked(movedPrivateStock.Total + daughter.CommunalStock.FoodStored +
            daughter.CommunalStock.WoodStored + daughter.CommunalStock.StoneStored);
        var daughterFoodReserve = checked(members.Length * 120);
        Assert.Equal(beforeStored, checked(founded.Economy.StoredGoods(founded.Settlement!).Total +
            daughter.CommunalStock.FoodStored + daughter.CommunalStock.WoodStored + daughter.CommunalStock.StoneStored));
        Assert.True(daughterStored <= daughter.CommunalStock.BaseStorageCapacity);
        Assert.Equal(checked(daughterStored + daughterFoodReserve), daughter.CommunalStock.BaseStorageCapacity);
        Assert.True(movedPrivateStock.Wood + movedPrivateStock.Stone + daughter.CommunalStock.WoodStored +
            daughter.CommunalStock.StoneStored <= daughter.CommunalStock.BaseStorageCapacity);
        Assert.True(daughter.CommunalStock.BaseStorageCapacity > EconomyRules.FoundingStorageCapacity);

        var foodAvailable = (int)InvokePrivate(engine, "AvailableStorage", ResourceType.Food, 2L)!;
        var nonFoodAvailable = (int)InvokePrivate(engine, "AvailableStorage", ResourceType.Wood, 2L)!;
        var originWoodAvailable = (int)InvokePrivate(engine, "AvailableStorage", ResourceType.Wood, 1L)!;
        Assert.Equal(daughterFoodReserve, foodAvailable);
        Assert.Equal(movedPrivateStock.Food + daughter.CommunalStock.FoodStored, nonFoodAvailable);
        Assert.True(originWoodAvailable > nonFoodAvailable);

        var originFoodBefore = engine.Settlement.FoodStored;
        var daughterFoodBefore = movedPrivateStock.Food + daughter.CommunalStock.FoodStored;
        var founder = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens")[members[0].Id.Value];
        founder.Needs = new CitizenNeeds(5_000, 0, 0, 0);
        founder.NeedsUpdatedMinute = engine.CurrentMinute.Value;
        InvokePrivate(engine, "Eat", founder);
        var daughterRuntime = Assert.IsType<SettlementState>(InvokePrivate(engine, "SettlementFor", 2L));
        var founderStock = GetPrivateField<SortedDictionary<long, HouseholdStock>>(engine, "_householdStocks")[household.Id.Value];
        var daughterFoodAfter = checked(founderStock.Holdings.Food + daughterRuntime.FoodStored);
        var eaten = checked((int)(daughterFoodBefore - daughterFoodAfter));
        Assert.True(eaten > 0);
        Assert.Equal(originFoodBefore, engine.Settlement.FoodStored);
        Assert.Equal(nonFoodAvailable, (int)InvokePrivate(engine, "AvailableStorage", ResourceType.Wood, 2L)!);

        founder.Location = destination;
        founder.CarriedResourceType = ResourceType.Wood;
        founder.CarriedResourceQuantity = nonFoodAvailable;
        founder.CurrentAction = CitizenAction.GatherWood;
        var production = GetPrivateField<Goods>(engine, "_producedGoods");
        SetPrivateField(engine, "_producedGoods", production.Add(ResourceType.Wood, nonFoodAvailable));
        var producedBeforeDeposit = founded.Economy!.Produced.Total;
        InvokePrivate(engine, "Deposit", founder);
        Assert.Equal(0, founder.CarriedResourceQuantity);
        Assert.Equal(checked(daughterFoodReserve + eaten - nonFoodAvailable),
            (int)InvokePrivate(engine, "AvailableStorage", ResourceType.Food, 2L)!);
        Assert.Equal(0, (int)InvokePrivate(engine, "AvailableStorage", ResourceType.Wood, 2L)!);
        InvokePrivate(engine, "FinishAction", founder);

        var afterDeposit = engine.CreatePersistenceSnapshot();
        MigrationValidation.Validate(afterDeposit);
        var afterMigration = afterDeposit.MigrationState!;
        var daughterAfter = afterMigration.DaughterSettlement!;
        var originStoredAfter = afterDeposit.Economy!.StoredGoodsAt(afterDeposit.Settlement!, afterMigration, 1).Total;
        var daughterStoredAfter = afterDeposit.Economy.StoredGoodsAt(daughterAfter.CommunalStock.ToSettlementState(),
            afterMigration, MigrationDaughterSettlementState.SettlementId).Total;
        Assert.Equal(checked(beforeStored - eaten + nonFoodAvailable), checked(originStoredAfter + daughterStoredAfter));
        Assert.Equal(checked(producedBeforeDeposit + nonFoodAvailable), afterDeposit.Economy.Produced.Total);
    }

    [Fact]
    public void LastTravelerDeathCachesCargoAndClearsPartyForCheckpointReplay()
    {
        var engine = new SimulationEngine(new WorldSeed(42),
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        var (household, members) = EnsureTwoMemberHousehold(engine);
        Assert.True((bool)InvokePrivate(engine, "TryWithdrawFoundingFood", household.Id.Value, 1L)!);
        var location = members[0].Location;
        var party = new MigrationTransitPartyState(9_100_001, household.Id.Value, 1, null, location,
            (TileCoordinate)InvokePrivate(engine, "FindFoundingSite", (object)members)! , members.Select(x => x.Id.Value).ToArray(),
            [new MigrationCargoStackState(9_100_002, MigrationCargoGood.Food, 1, MigrationCargoPurpose.Provisions)],
            32, engine.CurrentMinute.Value);
        InvokePrivate(engine, "SetMigrationParty", party);

        InvokePrivate(engine, "KillNatural", members[0]);
        InvokePrivate(engine, "SynchronizeLivingPeople");
        var recoveryLocation = Assert.Single(GetPrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties).Location;
        foreach (var member in members.Skip(1))
        {
            InvokePrivate(engine, "KillNatural", member);
            InvokePrivate(engine, "SynchronizeLivingPeople");
        }

        var resolved = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        Assert.Empty(resolved.InTransitParties);
        Assert.Null(resolved.DaughterSettlement);
        var checkpoint = engine.CreatePersistenceSnapshot();
        Assert.Contains(checkpoint.Economy!.Recoverable, x =>
            x.Location == recoveryLocation && x.Resource == ResourceType.Food && x.Quantity == 1);
        var reopened = SimulationEngine.FromPersistenceSnapshot(checkpoint).CreatePersistenceSnapshot();
        Assert.Equal(checkpoint.MigrationStateJson, reopened.MigrationStateJson);
        Assert.Equal(checkpoint.Economy!.ToCanonicalJson(), reopened.Economy!.ToCanonicalJson());
    }

    [Theory]
    [InlineData(1L, 2L)]
    [InlineData(2L, 1L)]
    public void RelocationDepartsFromEitherSiteWithPhysicalPartyAndConservedCargo(long origin, long destination)
    {
        var fixture = CreateRelocationEngine(new WorldSeed(918 + (ulong)origin), origin, destination);
        var engine = fixture.Engine;
        var before = engine.CreatePersistenceSnapshot();
        var originFoodBefore = (int)InvokePrivate(engine, "SettlementFor", origin)!.GetType()
            .GetProperty(nameof(SettlementState.FoodStored))!.GetValue(InvokePrivate(engine, "SettlementFor", origin))!;
        var otherFoodBefore = (int)InvokePrivate(engine, "SettlementFor", destination)!.GetType()
            .GetProperty(nameof(SettlementState.FoodStored))!.GetValue(InvokePrivate(engine, "SettlementFor", destination))!;
        var provisionQuantity = checked(RelocationProvisions(engine.CurrentMinute.Value) * fixture.Members.Length);
        var privateFoodBefore = (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Food)!;
        var woodBefore = (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Wood)!;
        var stoneBefore = (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Stone)!;
        var locationsBefore = fixture.Members.Select(x => x.Location).ToArray();
        var originScore = (int)InvokePrivate(engine, "MigrationOpportunityAt", origin)!;
        var destinationScore = (int)InvokePrivate(engine, "MigrationOpportunityAt", destination)!;
        Assert.True(destinationScore - originScore >= 5,
            $"Crafted opportunity did not favor {destination} over {origin}: {destinationScore} vs {originScore}.");
        Assert.True((bool)InvokePrivate(engine, "HasRelocationSupport", destination, fixture.Members.Length)!,
            $"Destination {destination} does not support the candidate household.");

        InvokePrivate(engine, "EvaluateMigrationRelocation");

        var departed = engine.CreatePersistenceSnapshot();
        var party = Assert.Single(departed.MigrationState!.InTransitParties);
        Assert.Equal(fixture.Household.Id.Value, party.HouseholdId);
        Assert.Equal(origin, party.OriginSettlementId);
        Assert.Equal(destination, party.DestinationSettlementId);
        Assert.Equal(MigrationJourneyKind.Relocation, party.JourneyKind);
        Assert.Equal(fixture.Members.Select(x => x.Id.Value).Order(), party.CitizenIds);
        Assert.Equal(provisionQuantity, party.Cargo.Where(x => x.Purpose == MigrationCargoPurpose.Provisions).Sum(x => x.Quantity));
        Assert.Equal(new Goods(), new Goods(
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Food)!,
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Wood)!,
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Stone)!));
        Assert.Equal(woodBefore, party.Cargo.Where(x => x.Purpose == MigrationCargoPurpose.Cargo && x.Good == MigrationCargoGood.Wood).Sum(x => x.Quantity));
        Assert.Equal(stoneBefore, party.Cargo.Where(x => x.Purpose == MigrationCargoPurpose.Cargo && x.Good == MigrationCargoGood.Stone).Sum(x => x.Quantity));
        Assert.Equal(originFoodBefore - checked((int)(provisionQuantity - privateFoodBefore)),
            (int)InvokePrivate(engine, "SettlementFor", origin)!.GetType().GetProperty(nameof(SettlementState.FoodStored))!
                .GetValue(InvokePrivate(engine, "SettlementFor", origin))!);
        Assert.Equal(otherFoodBefore, (int)InvokePrivate(engine, "SettlementFor", destination)!.GetType()
            .GetProperty(nameof(SettlementState.FoodStored))!.GetValue(InvokePrivate(engine, "SettlementFor", destination))!);
        Assert.Equal(locationsBefore, fixture.Members.Select(x => x.Location).ToArray());
        Assert.All(fixture.Members, member =>
        {
            Assert.Equal(CitizenAction.Explore, member.CurrentAction);
            Assert.Equal(party.DestinationSite, member.ActionTarget);
        });
        var beforeMigration = before.MigrationState!;
        var beforeDaughter = beforeMigration.DaughterSettlement!.CommunalStock;
        var beforeTotal = before.Economy!.StoredGoods(before.Settlement!).Total +
            beforeDaughter.FoodStored + beforeDaughter.WoodStored + beforeDaughter.StoneStored;
        var afterMigration = departed.MigrationState!;
        var afterDaughter = afterMigration.DaughterSettlement!.CommunalStock;
        var afterTotal = departed.Economy!.StoredGoods(departed.Settlement!).Total +
            afterDaughter.FoodStored + afterDaughter.WoodStored + afterDaughter.StoneStored + party.Cargo.Sum(x => x.Quantity);
        Assert.Equal(beforeTotal, afterTotal);
        MigrationValidation.Validate(departed);
    }

    [Fact]
    public void RelocationTravelMealsConsumeOnlyPartyProvisionsAfterTheyAreExhausted()
    {
        var fixture = CreateRelocationEngine(new WorldSeed(928), 1, 2);
        var engine = fixture.Engine;
        InvokePrivate(engine, "EvaluateMigrationRelocation");
        var party = Assert.Single(GetPrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        var provisionsBefore = party.Cargo.Where(x => x.Purpose == MigrationCargoPurpose.Provisions &&
            x.Good == MigrationCargoGood.Food).Sum(x => x.Quantity);
        Assert.True(provisionsBefore > 0);
        var origin = (SettlementState)InvokePrivate(engine, "SettlementFor", 1L)!;
        var destination = (SettlementState)InvokePrivate(engine, "SettlementFor", 2L)!;
        var originFoodBefore = origin.FoodStored;
        var destinationFoodBefore = destination.FoodStored;
        var privateFoodBefore = (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value,
            ResourceType.Food)!;
        var foodConsumedBefore = GetPrivateField<long>(engine, "_foodConsumed");
        var member = fixture.Members[0];
        long consumed = 0;
        var mealAttempts = checked((int)(provisionsBefore / CitizenSimulationRules.MealFoodUnits + 2));
        for (var index = 0; index < mealAttempts; index++)
            consumed += (int)InvokePrivate(engine, "ConsumeEconomicMeal", member,
                CitizenSimulationRules.MealFoodUnits)!;

        var afterParty = Assert.Single(GetPrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        Assert.Equal(provisionsBefore, consumed);
        Assert.Equal(provisionsBefore, GetPrivateField<long>(engine, "_foodConsumed") - foodConsumedBefore);
        Assert.DoesNotContain(afterParty.Cargo, x => x.Purpose == MigrationCargoPurpose.Provisions &&
            x.Good == MigrationCargoGood.Food);
        Assert.Equal(party.Cargo.Where(x => x.Purpose != MigrationCargoPurpose.Provisions ||
                x.Good != MigrationCargoGood.Food).ToArray(),
            afterParty.Cargo.Where(x => x.Purpose != MigrationCargoPurpose.Provisions ||
                x.Good != MigrationCargoGood.Food).ToArray());
        Assert.Equal(originFoodBefore, origin.FoodStored);
        Assert.Equal(destinationFoodBefore, destination.FoodStored);
        Assert.Equal(privateFoodBefore, (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value,
            ResourceType.Food)!);
    }

    [Fact]
    public void RelocationArrivalWaitsForTheWholeHouseholdThenMovesResidenceAndRestoresCargo()
    {
        var fixture = CreateRelocationEngine(new WorldSeed(920), 1, 2);
        var engine = fixture.Engine;
        InvokePrivate(engine, "EvaluateMigrationRelocation");
        var party = Assert.Single(GetPrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        var provisionQuantity = party.Cargo.Where(x => x.Purpose == MigrationCargoPurpose.Provisions).Sum(x => x.Quantity);
        var destinationFoodBefore = ((SettlementState)InvokePrivate(engine, "SettlementFor", 2L)!).FoodStored;
        var first = fixture.Members.First(x => x.AgeYears(engine.CurrentMinute) >= 18);
        first.Location = party.DestinationSite;
        first.CurrentAction = CitizenAction.Explore;
        first.ActionTarget = party.DestinationSite;

        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", first)!);
        var waiting = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        Assert.Single(waiting.InTransitParties);
        Assert.Equal(1, waiting.HouseholdResidences.Single(x => x.EntityId == fixture.Household.Id.Value).SettlementId);

        var final = fixture.Members.First(x => x.Id != first.Id);
        foreach (var member in fixture.Members.Where(x => x.Id != first.Id))
        {
            member.Location = party.DestinationSite;
            member.CurrentAction = CitizenAction.Explore;
            member.ActionTarget = party.DestinationSite;
        }
        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", final)!);

        var arrived = engine.CreatePersistenceSnapshot();
        var state = arrived.MigrationState!;
        Assert.Empty(state.InTransitParties);
        Assert.Equal(2, state.HouseholdResidences.Single(x => x.EntityId == fixture.Household.Id.Value).SettlementId);
        Assert.All(fixture.Members, member =>
            Assert.Equal(2, state.CitizenResidences.Single(x => x.EntityId == member.Id.Value).SettlementId));
        Assert.Contains(state.LastRelocations!, x => x.HouseholdId == fixture.Household.Id.Value &&
            x.LastCompletedMinute == engine.CurrentMinute.Value);
        Assert.Equal(destinationFoodBefore + provisionQuantity,
            arrived.MigrationState!.DaughterSettlement!.CommunalStock.FoodStored);
        Assert.Equal(5, (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Wood)!);
        Assert.Equal(3, (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Stone)!);
        Assert.Empty(state.InTransitParties);
        MigrationValidation.Validate(arrived);
    }

    [Fact]
    public void DaughterHouseholdRelocationToOriginMovesEveryMemberAndReturnsOwnedGoods()
    {
        var fixture = CreateRelocationEngine(new WorldSeed(922), 2, 1);
        var engine = fixture.Engine;
        var before = engine.CreatePersistenceSnapshot();
        var origin = (SettlementState)InvokePrivate(engine, "SettlementFor", 1L)!;

        InvokePrivate(engine, "EvaluateMigrationRelocation");
        var party = Assert.Single(GetPrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        var provisions = party.Cargo.Where(x => x.Purpose == MigrationCargoPurpose.Provisions).Sum(x => x.Quantity);
        var originFoodAfterDeparture = origin.FoodStored;
        Assert.Equal(2, party.OriginSettlementId);
        Assert.Equal(1, party.DestinationSettlementId);

        var firstAdult = fixture.Members.First(x => x.AgeYears(engine.CurrentMinute) >= 18);
        firstAdult.Location = party.DestinationSite;
        firstAdult.CurrentAction = CitizenAction.Explore;
        firstAdult.ActionTarget = party.DestinationSite;
        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", firstAdult)!);
        Assert.Single(GetPrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);

        foreach (var member in fixture.Members)
        {
            member.Location = party.DestinationSite;
            member.CurrentAction = CitizenAction.Explore;
            member.ActionTarget = party.DestinationSite;
        }
        var lastArrival = fixture.Members.Last();
        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", lastArrival)!);

        var arrived = engine.CreatePersistenceSnapshot();
        var state = arrived.MigrationState!;
        Assert.Empty(state.InTransitParties);
        Assert.Equal(1, state.HouseholdResidences.Single(x => x.EntityId == fixture.Household.Id.Value).SettlementId);
        Assert.All(fixture.Members, member =>
            Assert.Equal(1, state.CitizenResidences.Single(x => x.EntityId == member.Id.Value).SettlementId));
        Assert.Equal(originFoodAfterDeparture + provisions, origin.FoodStored);
        var householdStock = arrived.Economy!.Households.Single(x => x.HouseholdId == fixture.Household.Id.Value).Holdings;
        Assert.Equal(5, householdStock.Wood);
        Assert.Equal(3, householdStock.Stone);
        Assert.Equal(before.MigrationState!.StructureOwners, state.StructureOwners);
        Assert.Equal(before.MigrationState.FacilityOwners, state.FacilityOwners);
        Assert.Equal(before.MigrationState.WorkOrderOwners, state.WorkOrderOwners);
        Assert.Contains(state.LastRelocations!, x => x.HouseholdId == fixture.Household.Id.Value &&
            x.LastCompletedMinute == engine.CurrentMinute.Value);
        MigrationValidation.Validate(arrived);
    }

    [Fact]
    public void RelocationRequiresDestinationFoodAndShelterSupport()
    {
        var fixture = CreateRelocationEngine(new WorldSeed(923), 2, 1);
        var engine = fixture.Engine;
        var destination = (SettlementState)InvokePrivate(engine, "SettlementFor", 1L)!;
        var destinationPopulation = (int)InvokePrivate(engine, "PopulationAt", 1L)!;
        var requiredFood = checked(20 * (destinationPopulation + fixture.Members.Length));
        destination.FoodStored = requiredFood - 1;
        Assert.False((bool)InvokePrivate(engine, "HasRelocationSupport", 1L, fixture.Members.Length)!);
        var originScore = (int)InvokePrivate(engine, "MigrationOpportunityAt", 2L)!;
        var destinationScore = (int)InvokePrivate(engine, "MigrationOpportunityAt", 1L)!;
        Assert.True(destinationScore - originScore >= 5,
            $"The fixture must still favor the destination when food is one unit below support: {destinationScore} vs {originScore}.");
        var privateGoodsBefore = new Goods(
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Food)!,
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Wood)!,
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Stone)!);

        InvokePrivate(engine, "EvaluateMigrationRelocation");

        Assert.Empty(GetPrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        Assert.Equal(privateGoodsBefore, new Goods(
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Food)!,
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Wood)!,
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Stone)!));
    }

    [Fact]
    public void RelocationWaitsForDestinationStorageCapacityBeforeWithdrawingGoods()
    {
        var fixture = CreateRelocationEngine(new WorldSeed(926), 1, 2);
        var engine = fixture.Engine;
        var destination = (SettlementState)InvokePrivate(engine, "SettlementFor", 2L)!;
        var capacity = (int)InvokePrivate(engine, "StorageCapacityAt", 2L)!;
        var owned = (Goods)InvokePrivate(engine, "OwnedStoredGoodsAt", 2L)!;
        var living = (int)InvokePrivate(engine, "LivingStoredQuantityAt", 2L)!;
        var reserved = (long)InvokePrivate(engine, "TradeCargoReservedAt", 2L)!;
        var beforeUsed = checked((long)destination.StorageUsed + owned.Total + living + reserved);
        var freeStorage = capacity - beforeUsed;
        Assert.True(freeStorage > 0);
        var addFood = checked((int)Math.Max(0, freeStorage - 1));
        var originalFood = destination.FoodStored;
        destination.FoodStored = checked(originalFood + addFood);
        var produced = GetPrivateField<Goods>(engine, "_producedGoods");
        SetPrivateField(engine, "_producedGoods", produced with { Food = checked(produced.Food + addFood) });

        var privateGoodsBefore = new Goods(
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Food)!,
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Wood)!,
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Stone)!);
        var originFoodBefore = ((SettlementState)InvokePrivate(engine, "SettlementFor", 1L)!).FoodStored;
        Assert.True(destination.FoodStored >= 20L * ((int)InvokePrivate(engine, "PopulationAt", 2L)! +
            fixture.Members.Length));

        InvokePrivate(engine, "EvaluateMigrationRelocation");

        Assert.Empty(GetPrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        Assert.Equal(privateGoodsBefore, new Goods(
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Food)!,
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Wood)!,
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Stone)!));
        Assert.Equal(originFoodBefore, ((SettlementState)InvokePrivate(engine, "SettlementFor", 1L)!).FoodStored);
        MigrationValidation.Validate(engine.CreatePersistenceSnapshot());
    }

    [Fact]
    public void RelocationReturnsCargoIntactWhenDestinationStorageFillsDuringTravel()
    {
        var fixture = CreateRelocationEngine(new WorldSeed(927), 1, 2);
        var engine = fixture.Engine;
        var before = engine.CreatePersistenceSnapshot();
        var beforeTotal = checked(before.Economy!.StoredGoods(before.Settlement!).Total +
            before.MigrationState!.DaughterSettlement!.CommunalStock.FoodStored +
            before.MigrationState.DaughterSettlement.CommunalStock.WoodStored +
            before.MigrationState.DaughterSettlement.CommunalStock.StoneStored);
        InvokePrivate(engine, "EvaluateMigrationRelocation");
        var party = Assert.Single(GetPrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        var cargoTotal = party.Cargo.Sum(x => x.Quantity);

        var destination = (SettlementState)InvokePrivate(engine, "SettlementFor", 2L)!;
        var capacity = (int)InvokePrivate(engine, "StorageCapacityAt", 2L)!;
        var owned = (Goods)InvokePrivate(engine, "OwnedStoredGoodsAt", 2L)!;
        var living = (int)InvokePrivate(engine, "LivingStoredQuantityAt", 2L)!;
        var reserved = (long)InvokePrivate(engine, "TradeCargoReservedAt", 2L)!;
        var beforeUsed = checked((long)destination.StorageUsed + owned.Total + living + reserved);
        var freeStorage = capacity - beforeUsed;
        Assert.True(freeStorage >= cargoTotal,
            "The fixture must have had enough room for departure before storage fills in transit.");
        var addFood = checked((int)(freeStorage - 1));
        var previousDestinationFood = destination.FoodStored;
        destination.FoodStored = checked(previousDestinationFood + addFood);
        var produced = GetPrivateField<Goods>(engine, "_producedGoods");
        SetPrivateField(engine, "_producedGoods", produced with { Food = checked(produced.Food + addFood) });

        var adult = fixture.Members.First(x => x.AgeYears(engine.CurrentMinute) >= 18);
        foreach (var member in fixture.Members)
        {
            member.Location = party.DestinationSite;
            member.CurrentAction = CitizenAction.Explore;
            member.ActionTarget = party.DestinationSite;
        }
        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", adult)!);
        var returning = Assert.Single(GetPrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        Assert.True(returning.Returning);
        Assert.Equal(1, GetPrivateField<MigrationWorldState>(engine, "_migrationState").HouseholdResidences
            .Single(x => x.EntityId == fixture.Household.Id.Value).SettlementId);
        Assert.Equal(cargoTotal, returning.Cargo.Sum(x => x.Quantity));

        foreach (var member in fixture.Members)
        {
            member.Location = engine.World.StartingSite;
            member.CurrentAction = CitizenAction.Explore;
            member.ActionTarget = engine.World.StartingSite;
        }
        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", adult)!);

        var returned = engine.CreatePersistenceSnapshot();
        Assert.Empty(returned.MigrationState!.InTransitParties);
        Assert.Equal(1, returned.MigrationState.HouseholdResidences.Single(x => x.EntityId == fixture.Household.Id.Value)
            .SettlementId);
        Assert.Equal(checked(beforeTotal + addFood), checked(returned.Economy!.StoredGoods(returned.Settlement!).Total +
            returned.MigrationState.DaughterSettlement!.CommunalStock.FoodStored +
            returned.MigrationState.DaughterSettlement.CommunalStock.WoodStored +
            returned.MigrationState.DaughterSettlement.CommunalStock.StoneStored));
        MigrationValidation.Validate(returned);
    }

    [Fact]
    public void RelocationCooldownRejectsRecentlyMovedHouseholdWithoutWithdrawingGoods()
    {
        var fixture = CreateRelocationEngine(new WorldSeed(924), 1, 2);
        var engine = fixture.Engine;
        var originScore = (int)InvokePrivate(engine, "MigrationOpportunityAt", 1L)!;
        var destinationScore = (int)InvokePrivate(engine, "MigrationOpportunityAt", 2L)!;
        Assert.True(destinationScore - originScore >= 5,
            $"The fixture must otherwise favor the destination: {destinationScore} vs {originScore}.");
        Assert.True((bool)InvokePrivate(engine, "HasRelocationSupport", 2L, fixture.Members.Length)!);
        var state = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        var lastMoves = (state.LastRelocations ?? Array.Empty<MigrationHouseholdRelocationState>())
            .Where(x => x.HouseholdId != fixture.Household.Id.Value)
            .Append(new MigrationHouseholdRelocationState(fixture.Household.Id.Value, engine.CurrentMinute.Value))
            .OrderBy(x => x.HouseholdId).ToArray();
        SetPrivateField(engine, "_migrationState", new MigrationWorldState(state.Version,
            state.CitizenResidences, state.HouseholdResidences, state.StructureOwners, state.FacilityOwners,
            state.WorkOrderOwners, state.DaughterSettlement, state.InTransitParties, state.FoundingPressure,
            lastMoves, state.LastVisitAttemptYear));
        var privateGoodsBefore = new Goods(
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Food)!,
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Wood)!,
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Stone)!);

        InvokePrivate(engine, "EvaluateMigrationRelocation");

        Assert.Empty(GetPrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        Assert.Equal(privateGoodsBefore, new Goods(
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Food)!,
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Wood)!,
            (long)InvokePrivate(engine, "AvailablePrivate", fixture.Household.Id.Value, ResourceType.Stone)!));
    }

    [Fact]
    public void RelocationThatLosesDestinationSupportReturnsHouseholdAndCargoToOrigin()
    {
        var fixture = CreateRelocationEngine(new WorldSeed(925), 1, 2);
        var engine = fixture.Engine;
        var origin = (SettlementState)InvokePrivate(engine, "SettlementFor", 1L)!;
        InvokePrivate(engine, "EvaluateMigrationRelocation");
        var party = Assert.Single(GetPrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        var provisions = party.Cargo.Where(x => x.Purpose == MigrationCargoPurpose.Provisions).Sum(x => x.Quantity);
        var originFoodAfterDeparture = origin.FoodStored;
        var unsupportedDestination = (SettlementState)InvokePrivate(engine, "SettlementFor", 2L)!;
        var removedDestinationFood = unsupportedDestination.FoodStored;
        unsupportedDestination.FoodStored = 0;
        var produced = GetPrivateField<Goods>(engine, "_producedGoods");
        SetPrivateField(engine, "_producedGoods", produced with
        {
            Food = checked(produced.Food - removedDestinationFood)
        });

        var adult = fixture.Members.First(x => x.AgeYears(engine.CurrentMinute) >= 18);
        foreach (var member in fixture.Members)
        {
            member.Location = party.DestinationSite;
            member.CurrentAction = CitizenAction.Explore;
            member.ActionTarget = party.DestinationSite;
        }
        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", adult)!);
        var returning = Assert.Single(GetPrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        Assert.True(returning.Returning);
        Assert.Equal(1, returning.DestinationSettlementId);

        foreach (var member in fixture.Members)
        {
            member.Location = engine.World.StartingSite;
            member.CurrentAction = CitizenAction.Explore;
            member.ActionTarget = engine.World.StartingSite;
        }
        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", adult)!);

        var returned = engine.CreatePersistenceSnapshot();
        Assert.Empty(returned.MigrationState!.InTransitParties);
        Assert.Equal(1, returned.MigrationState.HouseholdResidences
            .Single(x => x.EntityId == fixture.Household.Id.Value).SettlementId);
        Assert.All(fixture.Members, member =>
            Assert.Equal(1, returned.MigrationState.CitizenResidences.Single(x => x.EntityId == member.Id.Value).SettlementId));
        Assert.Equal(originFoodAfterDeparture + provisions, origin.FoodStored);
        var householdStock = returned.Economy!.Households.Single(x => x.HouseholdId == fixture.Household.Id.Value).Holdings;
        Assert.Equal(5, householdStock.Wood);
        Assert.Equal(3, householdStock.Stone);
        Assert.DoesNotContain(returned.MigrationState.LastRelocations!, x => x.HouseholdId == fixture.Household.Id.Value);
        MigrationValidation.Validate(returned);
    }

    private sealed record RelocationFixture(SimulationEngine Engine, Household Household, Citizen[] Members);

    private static long RelocationProvisions(long minute)
    {
        var seasonIndex = (minute % WorldCalendar.MinutesPerYear) /
            ((long)WorldCalendar.DaysPerSeason * WorldCalendar.MinutesPerDay);
        var rate = seasonIndex == 3 ? NeedsProjection.WinterHungerRatePerMinute : NeedsProjection.HungerRatePerMinute;
        var hungerPoints = checked((long)rate * 14 * WorldCalendar.MinutesPerDay);
        return checked((hungerPoints * CitizenSimulationRules.MealFoodUnits +
            CitizenSimulationRules.FullHungerReduction - 1) / CitizenSimulationRules.FullHungerReduction);
    }

    private static RelocationFixture CreateRelocationEngine(WorldSeed seed, long origin, long destination)
    {
        var baseFixture = CreateDaughterFixture(seed);
        var engine = SimulationEngine.FromPersistenceSnapshot(baseFixture.Snapshot);
        var households = GetPrivateField<Dictionary<long, Household>>(engine, "_households");
        var citizens = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens");
        var household = households.Values.Where(x => x.DissolvedMinute is null &&
                (long)InvokePrivate(engine, "SiteIdForHousehold", x.Id.Value)! == origin &&
                citizens.Values.Any(c => c.IsAlive && c.HouseholdId == x.Id && c.AgeYears(engine.CurrentMinute) >= 18))
            .OrderBy(x => x.Id.Value).First();
        var members = citizens.Values.Where(x => x.IsAlive && x.HouseholdId == household.Id)
            .OrderBy(x => x.Id.Value).ToArray();
        if (members.Length < 2)
        {
            var additional = citizens.Values.Where(x => x.IsAlive && x.HouseholdId != household.Id &&
                    (long)InvokePrivate(engine, "SiteIdForCitizen", x)! == origin)
                .OrderBy(x => x.Id.Value).First();
            var priorHousehold = households[additional.HouseholdId!.Value.Value];
            additional.HouseholdId = household.Id;
            additional.HomeStructureId = null;
            foreach (var partner in citizens.Values.Where(x => x.PartnerId == additional.Id || x.PartnerId == members[0].Id))
                partner.PartnerId = null;
            additional.PartnerId = null;
            if (!citizens.Values.Any(x => x.IsAlive && x.HouseholdId == priorHousehold.Id))
            {
                priorHousehold.DissolvedMinute ??= engine.CurrentMinute.Value;
                priorHousehold.DwellingStructureId = null;
            }
            GetPrivateField<SortedDictionary<long, EconomicMember>>(engine, "_economicMembers")[additional.Id.Value] =
                GetPrivateField<SortedDictionary<long, EconomicMember>>(engine, "_economicMembers")[additional.Id.Value] with
                { HouseholdId = household.Id.Value };
            members = citizens.Values.Where(x => x.IsAlive && x.HouseholdId == household.Id)
                .OrderBy(x => x.Id.Value).ToArray();
            InvokePrivate(engine, "ReconcileEconomicHouseholds");
        }

        var current = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        var holdings = GetPrivateField<SortedDictionary<long, HouseholdStock>>(engine, "_householdStocks");
        var priorHoldings = holdings[household.Id.Value].Holdings;
        holdings[household.Id.Value] = holdings[household.Id.Value] with { Holdings = new Goods(10, 5, 3) };
        var produced = GetPrivateField<Goods>(engine, "_producedGoods");
        produced = produced with
        {
            Food = checked(produced.Food + 10 - priorHoldings.Food),
            Wood = checked(produced.Wood + 5 - priorHoldings.Wood),
            Stone = checked(produced.Stone + 3 - priorHoldings.Stone)
        };

        var originStock = (SettlementState)InvokePrivate(engine, "SettlementFor", origin)!;
        var destinationStock = (SettlementState)InvokePrivate(engine, "SettlementFor", destination)!;
        var provision = checked(RelocationProvisions(engine.CurrentMinute.Value) * members.Length);
        var originFood = origin == 2 ? checked((int)(provision - 10)) : 500;
        var destinationFood = destination == 2 ? 3000 : 600;
        var foodDelta = checked(originFood - originStock.FoodStored + destinationFood - destinationStock.FoodStored);
        originStock.FoodStored = originFood;
        destinationStock.FoodStored = destinationFood;
        produced = produced with { Food = checked(produced.Food + foodDelta) };

        var structureOwners = current.StructureOwners.ToList();
        var structures = GetPrivateField<Dictionary<long, Structure>>(engine, "_structures");
        var contributions = GetPrivateField<Dictionary<(long StructureId, long CitizenId), StructureContribution>>(engine, "_structureContributions");
        var counters = GetPrivateField<DeterministicCounters>(engine, "_counters");
        var structuresToAdd = destination == 1 ? 15 : 2;
        var structureFoodAccounting = new Goods();
        var citizenId = citizens.Values.Where(x => x.IsAlive &&
                (long)InvokePrivate(engine, "SiteIdForCitizen", x)! == destination)
            .OrderBy(x => x.Id.Value).First().Id;
        for (var index = 0; index < structuresToAdd; index++)
        {
            var id = counters.AllocateStructureId().Value;
            var location = FindFreeBuildableTile(engine, destination, destination == 1 ? engine.World.StartingSite : baseFixture.DaughterSite,
                structures.Values.ToArray(), []);
            var shelter = new Structure(new StructureId(id), StructureType.Shelter, location, 0,
                StructureDefinitions.ShelterRequiredWood, StructureDefinitions.ShelterRequiredStone,
                StructureDefinitions.ShelterRequiredWork)
            {
                Status = StructureStatus.Complete,
                CompletedMinute = engine.CurrentMinute.Value,
                DeliveredWood = StructureDefinitions.ShelterRequiredWood,
                DeliveredStone = StructureDefinitions.ShelterRequiredStone,
                CompletedWork = StructureDefinitions.ShelterRequiredWork
            };
            structures.Add(id, shelter);
            contributions.Add((id, citizenId.Value), new StructureContribution(shelter.Id, citizenId, shelter.CompletedWork,
                shelter.DeliveredWood, shelter.DeliveredStone));
            structureOwners.Add(new MigrationEntityResidence(id, destination));
            structureFoodAccounting = structureFoodAccounting with
            {
                Wood = checked(structureFoodAccounting.Wood + shelter.DeliveredWood),
                Stone = checked(structureFoodAccounting.Stone + shelter.DeliveredStone)
            };
        }
        produced = produced.Plus(structureFoodAccounting);
        SetPrivateField(engine, "_producedGoods", produced);
        SetPrivateField(engine, "_migrationState", new MigrationWorldState(current.Version, current.CitizenResidences,
            current.HouseholdResidences, structureOwners.OrderBy(x => x.EntityId).ToArray(), current.FacilityOwners,
            current.WorkOrderOwners, current.DaughterSettlement, current.InTransitParties, current.FoundingPressure,
            current.LastRelocations));

        var activeAtOrigin = households.Values.Where(x => x.DissolvedMinute is null &&
                (long)InvokePrivate(engine, "SiteIdForHousehold", x.Id.Value)! == origin && x.Id != household.Id)
            .Select(x => new MigrationHouseholdRelocationState(x.Id.Value, engine.CurrentMinute.Value)).ToArray();
        var state = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        SetPrivateField(engine, "_migrationState", new MigrationWorldState(state.Version, state.CitizenResidences,
            state.HouseholdResidences, state.StructureOwners, state.FacilityOwners, state.WorkOrderOwners,
            state.DaughterSettlement, state.InTransitParties, state.FoundingPressure, activeAtOrigin));

        return new RelocationFixture(engine, household, members);
    }

    private static Structure CreateActiveShelter(StructureId id, TileCoordinate location) =>
        new(id, StructureType.Shelter, location, 0, StructureDefinitions.ShelterRequiredWood,
            StructureDefinitions.ShelterRequiredStone, StructureDefinitions.ShelterRequiredWork);

    private static void AddSheltersForDemandChecks(SimulationEngine engine, long siteId, TileCoordinate site,
        bool migration)
    {
        var population = (int)InvokePrivate(engine, "PopulationAt", siteId)!;
        var capacity = (int)InvokePrivate(engine, "ShelterCapacityAt", siteId)!;
        var missingCapacity = Math.Max(0, checked(population + CitizenSimulationRules.DesiredSpareShelterSlots - capacity));
        var count = (missingCapacity + CitizenSimulationRules.ShelterCapacityPerBuilding - 1) /
            CitizenSimulationRules.ShelterCapacityPerBuilding;
        if (count == 0) return;

        var structures = GetPrivateField<Dictionary<long, Structure>>(engine, "_structures");
        var counters = GetPrivateField<DeterministicCounters>(engine, "_counters");
        var additions = new List<Structure>(count);
        for (var index = 0; index < count; index++)
        {
            var id = counters.AllocateStructureId();
            var location = FindFreeBuildableTile(engine, siteId, site, structures.Values.Concat(additions).ToArray(), []);
            var shelter = new Structure(id, StructureType.Shelter, location, engine.CurrentMinute.Value,
                StructureDefinitions.ShelterRequiredWood, StructureDefinitions.ShelterRequiredStone,
                StructureDefinitions.ShelterRequiredWork)
            {
                Status = StructureStatus.Complete,
                CompletedMinute = engine.CurrentMinute.Value,
                DeliveredWood = StructureDefinitions.ShelterRequiredWood,
                DeliveredStone = StructureDefinitions.ShelterRequiredStone,
                CompletedWork = StructureDefinitions.ShelterRequiredWork
            };
            structures.Add(id.Value, shelter);
            additions.Add(shelter);
        }

        if (migration)
        {
            var state = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
            SetPrivateField(engine, "_migrationState", WithAdditionalStructureOwners(state, additions,
                Enumerable.Repeat(siteId, additions.Count).ToArray()));
        }
    }

    private static MigrationWorldState WithAdditionalStructureOwners(MigrationWorldState state,
        IReadOnlyList<Structure> structures, long[] siteIds)
    {
        var owners = state.StructureOwners.Concat(structures.Select((structure, index) =>
                new MigrationEntityResidence(structure.Id.Value, siteIds[index])))
            .OrderBy(x => x.EntityId).ToArray();
        return new MigrationWorldState(state.Version, state.CitizenResidences, state.HouseholdResidences,
            owners, state.FacilityOwners, state.WorkOrderOwners, state.DaughterSettlement,
            state.InTransitParties, state.FoundingPressure);
    }

    private static TileCoordinate FindFreeBuildableTile(SimulationEngine engine, long siteId,
        TileCoordinate center, IReadOnlyList<Structure> structures, IReadOnlyCollection<TileCoordinate> reserved)
    {
        var occupied = structures.Select(x => x.Location).Concat(reserved).ToHashSet();
        var resourceTiles = engine.World.Resources.Select(x => x.Coordinate).ToHashSet();
        return engine.World.Tiles.Where(tile => tile.Coordinate != engine.World.StartingSite && tile.Buildable &&
                tile.Terrain != TerrainType.Freshwater && !occupied.Contains(tile.Coordinate) &&
                !resourceTiles.Contains(tile.Coordinate))
            .OrderBy(tile => Math.Abs(tile.Coordinate.X - center.X) + Math.Abs(tile.Coordinate.Y - center.Y))
            .ThenBy(tile => tile.Coordinate.Y).ThenBy(tile => tile.Coordinate.X)
            .First(tile => SiteIdForLocation(engine, tile.Coordinate) == siteId).Coordinate;
    }

    private static DeterministicCountersSnapshot WithNextEntityId(DeterministicCountersSnapshot counters,
        long nextEntityId) => new(nextEntityId, counters.NextHistoricalEventId, counters.NextScheduledEventSequence);

    private static TileCoordinate FindOtherWalkableTile(WorldMap world)
    {
        for (var y = 0; y < world.Height; y++)
        for (var x = 0; x < world.Width; x++)
        {
            var tile = new TileCoordinate(x, y);
            if (tile != world.StartingSite && world.GetTile(tile).Walkable) return tile;
        }
        throw new InvalidOperationException("The seeded world has no distinct walkable tile.");
    }

    private static string CommonState(SimulationPersistenceSnapshot snapshot) => JsonSerializer.Serialize(new
    {
        snapshot.Counters,
        snapshot.ScheduledEvents,
        snapshot.Citizens,
        snapshot.ResourceStates,
        snapshot.Settlement,
        snapshot.SurvivalVersion,
        snapshot.SettlementVersion,
        snapshot.Structures,
        snapshot.StructureContributions,
        snapshot.SocialVersion,
        snapshot.Relationships,
        snapshot.Households,
        snapshot.HistoryVersion,
        snapshot.HistoryState,
        snapshot.HistoricalEvents,
        snapshot.HistoricalEventCitizens,
        snapshot.HistoricalEventStructures,
        snapshot.StatisticsSamples,
        snapshot.Memories,
        Agriculture = snapshot.Agriculture?.ToCanonicalJson(),
        Economy = snapshot.Economy?.ToCanonicalJson(),
        snapshot.LivingStateJson
    });

    private static SimulationPersistenceSnapshot CopySnapshot(SimulationPersistenceSnapshot snapshot,
        string? migrationStateJson = null, IReadOnlyList<Citizen>? citizens = null,
        IReadOnlyList<Structure>? structures = null, SettlementState? settlement = null,
        EconomyState? economy = null, DeterministicCountersSnapshot? counters = null,
        IReadOnlyList<StructureContribution>? contributions = null) => new(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion,
        snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration, counters ?? snapshot.Counters,
        snapshot.ScheduledEvents, snapshot.World, citizens ?? snapshot.Citizens, snapshot.CitizenGenerationVersion,
        snapshot.ResourceStates, settlement ?? snapshot.Settlement, snapshot.SurvivalVersion, snapshot.SettlementVersion,
        structures ?? snapshot.Structures, contributions ?? snapshot.StructureContributions, snapshot.SocialVersion, snapshot.Relationships,
        snapshot.Households, snapshot.HistoryVersion, snapshot.HistoryState, snapshot.HistoricalEvents,
        snapshot.HistoricalEventCitizens, snapshot.HistoricalEventStructures, snapshot.StatisticsSamples,
        snapshot.Memories, snapshot.Agriculture, economy ?? snapshot.Economy, snapshot.LivingStateJson,
        migrationStateJson ?? snapshot.MigrationStateJson);

    private static MigrationWorldState WithDaughterStock(MigrationWorldState state,
        MigrationSettlementStockState stock, IReadOnlyList<MigrationEntityResidence>? structureOwners = null)
    {
        var daughter = state.DaughterSettlement ?? throw new InvalidOperationException("The fixture needs a daughter settlement.");
        return new MigrationWorldState(state.Version, state.CitizenResidences, state.HouseholdResidences,
            structureOwners ?? state.StructureOwners, state.FacilityOwners, state.WorkOrderOwners,
            new MigrationDaughterSettlementState(daughter.Site, stock, daughter.LivingGoods),
            state.InTransitParties, state.FoundingPressure, state.LastRelocations, state.LastVisitAttemptYear);
    }

    private static (SimulationPersistenceSnapshot Snapshot, TileCoordinate DaughterSite) CreateDaughterFixture(WorldSeed seed)
    {
        var baseline = new SimulationEngine(seed, simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion).CreatePersistenceSnapshot();
        var world = baseline.World!;
        var firstCosts = LivingTravelCosts.Compute(world, world.StartingSite);
        var partition = world.Tiles.Where(x => x.Coordinate != world.StartingSite && x.Walkable && x.Buildable &&
                world.GetResources(x.Coordinate).Count == 0 && firstCosts.ContainsKey(x.Coordinate))
            .OrderByDescending(x => firstCosts[x.Coordinate]).ThenBy(x => x.Coordinate.Y).ThenBy(x => x.Coordinate.X)
            .Select(x => (Site: x.Coordinate, Costs: LivingTravelCosts.Compute(world, x.Coordinate)))
            .First(x => world.Resources.Any(node => node.Type == ResourceType.Food && firstCosts.TryGetValue(node.Coordinate, out var first) &&
                    x.Costs.TryGetValue(node.Coordinate, out var second) && second < first) &&
                world.Resources.Any(node => node.Type == ResourceType.Food && firstCosts.TryGetValue(node.Coordinate, out var first) &&
                    x.Costs.TryGetValue(node.Coordinate, out var second) && first <= second));
        var daughterSite = partition.Site;
        var marketLocations = world.Tiles.Where(x => x.Coordinate != world.StartingSite && x.Coordinate != daughterSite &&
                x.Walkable && x.Buildable && world.GetResources(x.Coordinate).Count == 0 &&
                firstCosts.TryGetValue(x.Coordinate, out var first) && partition.Costs.TryGetValue(x.Coordinate, out var second))
            .ToArray();
        var marketOneSite = marketLocations.Where(x => firstCosts[x.Coordinate] <= partition.Costs[x.Coordinate])
            .OrderBy(x => firstCosts[x.Coordinate]).ThenBy(x => x.Coordinate.Y).ThenBy(x => x.Coordinate.X).First().Coordinate;
        var marketTwoSite = marketLocations.Where(x => partition.Costs[x.Coordinate] < firstCosts[x.Coordinate])
            .OrderBy(x => partition.Costs[x.Coordinate]).ThenBy(x => x.Coordinate.Y).ThenBy(x => x.Coordinate.X).First().Coordinate;
        var households = baseline.Households.Where(x => x.DissolvedMinute is null).OrderBy(x => x.Id.Value).ToArray();
        Assert.True(households.Length >= 4, "The fixture needs two households at each site for local barter.");
        var siteTwoHouseholds = households.Take(2).Select(x => x.Id.Value).ToHashSet();
        var siteOneTradeHouseholds = households.Skip(2).Take(2).Select(x => x.Id.Value).ToHashSet();
        var householdSites = baseline.Households.ToDictionary(x => x.Id.Value, x => siteTwoHouseholds.Contains(x.Id.Value) ? 2L : 1L);
        var citizens = baseline.Citizens.Select(citizen =>
        {
            var siteId = citizen.HouseholdId is { } household && householdSites.TryGetValue(household.Value, out var known) ? known : 1;
            citizen.Location = siteId == 2 ? daughterSite : world.StartingSite;
            if (siteId == 2) citizen.HomeStructureId = null;
            return citizen;
        }).ToArray();
        var citizenSites = citizens.ToDictionary(x => x.Id.Value,
            x => x.HouseholdId is { } household && householdSites.TryGetValue(household.Value, out var known) ? known : 1L);

        var economy = baseline.Economy!;
        var stocks = economy.Households.Select(stock =>
        {
            var woodDonor = siteTwoHouseholds.Contains(stock.HouseholdId) ? stock.HouseholdId == siteTwoHouseholds.Min() : siteOneTradeHouseholds.Contains(stock.HouseholdId) && stock.HouseholdId == siteOneTradeHouseholds.Min();
            var stoneDonor = siteTwoHouseholds.Contains(stock.HouseholdId) ? stock.HouseholdId == siteTwoHouseholds.Max() : siteOneTradeHouseholds.Contains(stock.HouseholdId) && stock.HouseholdId == siteOneTradeHouseholds.Max();
            return stock with { Holdings = woodDonor ? new Goods(Wood: 100) : stoneDonor ? new Goods(Stone: 100) : new Goods() };
        }).ToArray();
        economy = economy with
        {
            Households = Array.AsReadOnly(stocks),
            Produced = economy.Produced.Plus(new Goods(
                Wood: EconomyRules.MarketWood * 2L + 200L,
                Stone: EconomyRules.MarketStone * 2L + 200L))
        };

        var marketOneId = baseline.Counters.NextEntityId;
        var marketTwoId = checked(marketOneId + 1);
        var marketOne = CompleteMarketplace(marketOneId, marketOneSite);
        var marketTwo = CompleteMarketplace(marketTwoId, marketTwoSite);
        var marketplaceContributions = new[] { marketOne, marketTwo }.Select(x => new StructureContribution(
            x.Id, citizens[0].Id, x.CompletedWork, x.DeliveredWood, x.DeliveredStone));
        var structureOwners = baseline.MigrationState!.StructureOwners
            .Append(new MigrationEntityResidence(marketOneId, 1))
            .Append(new MigrationEntityResidence(marketTwoId, 2)).ToArray();
        var migration = new MigrationWorldState(1,
            citizens.Select(x => new MigrationEntityResidence(x.Id.Value, citizenSites[x.Id.Value])).ToArray(),
            baseline.Households.Select(x => new MigrationEntityResidence(x.Id.Value, householdSites[x.Id.Value])).ToArray(),
            structureOwners, baseline.MigrationState.FacilityOwners, baseline.MigrationState.WorkOrderOwners,
            new MigrationDaughterSettlementState(daughterSite,
                new MigrationSettlementStockState(100, 0, 0, 4000, baseline.WorldMinute.Value, baseline.WorldMinute.Value),
                Enum.GetValues<LivingGood>().Select(x => new LivingStock(x, 0)).ToArray()), baseline.MigrationState.InTransitParties);
        var settlementOne = new SettlementState(300, 0, 0, baseline.Settlement!.BaseStorageCapacity,
            baseline.Settlement.DemandUpdatedMinute, baseline.Settlement.ExposureConsequencesStartMinute);
        var counters = baseline.Counters with { NextEntityId = checked(marketTwoId + 1) };
        var snapshot = CopySnapshot(baseline, migration.ToCanonicalJson(), citizens,
            baseline.Structures.Concat([marketOne, marketTwo]).ToArray(), settlementOne, economy, counters,
            baseline.StructureContributions.Concat(marketplaceContributions).ToArray());
        return (snapshot, daughterSite);
    }

    private static Structure CompleteMarketplace(long id, TileCoordinate location) => new(
        new StructureId(id), StructureType.Marketplace, location, 0, EconomyRules.MarketWood,
        EconomyRules.MarketStone, EconomyRules.MarketWork)
    {
        Status = StructureStatus.Complete,
        CompletedMinute = 0,
        DeliveredWood = EconomyRules.MarketWood,
        DeliveredStone = EconomyRules.MarketStone,
        CompletedWork = EconomyRules.MarketWork
    };

    private static (Household Household, Citizen[] Members) EnsureTwoMemberHousehold(SimulationEngine engine)
    {
        var households = GetPrivateField<Dictionary<long, Household>>(engine, "_households");
        var citizens = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens");
        var household = households.Values.Where(x => x.DissolvedMinute is null).OrderBy(x => x.Id.Value)
            .First(x => citizens.Values.Any(c => c.IsAlive && c.HouseholdId == x.Id));
        var member = citizens.Values.Where(x => x.IsAlive && x.HouseholdId == household.Id)
            .OrderBy(x => x.Id.Value).First();
        var additional = citizens.Values.Where(x => x.IsAlive && x.HouseholdId != household.Id)
            .OrderBy(x => x.Id.Value).First();
        var priorHousehold = households[additional.HouseholdId!.Value.Value];
        additional.HouseholdId = household.Id;
        additional.HomeStructureId = null;
        foreach (var partner in citizens.Values.Where(x => x.PartnerId == additional.Id || x.PartnerId == member.Id))
            partner.PartnerId = null;
        additional.PartnerId = null;
        if (!citizens.Values.Any(x => x.IsAlive && x.HouseholdId == priorHousehold.Id))
        {
            priorHousehold.DissolvedMinute ??= engine.CurrentMinute.Value;
            priorHousehold.DwellingStructureId = null;
        }
        var economicMembers = GetPrivateField<SortedDictionary<long, EconomicMember>>(engine, "_economicMembers");
        economicMembers[additional.Id.Value] = economicMembers[additional.Id.Value] with { HouseholdId = household.Id.Value };
        return (household, citizens.Values.Where(x => x.IsAlive && x.HouseholdId == household.Id)
            .OrderBy(x => x.Id.Value).ToArray());
    }

    private static Structure AddCompletedHarvestFarm(SimulationEngine engine, long settlementId,
        TileCoordinate location, int yield)
    {
        var counters = GetPrivateField<DeterministicCounters>(engine, "_counters");
        var id = counters.AllocateStructureId();
        var farm = new Structure(id, StructureType.Farm, location, engine.CurrentMinute.Value,
            AgricultureRules.FarmWood, AgricultureRules.FarmStone, AgricultureRules.FarmWork)
        {
            Status = StructureStatus.Complete,
            CompletedMinute = engine.CurrentMinute.Value,
            DeliveredWood = AgricultureRules.FarmWood,
            DeliveredStone = AgricultureRules.FarmStone,
            CompletedWork = AgricultureRules.FarmWork
        };
        GetPrivateField<Dictionary<long, Structure>>(engine, "_structures").Add(id.Value, farm);
        GetPrivateField<SortedDictionary<long, FarmCrop>>(engine, "_farms").Add(id.Value,
            new FarmCrop(id.Value, engine.CurrentMinute.ToCalendar().Year, CropStage.Harvest,
                AgricultureRules.PlantingWork, AgricultureRules.TendingWork, yield, yield, 0));
        if (engine.GetType().GetField("_migrationState", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(engine) is MigrationWorldState migration)
            SetPrivateField(engine, "_migrationState", WithAdditionalStructureOwners(migration, [farm], [settlementId]));
        return farm;
    }

    private static (int Harvested, int Food, int Grain) HarvestAndDeliver(SimulationEngine engine,
        long settlementId, Citizen citizen, Structure farm)
    {
        var minute = engine.CurrentMinute;
        var foodBefore = checked(((SettlementState)InvokePrivate(engine, "SettlementFor", settlementId)!).FoodStored +
            ((Goods)InvokePrivate(engine, "OwnedStoredGoodsAt", settlementId)!).Food);
        var grainBefore = (int)InvokePrivate(engine, "GoodAt", settlementId, LivingGood.Grain)!;
        citizen.Location = farm.Location;
        citizen.CurrentAction = CitizenAction.HaulHarvest;
        citizen.ActionPhase = CitizenActionPhase.Perform;
        citizen.TargetStructureId = farm.Id;
        InvokePrivate(engine, "CompleteFarmWork", citizen);

        var crop = GetPrivateField<SortedDictionary<long, FarmCrop>>(engine, "_farms")[farm.Id.Value];
        var grainCargo = GetPrivateField<LivingWorldState>(engine, "_living").Orders
            .Where(order => order.Kind == LivingWorkKind.Harvest && order.CitizenId == citizen.Id.Value && order.CargoInTransit)
            .Sum(order => order.Cargo.Where(item => item.Good == LivingGood.Grain).Sum(item => item.Quantity));
        Assert.Equal(WorldSeason.Autumn, minute.ToCalendar().Season);
        Assert.Equal(crop.Harvested, citizen.CarriedResourceQuantity + grainCargo);

        citizen.Location = Assert.IsType<TileCoordinate>(InvokePrivate(engine, "SiteLocation", settlementId));
        InvokePrivate(engine, "Deposit", citizen);
        var foodAfter = checked(((SettlementState)InvokePrivate(engine, "SettlementFor", settlementId)!).FoodStored +
            ((Goods)InvokePrivate(engine, "OwnedStoredGoodsAt", settlementId)!).Food);
        var grainAfter = (int)InvokePrivate(engine, "GoodAt", settlementId, LivingGood.Grain)!;
        var foodDelivered = checked((int)(foodAfter - foodBefore));
        var grainDelivered = grainAfter - grainBefore;
        Assert.Equal(crop.Harvested, foodDelivered + grainDelivered);
        return (crop.Harvested, foodDelivered, grainDelivered);
    }

    private static void MoveOneLivingHouseholdToSite(SimulationEngine engine, long settlementId,
        TileCoordinate site)
    {
        var migration = GetPrivateField<MigrationWorldState>(engine, "_migrationState");
        var households = GetPrivateField<Dictionary<long, Household>>(engine, "_households");
        var citizens = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens");
        var householdId = migration.HouseholdResidences.Where(x => x.SettlementId == 1)
            .OrderBy(x => x.EntityId).Select(x => x.EntityId)
            .First(id => citizens.Values.Any(x => x.IsAlive && x.HouseholdId?.Value == id));
        var householdIds = new HashSet<long> { householdId };
        var citizenIds = citizens.Values.Where(x => x.IsAlive && x.HouseholdId?.Value == householdId)
            .Select(x => x.Id.Value).ToHashSet();
        SetPrivateField(engine, "_migrationState", new MigrationWorldState(migration.Version,
            migration.CitizenResidences.Select(x => citizenIds.Contains(x.EntityId)
                ? x with { SettlementId = settlementId } : x).OrderBy(x => x.EntityId).ToArray(),
            migration.HouseholdResidences.Select(x => householdIds.Contains(x.EntityId)
                ? x with { SettlementId = settlementId } : x).OrderBy(x => x.EntityId).ToArray(),
            migration.StructureOwners, migration.FacilityOwners, migration.WorkOrderOwners,
            migration.DaughterSettlement, migration.InTransitParties, migration.FoundingPressure,
            migration.LastRelocations, migration.LastVisitAttemptYear));

        households[householdId].DwellingStructureId = null;
        foreach (var citizen in citizens.Values.Where(x => citizenIds.Contains(x.Id.Value)))
        {
            citizen.Location = site;
            citizen.HomeStructureId = null;
        }
    }

    private static void SetCurrentMinute(SimulationEngine engine, WorldMinute minute) =>
        engine.GetType().GetProperty(nameof(SimulationEngine.CurrentMinute))!.SetValue(engine, minute);

    private static void ClearScheduledEvents(SimulationEngine engine) =>
        GetPrivateField<object>(engine, "_scheduledEvents").GetType().GetMethod("Clear")!.Invoke(
            GetPrivateField<object>(engine, "_scheduledEvents"), null);

    private static void SetPrivateField(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private static T GetPrivateField<T>(object instance, string name) =>
        (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException($"Could not read private field '{name}'."));

    private static object? InvokePrivate(object instance, string name, params object?[] arguments)
    {
        var method = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(candidate => candidate.Name == name && candidate.GetParameters().Length == arguments.Length &&
                candidate.GetParameters().Select((parameter, index) =>
                    arguments[index] is null
                        ? !parameter.ParameterType.IsValueType
                        : parameter.ParameterType.IsInstanceOfType(arguments[index])).All(x => x));
        if (method is null) throw new InvalidOperationException($"Could not invoke private method '{name}'.");
        return method.Invoke(instance, arguments);
    }

    private static long SiteIdForCitizen(SimulationEngine engine, Citizen citizen) => (long)InvokePrivate(engine, "SiteIdForCitizen", citizen)!;
    private static long SiteIdForResource(SimulationEngine engine, ResourceNode resource) => (long)InvokePrivate(engine, "SiteIdForResource", resource)!;
    private static long SiteIdForLocation(SimulationEngine engine, TileCoordinate location) => (long)InvokePrivate(engine, "SiteIdForLocation", location)!;
}
