using System.Globalization;
using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M5SocialCoreTests
{
    [Fact]
    public void FreshM5StateHasCanonicalGlobalEventsAndInvariantFingerprint()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        var snapshot = engine.CreatePersistenceSnapshot();

        Assert.Equal(SimulationEngine.SocialVersion, snapshot.SocialVersion);
        Assert.Empty(snapshot.Relationships);
        Assert.Empty(snapshot.Households);
        Assert.Collection(snapshot.ScheduledEvents.Where(x => x.Name == CitizenEventNames.FamilyCheck), item =>
        {
            Assert.Equal(CitizenEventNames.FamilyCheckPriority, item.Order.Priority);
            Assert.Equal(0, item.Order.EntitySortKey);
            Assert.Equal(WorldCalendar.MinutesPerDay, item.Order.DueWorldMinute.Value);
            Assert.Equal("{\"version\":1}", item.PayloadJson);
        });
        Assert.Collection(snapshot.ScheduledEvents.Where(x => x.Name == CitizenEventNames.LifecycleCheck), item =>
        {
            Assert.Equal(CitizenEventNames.LifecycleCheckPriority, item.Order.Priority);
            Assert.Equal(0, item.Order.EntitySortKey);
            Assert.Equal(WorldCalendar.MinutesPerDay, item.Order.DueWorldMinute.Value);
            Assert.Equal("{\"version\":1}", item.PayloadJson);
        });
        Assert.Equal(engine.SocialFingerprint, SimulationEngine.FromPersistenceSnapshot(snapshot).SocialFingerprint);
    }

    [Fact]
    public void M5FamilyAndLifecycleEventsRequireTheExactNextStrictDayBoundary()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion).CreatePersistenceSnapshot();
        var expected = new WorldMinute(WorldCalendar.MinutesPerDay);

        foreach (var name in new[] { CitizenEventNames.FamilyCheck, CitizenEventNames.LifecycleCheck })
        {
            var exact = source.ScheduledEvents.Single(item => item.Name == name);
            Assert.Equal(expected, exact.Order.DueWorldMinute);
            _ = SimulationEngine.FromPersistenceSnapshot(source);

            var invalid = new[]
            {
                WithOrder(exact, new WorldMinute(expected.Value + WorldCalendar.MinutesPerDay)),
                WithOrder(exact, new WorldMinute(expected.Value + (3L * WorldCalendar.MinutesPerDay))),
                WithOrder(exact, WorldMinute.Zero),
                WithOrder(exact, exact.Order.DueWorldMinute, exact.Order.Priority + 1),
                exact with { PayloadJson = "{\"version\":2}" }
            };

            foreach (var malformed in invalid)
            {
                var events = source.ScheduledEvents.Select(item => item.Name == name ? malformed : item).ToArray();
                Assert.Throws<ArgumentException>(() => Recreate(source, events: events));
            }
        }
    }

    [Fact]
    public void M5MortalityUsesCurrentMinuteRatherThanAnUnboundedSyntheticAge()
    {
        var traits = new CitizenTraits(0, 0, 0, 0, 0, 0); var skills = new CitizenSkills(0, 0, 0, 0, 0, 0);
        var thirtyNine = new Citizen(new CitizenId(1), 0, "A", "B", -39L * WorldCalendar.MinutesPerYear, new TileCoordinate(0, 0), traits, skills);
        var forty = new Citizen(new CitizenId(2), 1, "C", "D", -40L * WorldCalendar.MinutesPerYear, new TileCoordinate(0, 0), traits, skills);
        Assert.Equal(1, SimulationEngine.NaturalMortalityRisk(thirtyNine, WorldMinute.Zero));
        Assert.Equal(3, SimulationEngine.NaturalMortalityRisk(forty, WorldMinute.Zero));
    }

    private static ScheduledEventSnapshot WithOrder(ScheduledEventSnapshot source, WorldMinute due, int? priority = null) => source with
    {
        Order = new ScheduledEventOrder(due, priority ?? source.Order.Priority, source.Order.EntitySortKey, source.Order.Sequence)
    };

    [Fact]
    public void DescendantTargetSelectionUsesStableCitizenIdentity()
    {
        var seed = new WorldSeed(42);
        var world = new WorldGenerator().Generate(seed, WorldGenerationConfiguration.Default);
        var descendant = new Citizen(new CitizenId(101), null, "Child", "Vale", 0, world.StartingSite, new CitizenTraits(5000, 5000, 5000, 5000, 5000, 5000), new CitizenSkills(0, 0, 0, 0, 0, 0))
        {
            ActionSequence = 7
        };

        var first = CitizenGenerator.SelectTarget(seed, world, descendant, CitizenSimulationRules.ExploreRadius);
        var second = CitizenGenerator.SelectTarget(seed, world, descendant, CitizenSimulationRules.ExploreRadius);

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void M5SnapshotRejectsMissingPerCitizenActionFlow()
    {
        var snapshot = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion).CreatePersistenceSnapshot();
        var missingAction = snapshot.ScheduledEvents.Where(x => !(x.Name == CitizenEventNames.Decision && x.Order.EntitySortKey == 1)).ToArray();

        Assert.Throws<ArgumentException>(() => Recreate(snapshot, missingAction));
    }

    [Fact]
    public void M5SnapshotRejectsDuplicateAndCrossCollectionEntityIds()
    {
        var source = CreateM5SettlementSnapshot();
        var structure = Assert.Single(source.Structures);
        var duplicateHousehold = new Household(new HouseholdId(source.Counters.NextEntityId), source.WorldMinute.Value) { DissolvedMinute = source.WorldMinute.Value };
        var duplicateHouseholds = new[]
        {
            duplicateHousehold,
            new Household(duplicateHousehold.Id, source.WorldMinute.Value) { DissolvedMinute = source.WorldMinute.Value }
        };
        Assert.Throws<ArgumentException>(() => Recreate(source, households: duplicateHouseholds, counters: source.Counters with { NextEntityId = source.Counters.NextEntityId + 1 }));

        var duplicateCitizens = source.Citizens.Concat([source.Citizens[0]]).ToArray();
        Assert.Throws<ArgumentException>(() => Recreate(source, citizens: duplicateCitizens));

        var duplicateStructure = CopyStructure(structure, structure.Id);
        Assert.Throws<ArgumentException>(() => Recreate(source, structures: [structure, duplicateStructure]));

        var collidingStructure = CopyStructure(structure, new StructureId(source.Citizens[0].Id.Value));
        Assert.Throws<ArgumentException>(() => Recreate(source, structures: [collidingStructure]));
    }

    [Fact]
    public void M5SnapshotRejectsFutureAndInconsistentCitizenTemporalState()
    {
        var founderSnapshot = CreateM5Snapshot();
        Assert.All(founderSnapshot.Citizens.Where(citizen => citizen.FounderOrdinal is not null), citizen => Assert.True(citizen.BirthMinute < 0));
        _ = SimulationEngine.FromPersistenceSnapshot(founderSnapshot);

        var needsSnapshot = CreateM5Snapshot();
        var needsTarget = needsSnapshot.Citizens[0];
        var futureNeeds = needsSnapshot.Citizens.Select(citizen => citizen.Id == needsTarget.Id
            ? ReplaceCitizen(citizen, needsUpdatedMinute: needsSnapshot.WorldMinute.Value + 1)
            : ReplaceCitizen(citizen)).ToArray();
        Assert.Throws<ArgumentException>(() => Recreate(needsSnapshot, citizens: futureNeeds));

        var healthSnapshot = CreateM5Snapshot();
        var healthTarget = healthSnapshot.Citizens[0];
        var futureHealth = healthSnapshot.Citizens.Select(citizen => citizen.Id == healthTarget.Id
            ? ReplaceCitizen(citizen, healthUpdatedMinute: healthSnapshot.WorldMinute.Value + 1)
            : ReplaceCitizen(citizen)).ToArray();
        var healthBoundary = new WorldMinute(healthSnapshot.WorldMinute.Value + 1);
        var futureHealthEvents = healthSnapshot.ScheduledEvents.Select(item => item.Name == CitizenEventNames.SurvivalCheck && item.Order.EntitySortKey == healthTarget.Id.Value
            ? item with { Order = new ScheduledEventOrder(healthBoundary.Add(CitizenSimulationRules.SurvivalCheckIntervalMinutes), item.Order.Priority, item.Order.EntitySortKey, item.Order.Sequence) }
            : item).ToArray();
        Assert.Throws<ArgumentException>(() => Recreate(healthSnapshot, events: futureHealthEvents, citizens: futureHealth));

        var futureBirthSnapshot = CreateM5SnapshotWithChild();
        var futureBirthChild = futureBirthSnapshot.Citizens.Single(citizen => citizen.ParentAId is not null);
        var futureChild = ReplaceCitizen(futureBirthChild, birthMinute: futureBirthSnapshot.WorldMinute.Value + 1);
        var futureBirthCitizens = futureBirthSnapshot.Citizens.Select(citizen => citizen.Id == futureBirthChild.Id ? futureChild : ReplaceCitizen(citizen)).ToArray();
        Assert.Throws<ArgumentException>(() => Recreate(futureBirthSnapshot, citizens: futureBirthCitizens));

        var futureDeathSnapshot = CreateM5SnapshotWithChild();
        var futureDeathChild = futureDeathSnapshot.Citizens.Single(citizen => citizen.ParentAId is not null);
        var futureDeath = MakeDead(ReplaceCitizen(futureDeathChild));
        futureDeath.DeathMinute = futureDeathSnapshot.WorldMinute.Value + 1;
        var futureDeathCitizens = futureDeathSnapshot.Citizens.Select(citizen => citizen.Id == futureDeathChild.Id ? futureDeath : ReplaceCitizen(citizen)).ToArray();
        Assert.Throws<ArgumentException>(() => Recreate(futureDeathSnapshot,
            events: futureDeathSnapshot.ScheduledEvents.Where(item => item.Order.EntitySortKey != futureDeathChild.Id.Value).ToArray(),
            citizens: futureDeathCitizens));

        var negativeDeathSnapshot = CreateM5Snapshot();
        var negativeDeathFounder = MakeDead(ReplaceCitizen(negativeDeathSnapshot.Citizens[0]));
        negativeDeathFounder.DeathMinute = -1;
        var negativeDeathCitizens = negativeDeathSnapshot.Citizens.Select(citizen => citizen.Id == negativeDeathFounder.Id ? negativeDeathFounder : ReplaceCitizen(citizen)).ToArray();
        Assert.Throws<ArgumentException>(() => Recreate(negativeDeathSnapshot,
            events: negativeDeathSnapshot.ScheduledEvents.Where(item => item.Order.EntitySortKey != negativeDeathFounder.Id.Value).ToArray(),
            citizens: negativeDeathCitizens));

        var futureActionStartSnapshot = CreateM5Snapshot();
        var futureActionTarget = futureActionStartSnapshot.Citizens[0];
        var futureActionCitizen = ReplaceCitizen(futureActionTarget, currentAction: CitizenAction.Idle, actionPhase: CitizenActionPhase.Perform,
            actionStartedMinute: futureActionStartSnapshot.WorldMinute.Add(1), actionCompletesMinute: futureActionStartSnapshot.WorldMinute.Add(2));
        var futureActionCitizens = futureActionStartSnapshot.Citizens.Select(citizen => citizen.Id == futureActionTarget.Id ? futureActionCitizen : ReplaceCitizen(citizen)).ToArray();
        var futureActionEvents = futureActionStartSnapshot.ScheduledEvents.Select(item => item.Order.EntitySortKey == futureActionTarget.Id.Value && item.Name == CitizenEventNames.Decision
            ? item with { Name = CitizenEventNames.ActionComplete, Order = new ScheduledEventOrder(futureActionStartSnapshot.WorldMinute.Add(2), CitizenEventNames.CompletionPriority, item.Order.EntitySortKey, item.Order.Sequence) }
            : item).ToArray();
        Assert.Throws<ArgumentException>(() => Recreate(futureActionStartSnapshot, events: futureActionEvents, citizens: futureActionCitizens));

        var pastActionSnapshot = CreateM5Snapshot(new WorldMinute(10));
        var pastActionTarget = pastActionSnapshot.Citizens[0];
        var pastActionCitizen = ReplaceCitizen(pastActionTarget, currentAction: CitizenAction.Idle, actionPhase: CitizenActionPhase.Perform,
            actionStartedMinute: WorldMinute.Zero, actionCompletesMinute: WorldMinute.Zero);
        var pastActionCitizens = pastActionSnapshot.Citizens.Select(citizen => citizen.Id == pastActionTarget.Id ? pastActionCitizen : ReplaceCitizen(citizen)).ToArray();
        var pastActionEvents = pastActionSnapshot.ScheduledEvents.Select(item => item.Order.EntitySortKey == pastActionTarget.Id.Value && item.Name == CitizenEventNames.Decision
            ? item with { Name = CitizenEventNames.ActionComplete, Order = new ScheduledEventOrder(WorldMinute.Zero, CitizenEventNames.CompletionPriority, item.Order.EntitySortKey, item.Order.Sequence) }
            : item).ToArray();
        Assert.Throws<ArgumentException>(() => Recreate(pastActionSnapshot, events: pastActionEvents, citizens: pastActionCitizens));

        var deathBeforeBirthSnapshot = CreateM5SnapshotWithChild(new WorldMinute(10));
        var youngChild = deathBeforeBirthSnapshot.Citizens.Single(citizen => citizen.ParentAId is not null);
        var deathBeforeBirth = MakeDead(ReplaceCitizen(youngChild));
        deathBeforeBirth.DeathMinute = youngChild.BirthMinute - 1;
        var deathBeforeBirthCitizens = deathBeforeBirthSnapshot.Citizens.Select(citizen => citizen.Id == youngChild.Id ? deathBeforeBirth : ReplaceCitizen(citizen)).ToArray();
        Assert.Throws<ArgumentException>(() => Recreate(deathBeforeBirthSnapshot,
            events: deathBeforeBirthSnapshot.ScheduledEvents.Where(item => item.Order.EntitySortKey != youngChild.Id.Value).ToArray(),
            citizens: deathBeforeBirthCitizens));
    }

    [Fact]
    public void M5SnapshotRejectsEverySharedM4SettlementCorruption()
    {
        var source = CreateM5SettlementSnapshot();
        var structure = Assert.Single(source.Structures);
        var invalidStructure = new Structure(structure.Id, StructureType.Shelter, structure.Location, structure.ConstructionStartedMinute, 41, structure.RequiredStone, structure.RequiredWork)
        {
            DeliveredWood = structure.DeliveredWood,
            DeliveredStone = structure.DeliveredStone,
            CompletedWork = structure.CompletedWork
        };
        Assert.Throws<ArgumentException>(() => Recreate(source, structures: [invalidStructure]));

        var contribution = Assert.Single(source.StructureContributions);
        Assert.Throws<ArgumentException>(() => Recreate(source, contributions: [new StructureContribution(contribution.StructureId, contribution.CitizenId, contribution.ConstructionWork, contribution.WoodDelivered + 1, contribution.StoneDelivered)]));

        var settlement = source.Settlement!;
        Assert.Throws<ArgumentException>(() => Recreate(source, settlement: new SettlementState(9_999, settlement.WoodStored, settlement.StoneStored, settlement.BaseStorageCapacity, settlement.DemandUpdatedMinute, settlement.ExposureConsequencesStartMinute)));

        Assert.Throws<ArgumentException>(() => Recreate(source, events: source.ScheduledEvents.Select(item => item.Name == CitizenEventNames.SettlementEvaluateDemand ? item with { PayloadJson = "{}" } : item).ToArray()));

        var citizens = source.Citizens.ToArray();
        citizens[0].CurrentAction = CitizenAction.HaulConstruction;
        citizens[0].ActionPhase = CitizenActionPhase.TransportToConstruction;
        citizens[0].TargetStructureId = structure.Id;
        citizens[0].CarriedResourceType = ResourceType.Wood;
        citizens[0].CarriedResourceQuantity = structure.RequiredWood;
        Assert.Throws<ArgumentException>(() => Recreate(source, citizens: citizens));
    }

    [Fact]
    public void CitizenEqualityIncludesM5FamilyAndHouseholdState()
    {
        var first = NewCitizen();
        var equal = NewCitizen();
        Assert.Equal(first, equal);
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());

        foreach (var mutate in new Action<Citizen>[]
        {
            citizen => citizen.ParentAId = new CitizenId(11),
            citizen => citizen.ParentBId = new CitizenId(12),
            citizen => citizen.PartnerId = new CitizenId(13),
            citizen => citizen.HouseholdId = new HouseholdId(14),
            citizen => citizen.TargetCitizenId = new CitizenId(15)
        })
        {
            var changed = NewCitizen();
            mutate(changed);
            Assert.NotEqual(first, changed);
        }
    }

    [Fact]
    public void PopulatedRelationshipAndHouseholdSnapshotRoundTrips()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion).CreatePersistenceSnapshot();
        var citizens = source.Citizens.ToArray();
        var first = citizens[0];
        var second = citizens[1];
        var household = new Household(new HouseholdId(source.Counters.NextEntityId), source.WorldMinute.Value);
        first.PartnerId = second.Id;
        second.PartnerId = first.Id;
        first.HouseholdId = household.Id;
        second.HouseholdId = household.Id;
        var populated = new SimulationPersistenceSnapshot(source.Seed, source.WorldMinute, source.WorldSchemaVersion, source.SimulationRulesVersion, source.ApplicationVersion, source.WorldConfiguration, source.Counters with { NextEntityId = source.Counters.NextEntityId + 1 }, source.ScheduledEvents, source.World, citizens, source.CitizenGenerationVersion, source.ResourceStates, source.Settlement, source.SurvivalVersion, source.SettlementVersion, source.Structures, source.StructureContributions, source.SocialVersion, [new RelationshipState(first.Id, second.Id, 6_000, 4_500, 3_500, 0, source.WorldMinute.Value, 1)], [household]);

        var reloaded = SimulationEngine.FromPersistenceSnapshot(populated).CreatePersistenceSnapshot();

        Assert.Equal(populated.Relationships, reloaded.Relationships);
        Assert.Equal(populated.Households.Select(x => x.Id), reloaded.Households.Select(x => x.Id));
        Assert.Equal(populated.Citizens, reloaded.Citizens);
        Assert.Equal(populated.Counters, reloaded.Counters);
    }

    [Fact]
    public void M4SnapshotRejectsM5SocialAction()
    {
        var snapshot = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M4SimulationRulesVersion).CreatePersistenceSnapshot();
        var citizens = snapshot.Citizens.ToArray();
        citizens[0].CurrentAction = CitizenAction.Socialize;
        citizens[0].ActionPhase = CitizenActionPhase.Perform;
        citizens[0].TargetCitizenId = citizens[1].Id;
        citizens[0].ActionStartedMinute = snapshot.WorldMinute;
        citizens[0].ActionCompletesMinute = snapshot.WorldMinute.Add(CitizenSimulationRules.SocializeDurationMinutes);

        Assert.Throws<ArgumentException>(() => new SimulationPersistenceSnapshot(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters, snapshot.ScheduledEvents, snapshot.World, citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates, snapshot.Settlement, snapshot.SurvivalVersion, snapshot.SettlementVersion, snapshot.Structures, snapshot.StructureContributions, snapshot.SocialVersion, snapshot.Relationships, snapshot.Households));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DirectM4SnapshotRequiresExactUniqueFounderOrdinals(bool removeOrdinal)
    {
        var snapshot = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M4SimulationRulesVersion).CreatePersistenceSnapshot();
        var citizens = snapshot.Citizens.ToArray();
        var original = citizens[0];
        citizens[0] = new Citizen(original.Id, removeOrdinal ? null : 1, original.GivenName, original.FamilyName, original.BirthMinute, original.Location, original.Traits, original.Skills, original.Needs)
        {
            Health = original.Health,
            NeedsUpdatedMinute = original.NeedsUpdatedMinute,
            HealthUpdatedMinute = original.HealthUpdatedMinute
        };

        Assert.Throws<ArgumentException>(() => Recreate(snapshot, citizens: citizens));
    }

    [Fact]
    public void M5FourteenDayCheckpointRoundTripsWithCanonicalGameplayEvents()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        source.AdvanceUntil(new WorldMinute(14 * WorldCalendar.MinutesPerDay));
        var snapshot = source.CreatePersistenceSnapshot();
        var reloaded = SimulationEngine.FromPersistenceSnapshot(snapshot);

        Assert.Equal(source.SocialFingerprint, reloaded.SocialFingerprint);
        Assert.Equal(snapshot.ScheduledEvents, reloaded.CreatePersistenceSnapshot().ScheduledEvents);
        Assert.Equal(source.LivingPopulation, reloaded.LivingPopulation);
    }

    [Fact]
    public void BirthRequiresAFreeSlotInTheHouseholdDwelling()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion).CreatePersistenceSnapshot();
        var parents = source.Citizens.Take(2).ToArray();
        var shelterId = new StructureId(source.Counters.NextEntityId);
        var household = new Household(new HouseholdId(source.Counters.NextEntityId + 1), source.WorldMinute.Value) { DwellingStructureId = shelterId };
        var site = source.World!.Tiles.First(tile => tile.Buildable && tile.Coordinate != source.World.StartingSite && source.World.GetResources(tile.Coordinate).Count == 0).Coordinate;
        var shelter = new Structure(shelterId, StructureType.Shelter, site, source.WorldMinute.Value, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork)
        {
            Status = StructureStatus.Complete,
            CompletedMinute = source.WorldMinute.Value,
            DeliveredWood = CitizenSimulationRules.ShelterRequiredWood,
            DeliveredStone = CitizenSimulationRules.ShelterRequiredStone,
            CompletedWork = CitizenSimulationRules.ShelterRequiredWork
        };
        parents[0].PartnerId = parents[1].Id;
        parents[1].PartnerId = parents[0].Id;
        parents[0].HouseholdId = household.Id;
        parents[1].HouseholdId = household.Id;
        foreach (var resident in source.Citizens.Take(4)) resident.HomeStructureId = shelterId;
        var prepared = new SimulationPersistenceSnapshot(source.Seed, source.WorldMinute, source.WorldSchemaVersion, source.SimulationRulesVersion, source.ApplicationVersion, source.WorldConfiguration,
            source.Counters with { NextEntityId = source.Counters.NextEntityId + 2 }, source.ScheduledEvents, source.World, source.Citizens, source.CitizenGenerationVersion,
            source.ResourceStates, source.Settlement, source.SurvivalVersion, source.SettlementVersion, new[] { shelter }, new[] { new StructureContribution(shelter.Id, parents[0].Id, shelter.CompletedWork, shelter.DeliveredWood, shelter.DeliveredStone) }, source.SocialVersion,
            new[] { new RelationshipState(parents[0].Id, parents[1].Id, 10_000, 10_000, 10_000, 0, source.WorldMinute.Value, 1) }, new[] { household });
        var engine = SimulationEngine.FromPersistenceSnapshot(prepared);
        var tryBirth = typeof(SimulationEngine).GetMethod("TryBirth", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(tryBirth);
        tryBirth!.Invoke(engine, new object?[] { Assert.Single(engine.Households), null });

        Assert.Equal(CitizenGenerator.FounderCount, engine.TotalCitizenCount);
    }

    [Fact]
    public void StrictM5SpareShelterDemandDoesNotTriggerAtTheEqualityBoundary()
    {
        var m5 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        AddCompletedShelters(m5, 5);
        AddPackedEligibleHouseholds(m5);

        Assert.Equal(20, m5.ShelterCapacity);
        Assert.Equal(4, m5.ShelterCapacity - m5.LivingPopulation);
        Assert.NotEqual(StructureType.Shelter, SelectSettlementDemand(m5));

        var m4 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M4SimulationRulesVersion);
        AddCompletedShelters(m4, 5);

        Assert.NotEqual(StructureType.Shelter, SelectSettlementDemand(m4));
    }

    [Fact]
    public void M5FamilyBufferShelterDemandRequiresThreeSpareSlotsAndAllNonHousingPrerequisites()
    {
        var threeSpare = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        AddCompletedShelters(threeSpare, 5);
        AddFamilyBufferHousehold(threeSpare, deceasedCount: 3);
        Assert.Equal(3, threeSpare.ShelterCapacity - threeSpare.LivingPopulation);
        Assert.Equal(StructureType.Shelter, SelectSettlementDemand(threeSpare));

        var fourSpare = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        AddCompletedShelters(fourSpare, 5);
        AddFamilyBufferHousehold(fourSpare, deceasedCount: 4);
        Assert.Equal(4, fourSpare.ShelterCapacity - fourSpare.LivingPopulation);
        Assert.NotEqual(StructureType.Shelter, SelectSettlementDemand(fourSpare));

        var failedPrerequisite = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        AddCompletedShelters(failedPrerequisite, 5);
        AddFamilyBufferHousehold(failedPrerequisite, deceasedCount: 3);
        failedPrerequisite.Settlement.FoodStored = 0;
        Assert.NotEqual(StructureType.Shelter, SelectSettlementDemand(failedPrerequisite));

        var unchangedM4 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M4SimulationRulesVersion);
        AddCompletedShelters(unchangedM4, 5);
        AddFamilyBufferHousehold(unchangedM4, deceasedCount: 3);
        Assert.NotEqual(StructureType.Shelter, SelectSettlementDemand(unchangedM4));
    }

    [Fact]
    public void M5HousingExchangeRelocatesTheMinimalWholeGroupAndCreatesAnActualBirthSlot()
    {
        var setup = CreateFragmentedHousingExchangeEngine(restDonors: false);
        RunHousingExchange(setup.Engine);

        Assert.Equal(setup.Destination.Id, setup.MinimalDonor.HomeStructureId);
        Assert.Equal(setup.Source.Id, setup.OtherDonor.HomeStructureId);
        Assert.Equal(3, Citizens(setup.Engine).Values.Count(citizen => citizen.IsAlive && citizen.HomeStructureId == setup.Source.Id));
        Assert.True(IsReproductionReady(setup.Engine, setup.CandidateHousehold, requireDwellingCapacity: true));
    }

    [Fact]
    public void M5HousingExchangeIsIdempotentAndLeavesActionsAndEventsCoherent()
    {
        var setup = CreateFragmentedHousingExchangeEngine(restDonors: false);
        var events = setup.Engine.PendingEventCount;
        setup.MinimalDonor.CurrentAction = CitizenAction.Idle;
        setup.MinimalDonor.ActionPhase = CitizenActionPhase.Perform;
        RunHousingExchange(setup.Engine);
        var firstHomes = Citizens(setup.Engine).Values.Where(citizen => citizen.IsAlive).OrderBy(citizen => citizen.Id.Value).Select(citizen => (citizen.Id, citizen.HomeStructureId)).ToArray();
        RunHousingExchange(setup.Engine);

        Assert.Equal(firstHomes, Citizens(setup.Engine).Values.Where(citizen => citizen.IsAlive).OrderBy(citizen => citizen.Id.Value).Select(citizen => (citizen.Id, citizen.HomeStructureId)).ToArray());
        Assert.Equal(CitizenAction.Idle, setup.MinimalDonor.CurrentAction);
        Assert.Equal(CitizenActionPhase.Perform, setup.MinimalDonor.ActionPhase);
        Assert.Equal(events, setup.Engine.PendingEventCount);
    }

    [Fact]
    public void M5HousingExchangeDoesNothingWhenOnlyRestingDonorsAreFeasible()
    {
        var setup = CreateFragmentedHousingExchangeEngine(restDonors: true);
        RunHousingExchange(setup.Engine);

        Assert.Equal(setup.Source.Id, setup.MinimalDonor.HomeStructureId);
        Assert.Equal(setup.Source.Id, setup.OtherDonor.HomeStructureId);
        Assert.False(IsReproductionReady(setup.Engine, setup.CandidateHousehold, requireDwellingCapacity: true));
    }

    [Theory]
    [InlineData(0, 4000)]
    [InlineData(5000, 3500)]
    [InlineData(40000, 0)]
    [InlineData(90000, 0)]
    public void SocialRecentInteractionPenaltyHasExactBoundedDecay(long elapsed, int expected)
    {
        var relationship = new RelationshipState(new CitizenId(1), new CitizenId(2), 0, 0, 0, 0, 0, 1);

        Assert.Equal(expected, SimulationEngine.SocialRecentInteractionPenalty(relationship, new WorldMinute(elapsed)));
    }

    [Fact]
    public void SocialRecentInteractionPenaltyIsZeroForNullRelationship() =>
        Assert.Equal(0, SimulationEngine.SocialRecentInteractionPenalty(null, WorldMinute.Zero));

    [Fact]
    public void SocialRecentInteractionPenaltyIsRelationshipCategoryNeutral()
    {
        var ordinary = new RelationshipState(new CitizenId(1), new CitizenId(2), 500, 0, 0, 0, 0, 1);
        var family = new RelationshipState(new CitizenId(1), new CitizenId(2), 8_000, 8_000, 7_000, 0, 0, 1);
        var partner = new RelationshipState(new CitizenId(1), new CitizenId(2), 10_000, 10_000, 10_000, 0, 0, 1);
        var rival = new RelationshipState(new CitizenId(1), new CitizenId(2), 2_500, -1, 0, 2_000, 0, 1);

        Assert.All(new[] { ordinary, family, partner, rival }, relationship => Assert.Equal(3_500, SimulationEngine.SocialRecentInteractionPenalty(relationship, new WorldMinute(5_000))));
    }

    [Fact]
    public void RecentStrongFamilyTargetLosesToAnUnfamiliarTargetThenRegainsAfterDecay()
    {
        Assert.Equal(3, SelectTargetWithFamilyRelationship(currentMinute: 0).Value);
        Assert.Equal(2, SelectTargetWithFamilyRelationship(currentMinute: 40_000).Value);
    }

    [Fact]
    public void SocialTargetRankingRetainsScoreDistanceAndIdentityOrder()
    {
        Assert.Equal(3, SimulationEngine.SelectSocialTargetCandidate([
            new SocialTargetCandidate(new CitizenId(1), 10, 1),
            new SocialTargetCandidate(new CitizenId(2), 11, 2),
            new SocialTargetCandidate(new CitizenId(3), 12, 3)]).Value);
        Assert.Equal(2, SimulationEngine.SelectSocialTargetCandidate([
            new SocialTargetCandidate(new CitizenId(1), 10, 2),
            new SocialTargetCandidate(new CitizenId(2), 10, 1)]).Value);
        Assert.Equal(1, SimulationEngine.SelectSocialTargetCandidate([
            new SocialTargetCandidate(new CitizenId(2), 10, 1),
            new SocialTargetCandidate(new CitizenId(1), 10, 1)]).Value);
    }

    [Fact]
    public void PartnershipEligibilityAndScoringRemainIndependentOfSocialRecency()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        var citizens = Citizens(engine);
        var first = citizens[1];
        var second = citizens[2];
        var relationship = new RelationshipState(first.Id, second.Id, 7_000, 6_000, 5_000, 0, 0, 1);
        var form = typeof(SimulationEngine).GetMethod("TryFormPartnership", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(form);
        form!.Invoke(engine, new object[] { first, second, relationship });

        Assert.Equal(second.Id, first.PartnerId);
        Assert.Equal(first.Id, second.PartnerId);
        Assert.Single(engine.Households);
    }

    [Fact]
    public void SocialFingerprintIsInvariantUnderCustomNegativeSignCultureAndIncludesRelationshipAndHouseholdState()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion).CreatePersistenceSnapshot();
        var first = source.Citizens[0];
        var second = source.Citizens[1];
        var household = new Household(new HouseholdId(source.Counters.NextEntityId), source.WorldMinute.Value);
        first.PartnerId = second.Id;
        second.PartnerId = first.Id;
        first.HouseholdId = household.Id;
        second.HouseholdId = household.Id;
        var social = new SimulationPersistenceSnapshot(source.Seed, source.WorldMinute, source.WorldSchemaVersion, source.SimulationRulesVersion, source.ApplicationVersion, source.WorldConfiguration,
            source.Counters with { NextEntityId = source.Counters.NextEntityId + 1 }, source.ScheduledEvents, source.World, source.Citizens, source.CitizenGenerationVersion,
            source.ResourceStates, source.Settlement, source.SurvivalVersion, source.SettlementVersion, source.Structures, source.StructureContributions, source.SocialVersion,
            new[] { new RelationshipState(first.Id, second.Id, 6_000, -500, 5_000, 0, source.WorldMinute.Value, 2) }, new[] { household });
        var invariant = SimulationEngine.FromPersistenceSnapshot(social).SocialFingerprint;
        var priorCulture = CultureInfo.CurrentCulture;
        var priorUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var customized = (CultureInfo)priorCulture.Clone();
            customized.NumberFormat.NegativeSign = "~";
            CultureInfo.CurrentCulture = customized;
            CultureInfo.CurrentUICulture = customized;
            Assert.Equal(invariant, SimulationEngine.FromPersistenceSnapshot(social).SocialFingerprint);
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }

    [Theory]
    [InlineData(0, 0, 0, 0, false, false, RelationshipLabels.Stranger)]
    [InlineData(500, 0, 0, 0, false, false, RelationshipLabels.Acquaintance)]
    [InlineData(2500, 2000, 1500, 1999, false, false, RelationshipLabels.Friend)]
    [InlineData(6000, 5000, 4500, 1499, false, false, RelationshipLabels.CloseFriend)]
    [InlineData(2500, 0, 0, 2000, false, false, RelationshipLabels.Rival)]
    [InlineData(2500, 0, 0, 2000, false, true, RelationshipLabels.Family)]
    [InlineData(2500, 0, 0, 2000, true, true, RelationshipLabels.Partner)]
    public void RelationshipLabelsHaveExplicitCanonicalPrecedence(int familiarity, int affinity, int trust, int conflict, bool partner, bool family, string expected)
    {
        var relationship = new RelationshipState(new CitizenId(1), new CitizenId(2), familiarity, affinity, trust, conflict, 0, 1);
        Assert.Equal(expected, RelationshipLabels.Derive(relationship, partner, family));
    }

    private static SimulationPersistenceSnapshot Recreate(SimulationPersistenceSnapshot snapshot, IReadOnlyList<ScheduledEventSnapshot>? events = null, IReadOnlyList<Citizen>? citizens = null, SettlementState? settlement = null, IReadOnlyList<Structure>? structures = null, IReadOnlyList<StructureContribution>? contributions = null, IReadOnlyList<RelationshipState>? relationships = null, IReadOnlyList<Household>? households = null, DeterministicCountersSnapshot? counters = null) =>
        new(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration, counters ?? snapshot.Counters, events ?? snapshot.ScheduledEvents, snapshot.World, citizens ?? snapshot.Citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates, settlement ?? snapshot.Settlement, snapshot.SurvivalVersion, snapshot.SettlementVersion, structures ?? snapshot.Structures, contributions ?? snapshot.StructureContributions, snapshot.SocialVersion, relationships ?? snapshot.Relationships, households ?? snapshot.Households);

    private static SimulationPersistenceSnapshot CreateM5SettlementSnapshot()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion).CreatePersistenceSnapshot();
        var site = source.World!.Tiles.First(tile => tile.Buildable && tile.Coordinate != source.World.StartingSite && source.World.GetResources(tile.Coordinate).Count == 0).Coordinate;
        var structure = new Structure(new StructureId(source.Counters.NextEntityId), StructureType.Shelter, site, source.WorldMinute.Value, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork)
        {
            DeliveredWood = 1
        };
        var contribution = new StructureContribution(structure.Id, source.Citizens[0].Id, woodDelivered: 1);
        return new SimulationPersistenceSnapshot(source.Seed, source.WorldMinute, source.WorldSchemaVersion, source.SimulationRulesVersion, source.ApplicationVersion, source.WorldConfiguration, source.Counters with { NextEntityId = source.Counters.NextEntityId + 1 }, source.ScheduledEvents, source.World, source.Citizens, source.CitizenGenerationVersion, source.ResourceStates, source.Settlement, source.SurvivalVersion, source.SettlementVersion, [structure], [contribution], source.SocialVersion, source.Relationships, source.Households);
    }

    private static SimulationPersistenceSnapshot CreateM5Snapshot(WorldMinute initialMinute = default) =>
        new SimulationEngine(new WorldSeed(42), initialMinute, simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion).CreatePersistenceSnapshot();

    private static SimulationPersistenceSnapshot CreateM5SnapshotWithChild(WorldMinute initialMinute = default)
    {
        var source = CreateM5Snapshot(initialMinute);
        var citizens = source.Citizens.ToArray();
        var first = citizens[1];
        var second = citizens[2];
        var structureId = new StructureId(source.Counters.NextEntityId);
        var householdId = new HouseholdId(source.Counters.NextEntityId + 1);
        var childId = new CitizenId(source.Counters.NextEntityId + 2);
        var site = source.World!.Tiles.First(tile => tile.Buildable && tile.Coordinate != source.World.StartingSite && source.World.GetResources(tile.Coordinate).Count == 0).Coordinate;
        var shelter = new Structure(structureId, StructureType.Shelter, site, source.WorldMinute.Value, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork)
        {
            Status = StructureStatus.Complete,
            CompletedMinute = source.WorldMinute.Value,
            DeliveredWood = CitizenSimulationRules.ShelterRequiredWood,
            DeliveredStone = CitizenSimulationRules.ShelterRequiredStone,
            CompletedWork = CitizenSimulationRules.ShelterRequiredWork
        };
        var household = new Household(householdId, source.WorldMinute.Value) { DwellingStructureId = structureId };
        first.PartnerId = second.Id;
        second.PartnerId = first.Id;
        first.HouseholdId = householdId;
        second.HouseholdId = householdId;
        first.HomeStructureId = structureId;
        second.HomeStructureId = structureId;
        var child = new Citizen(childId, null, "Child", first.FamilyName, source.WorldMinute.Value, first.Location, first.Traits, new CitizenSkills(0, 0, 0, 0, 0, 0))
        {
            ParentAId = first.Id,
            ParentBId = second.Id,
            HouseholdId = householdId,
            HomeStructureId = structureId,
            NeedsUpdatedMinute = source.WorldMinute.Value,
            HealthUpdatedMinute = source.WorldMinute.Value
        };
        var nextSequence = source.Counters.NextScheduledEventSequence;
        var childEvents = new[]
        {
            new ScheduledEventSnapshot(new ScheduledEventId(nextSequence), new ScheduledEventOrder(source.WorldMinute, CitizenEventNames.DecisionPriority, childId.Value, nextSequence), CitizenEventNames.Decision, $"{{\"citizenId\":\"{childId.Value}\",\"actionSequence\":0}}"),
            new ScheduledEventSnapshot(new ScheduledEventId(nextSequence + 1), new ScheduledEventOrder(source.WorldMinute.Add(CitizenSimulationRules.SurvivalCheckIntervalMinutes), CitizenEventNames.SurvivalPriority, childId.Value, nextSequence + 1), CitizenEventNames.SurvivalCheck, $"{{\"citizenId\":\"{childId.Value}\"}}")
        };
        return new SimulationPersistenceSnapshot(source.Seed, source.WorldMinute, source.WorldSchemaVersion, source.SimulationRulesVersion, source.ApplicationVersion, source.WorldConfiguration,
            source.Counters with { NextEntityId = childId.Value + 1, NextScheduledEventSequence = nextSequence + 2 }, source.ScheduledEvents.Concat(childEvents).ToArray(), source.World, citizens.Append(child).ToArray(), source.CitizenGenerationVersion,
            source.ResourceStates, source.Settlement, source.SurvivalVersion, source.SettlementVersion, [shelter], [new StructureContribution(structureId, first.Id, shelter.CompletedWork, shelter.DeliveredWood, shelter.DeliveredStone)], source.SocialVersion,
            [new RelationshipState(first.Id, second.Id, 10_000, 10_000, 10_000, 0, source.WorldMinute.Value, 1)], [household]);
    }

    private static Citizen ReplaceCitizen(Citizen source, long? birthMinute = null, long? needsUpdatedMinute = null, long? healthUpdatedMinute = null,
        CitizenAction? currentAction = null, CitizenActionPhase? actionPhase = null, WorldMinute? actionStartedMinute = null, WorldMinute? actionCompletesMinute = null) => new(source.Id, source.FounderOrdinal, source.GivenName, source.FamilyName, birthMinute ?? source.BirthMinute, source.Location,
        source.Traits, source.Skills, source.Needs)
    {
        Health = source.Health,
        CurrentAction = currentAction ?? source.CurrentAction,
        ActionPhase = actionPhase ?? source.ActionPhase,
        ActionSequence = source.ActionSequence,
        ActionStartedMinute = actionStartedMinute ?? source.ActionStartedMinute,
        ActionCompletesMinute = actionCompletesMinute ?? source.ActionCompletesMinute,
        ActionTarget = source.ActionTarget,
        TargetResourceNodeId = source.TargetResourceNodeId,
        TargetStructureId = source.TargetStructureId,
        TargetCitizenId = source.TargetCitizenId,
        CarriedResourceType = source.CarriedResourceType,
        CarriedResourceQuantity = source.CarriedResourceQuantity,
        NeedsUpdatedMinute = needsUpdatedMinute ?? source.NeedsUpdatedMinute,
        HealthUpdatedMinute = healthUpdatedMinute ?? source.HealthUpdatedMinute,
        LifetimeMovementSteps = source.LifetimeMovementSteps,
        LifetimeMovementCost = source.LifetimeMovementCost,
        LifetimeForagingMinutes = source.LifetimeForagingMinutes,
        LifetimeWoodcuttingMinutes = source.LifetimeWoodcuttingMinutes,
        LifetimeStoneworkingMinutes = source.LifetimeStoneworkingMinutes,
        LifetimeConstructionMinutes = source.LifetimeConstructionMinutes,
        LifetimeHaulingMinutes = source.LifetimeHaulingMinutes,
        DeathMinute = source.DeathMinute,
        DeathCause = source.DeathCause,
        ParentAId = source.ParentAId,
        ParentBId = source.ParentBId,
        PartnerId = source.PartnerId,
        HouseholdId = source.HouseholdId,
        HomeStructureId = source.HomeStructureId
    };

    private static Citizen MakeDead(Citizen source)
    {
        source.Health = 0;
        source.DeathCause = "natural";
        source.CurrentAction = CitizenAction.Dead;
        source.ActionPhase = CitizenActionPhase.None;
        source.ActionTarget = null;
        source.TargetResourceNodeId = null;
        source.TargetStructureId = null;
        source.TargetCitizenId = null;
        source.ActionStartedMinute = null;
        source.ActionCompletesMinute = null;
        source.CarriedResourceType = null;
        source.CarriedResourceQuantity = 0;
        return source;
    }

    private static Structure CopyStructure(Structure source, StructureId id) => new(id, source.Type, source.Location, source.ConstructionStartedMinute, source.RequiredWood, source.RequiredStone, source.RequiredWork)
    {
        Status = source.Status,
        CompletedMinute = source.CompletedMinute,
        DeliveredWood = source.DeliveredWood,
        DeliveredStone = source.DeliveredStone,
        CompletedWork = source.CompletedWork
    };

    private static Citizen NewCitizen() => new(new CitizenId(1), 0, "A", "B", -WorldCalendar.MinutesPerYear, new TileCoordinate(0, 0), new CitizenTraits(1, 2, 3, 4, 5, 6), new CitizenSkills(7, 8, 9, 10, 11, 12));

    private static CitizenId SelectTargetWithFamilyRelationship(long currentMinute)
    {
        var engine = new SimulationEngine(new WorldSeed(42), new WorldMinute(currentMinute), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        var citizens = Citizens(engine);
        var initiator = citizens[1];
        var family = citizens[2];
        var unfamiliar = citizens[3];
        var unrelatedParent = citizens[4];
        var remote = engine.World.Tiles.First(tile => tile.Walkable && Math.Max(Math.Abs(tile.Coordinate.X - engine.World.StartingSite.X), Math.Abs(tile.Coordinate.Y - engine.World.StartingSite.Y)) > CitizenSimulationRules.SocialRadius).Coordinate;
        foreach (var citizen in citizens.Values) citizen.Location = remote;
        initiator.Location = engine.World.StartingSite;
        family.Location = engine.World.StartingSite;
        unfamiliar.Location = engine.World.StartingSite;
        family.ParentAId = initiator.Id;
        family.ParentBId = unrelatedParent.Id;
        Relationships(engine)[(initiator.Id.Value, family.Id.Value)] = new RelationshipState(initiator.Id, family.Id, 10_000, 10_000, 10_000, 0, 0, 1);
        var select = typeof(SimulationEngine).GetMethod("SelectSocialTarget", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(select);
        return Assert.IsType<Citizen>(select!.Invoke(engine, new object[] { initiator })).Id;
    }

    private static Dictionary<long, Citizen> Citizens(SimulationEngine engine) => Assert.IsType<Dictionary<long, Citizen>>(typeof(SimulationEngine).GetField("_citizens", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));

    private static Dictionary<long, Structure> Structures(SimulationEngine engine) => Assert.IsType<Dictionary<long, Structure>>(typeof(SimulationEngine).GetField("_structures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));

    private static Dictionary<long, Household> Households(SimulationEngine engine) => Assert.IsType<Dictionary<long, Household>>(typeof(SimulationEngine).GetField("_households", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));

    private static StructureType? SelectSettlementDemand(SimulationEngine engine) => (StructureType?)typeof(SimulationEngine).GetMethod("SelectSettlementDemand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, null);

    private static void EvaluateSettlementDemand(SimulationEngine engine) => typeof(SimulationEngine).GetMethod("EvaluateSettlementDemand", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, null);

    private static void AddEligibleHousehold(SimulationEngine engine)
    {
        var parents = Citizens(engine).Values.Where(citizen => citizen.AgeYears(engine.CurrentMinute) is >= 18 and <= 45).OrderBy(citizen => citizen.Id.Value).Take(2).ToArray();
        Assert.Equal(2, parents.Length);
        var household = new Household(new HouseholdId(10_000), engine.CurrentMinute.Value);
        parents[0].PartnerId = parents[1].Id;
        parents[1].PartnerId = parents[0].Id;
        parents[0].HouseholdId = household.Id;
        parents[1].HouseholdId = household.Id;
        Households(engine).Add(household.Id.Value, household);
        var pair = RelationshipState.Normalize(parents[0].Id, parents[1].Id);
        Relationships(engine).Add((pair.A.Value, pair.B.Value), new RelationshipState(pair.A, pair.B, 10_000, 10_000, 10_000, 0, engine.CurrentMinute.Value, 1));
    }

    private static void AddFamilyBufferHousehold(SimulationEngine engine, int deceasedCount)
    {
        var people = Citizens(engine);
        foreach (var citizen in people.Values.OrderByDescending(citizen => citizen.Id.Value).Take(deceasedCount))
        {
            citizen.Health = 0;
            citizen.DeathMinute = engine.CurrentMinute.Value;
            citizen.DeathCause = "natural";
            citizen.CurrentAction = CitizenAction.Dead;
        }

        var parents = people.Values.Where(citizen => citizen.IsAlive).OrderBy(citizen => citizen.Id.Value).Take(2).ToArray();
        Assert.Equal(2, parents.Length);
        var household = new Household(new HouseholdId(10_000 + deceasedCount), engine.CurrentMinute.Value);
        parents[0].PartnerId = parents[1].Id;
        parents[1].PartnerId = parents[0].Id;
        parents[0].HouseholdId = household.Id;
        parents[1].HouseholdId = household.Id;
        Households(engine).Add(household.Id.Value, household);
        var pair = RelationshipState.Normalize(parents[0].Id, parents[1].Id);
        Relationships(engine)[(pair.A.Value, pair.B.Value)] = new RelationshipState(pair.A, pair.B, 10_000, 10_000, 10_000, 0, engine.CurrentMinute.Value, 1);
    }

    private static void AddCompletedShelters(SimulationEngine engine, int count)
    {
        var sites = engine.World.Tiles.Where(tile => tile.Buildable && tile.Coordinate != engine.World.StartingSite && engine.World.GetResources(tile.Coordinate).Count == 0).Select(tile => tile.Coordinate).Take(count).ToArray();
        Assert.Equal(count, sites.Length);
        for (var index = 0; index < count; index++)
        {
            var shelter = new Structure(new StructureId(9_000 + index), StructureType.Shelter, sites[index], engine.CurrentMinute.Value, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork)
            {
                Status = StructureStatus.Complete,
                CompletedMinute = engine.CurrentMinute.Value,
                DeliveredWood = CitizenSimulationRules.ShelterRequiredWood,
                DeliveredStone = CitizenSimulationRules.ShelterRequiredStone,
                CompletedWork = CitizenSimulationRules.ShelterRequiredWork
            };
            Structures(engine).Add(shelter.Id.Value, shelter);
        }
    }

    private static void AddPackedEligibleHouseholds(SimulationEngine engine)
    {
        foreach (var citizen in Citizens(engine).Values.OrderByDescending(citizen => citizen.Id.Value).Take(4))
        {
            citizen.Health = 0;
            citizen.DeathMinute = engine.CurrentMinute.Value;
            citizen.DeathCause = "test";
        }
        var members = Citizens(engine).Values.Where(citizen => citizen.IsAlive).OrderBy(citizen => citizen.Id.Value).ToArray();
        Assert.Equal(16, members.Length);
        for (var index = 0; index < members.Length; index += 2)
        {
            var household = new Household(new HouseholdId(11_000 + index / 2), engine.CurrentMinute.Value);
            var first = members[index];
            var second = members[index + 1];
            first.PartnerId = second.Id;
            second.PartnerId = first.Id;
            first.HouseholdId = household.Id;
            second.HouseholdId = household.Id;
            Households(engine).Add(household.Id.Value, household);
            var pair = RelationshipState.Normalize(first.Id, second.Id);
            Relationships(engine).Add((pair.A.Value, pair.B.Value), new RelationshipState(pair.A, pair.B, 10_000, 10_000, 10_000, 0, engine.CurrentMinute.Value, 1));
        }
        typeof(SimulationEngine).GetMethod("ReconcileHouseholdsAndHousing", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, null);
    }

    private sealed record ExchangeSetup(SimulationEngine Engine, Household CandidateHousehold, Citizen MinimalDonor, Citizen OtherDonor, Structure Source, Structure Destination);

    private static ExchangeSetup CreateFragmentedHousingExchangeEngine(bool restDonors)
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        foreach (var founder in Citizens(engine).Values)
        {
            founder.Health = 0;
            founder.DeathMinute = engine.CurrentMinute.Value;
            founder.DeathCause = "test";
            founder.CurrentAction = CitizenAction.Dead;
        }
        var sites = engine.World.Tiles.Where(tile => tile.Buildable && tile.Coordinate != engine.World.StartingSite && engine.World.GetResources(tile.Coordinate).Count == 0).Select(tile => tile.Coordinate).Take(2).ToArray();
        var source = CompletedShelter(new StructureId(40_001), sites[0]);
        var destination = CompletedShelter(new StructureId(40_002), sites[1]);
        Structures(engine).Add(source.Id.Value, source);
        Structures(engine).Add(destination.Id.Value, destination);
        Citizen Person(long id, int age) => new(new CitizenId(id), null, "Citizen", id.ToString(CultureInfo.InvariantCulture), -1L * age * WorldCalendar.MinutesPerYear, engine.World.StartingSite, new CitizenTraits(5_000, 5_000, 5_000, 5_000, 5_000, 5_000), new CitizenSkills(0, 0, 0, 0, 0, 0));
        var first = Person(41_001, 24);
        var second = Person(41_002, 24);
        var minimalDonor = Person(41_003, 60);
        var otherDonor = Person(41_004, 60);
        var destinationFirst = Person(41_005, 60);
        var destinationSecond = Person(41_006, 60);
        foreach (var citizen in new[] { first, second, minimalDonor, otherDonor, destinationFirst, destinationSecond }) Citizens(engine).Add(citizen.Id.Value, citizen);
        var candidate = new Household(new HouseholdId(42_001), engine.CurrentMinute.Value) { DwellingStructureId = source.Id };
        var destinationHousehold = new Household(new HouseholdId(42_002), engine.CurrentMinute.Value) { DwellingStructureId = destination.Id };
        Households(engine).Add(candidate.Id.Value, candidate);
        Households(engine).Add(destinationHousehold.Id.Value, destinationHousehold);
        first.PartnerId = second.Id;
        second.PartnerId = first.Id;
        first.HouseholdId = candidate.Id;
        second.HouseholdId = candidate.Id;
        destinationFirst.HouseholdId = destinationHousehold.Id;
        destinationSecond.HouseholdId = destinationHousehold.Id;
        first.HomeStructureId = source.Id;
        second.HomeStructureId = source.Id;
        minimalDonor.HomeStructureId = source.Id;
        otherDonor.HomeStructureId = source.Id;
        destinationFirst.HomeStructureId = destination.Id;
        destinationSecond.HomeStructureId = destination.Id;
        var pair = RelationshipState.Normalize(first.Id, second.Id);
        Relationships(engine).Add((pair.A.Value, pair.B.Value), new RelationshipState(pair.A, pair.B, 10_000, 10_000, 10_000, 0, engine.CurrentMinute.Value, 1));
        if (restDonors)
        {
            minimalDonor.CurrentAction = CitizenAction.Rest;
            otherDonor.CurrentAction = CitizenAction.Rest;
        }
        ReconcileHousing(engine);
        return new ExchangeSetup(engine, candidate, minimalDonor, otherDonor, source, destination);
    }

    private static Structure CompletedShelter(StructureId id, TileCoordinate site) => new(id, StructureType.Shelter, site, 0, CitizenSimulationRules.ShelterRequiredWood, CitizenSimulationRules.ShelterRequiredStone, CitizenSimulationRules.ShelterRequiredWork)
    {
        Status = StructureStatus.Complete,
        CompletedMinute = 0,
        DeliveredWood = CitizenSimulationRules.ShelterRequiredWood,
        DeliveredStone = CitizenSimulationRules.ShelterRequiredStone,
        CompletedWork = CitizenSimulationRules.ShelterRequiredWork
    };

    private static void RunHousingExchange(SimulationEngine engine) => typeof(SimulationEngine).GetMethod("TryRelocateOneHousingBlockedReproductiveHousehold", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, null);

    private static bool IsReproductionReady(SimulationEngine engine, Household household, bool requireDwellingCapacity)
    {
        var arguments = new object?[] { household, requireDwellingCapacity, null, null };
        return Assert.IsType<bool>(typeof(SimulationEngine).GetMethod("IsReproductionReady", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, arguments));
    }

    private static void ReconcileHousing(SimulationEngine engine) => typeof(SimulationEngine).GetMethod("ReconcileHouseholdsAndHousing", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, null);

    private static Dictionary<(long CitizenAId, long CitizenBId), RelationshipState> Relationships(SimulationEngine engine) => Assert.IsType<Dictionary<(long CitizenAId, long CitizenBId), RelationshipState>>(typeof(SimulationEngine).GetField("_relationships", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
}
