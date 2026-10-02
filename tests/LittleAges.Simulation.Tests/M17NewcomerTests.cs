using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M17NewcomerTests
{
    private static readonly Lazy<SimulationPersistenceSnapshot> Village = new(() =>
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.NewcomersRulesVersion);
        engine.AdvanceUntil(new WorldMinute(30L * WorldCalendar.MinutesPerDay));
        var location = (TileCoordinate)Invoke(engine, "SelectConstructionSite", 1L, StructureType.Shelter)!;
        Invoke(engine, "CreateStructure", StructureType.Shelter, location);
        engine.AdvanceUntil(new WorldMinute(60L * WorldCalendar.MinutesPerDay));
        return engine.CreatePersistenceSnapshot();
    });

    [Fact]
    public void GuestHasPhysicalRouteOwnSuppliesAndNoResidentRights()
    {
        var engine = new SimulationEngine(Village.Value);
        var population = engine.Population; var households = engine.Households.Count;
        Assert.True((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        var guest = Guest(engine);
        Assert.Equal(NewcomerPhase.Approaching, guest.Phase);
        Assert.Equal(guest.EntryTile, guest.Person!.Location);
        Assert.Equal(120, guest.ProvisionsRemaining);
        Assert.Null(guest.Person.HouseholdId); Assert.Null(guest.Person.HomeStructureId);
        Assert.Equal(population, engine.Population); Assert.Equal(households, engine.Households.Count);
        Assert.DoesNotContain(engine.Citizens, x => x.Id.Value == guest.CitizenId);
        Assert.DoesNotContain(Field<LivingWorldState>(engine, "_living").People, x => x.CitizenId == guest.CitizenId);
        Assert.False((bool)Invoke(engine, "TryCreateNewcomer", 7L)!);
        var snapshot = engine.CreatePersistenceSnapshot();
        Assert.Single(snapshot.ScheduledEvents, x => x.Name == SimulationEngine.NewcomerStepEvent);
        var restored = new SimulationEngine(snapshot);
        engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + 180));
        restored.AdvanceUntil(engine.CurrentMinute);
        Assert.Equal(engine.LivingStateJson, restored.LivingStateJson);
        Assert.Equal(engine.CreatePersistenceSnapshot().ScheduledEvents, restored.CreatePersistenceSnapshot().ScheduledEvents);
        Assert.True(Guest(engine).Person!.LifetimeMovementSteps > 0);
    }

    [Fact]
    public void PhysicalExitIsAliveAndExportsItsOwnRemainingGoods()
    {
        var engine = new SimulationEngine(Village.Value);
        Assert.True((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        var guest = Guest(engine);
        engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + 180));
        Invoke(engine, "BeginNewcomerExit", guest, "choice");
        var population = engine.Population;
        engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + 3L * WorldCalendar.MinutesPerDay));
        guest = Guest(engine);
        Assert.Equal(NewcomerPhase.Departed, guest.Phase);
        Assert.Equal(guest.EntryTile, guest.Person!.Location);
        Assert.True(guest.Person.IsAlive); Assert.Null(guest.Person.DeathMinute);
        Assert.Equal(guest.InitialProvisions, guest.ProvisionsConsumed + guest.ProvisionsExported);
        Assert.Equal(0, guest.ProvisionsRemaining);
        Assert.DoesNotContain(engine.CreatePersistenceSnapshot().HistoricalEvents, x => x.EventType == HistoricalEventType.VisitorDied);
        Assert.Equal(population, engine.Population);
        Assert.Equal("choice", guest.ExitReason);
        _ = new SimulationEngine(engine.CreatePersistenceSnapshot());
    }

    [Fact]
    public void ContactRequiresAlivePhysicalResidentAndCannotFormPartnership()
    {
        var engine = new SimulationEngine(Village.Value);
        Assert.True((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        var guest = Guest(engine);
        while (guest.Phase == NewcomerPhase.Approaching) engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + 60));
        var person = guest.Person!;
        var resident = Field<Dictionary<long, Citizen>>(engine, "_citizens").Values.First(x => x.IsAlive);
        person.TargetCitizenId = resident.Id;
        resident.Location = guest.EntryTile;
        Invoke(engine, "CompleteGuestContact", guest);
        Assert.Empty(guest.Contacts);
        resident.Location = person.Location;
        Invoke(engine, "CompleteGuestContact", guest);
        var contact = Assert.Single(guest.Contacts);
        Assert.Equal(1, contact.InteractionCount);
        Assert.Null(person.PartnerId); Assert.Null(person.HouseholdId);
        Assert.DoesNotContain(engine.Relationships, x => x.CitizenAId == person.Id || x.CitizenBId == person.Id);
        resident.DeathMinute = engine.CurrentMinute.Value;
        Invoke(engine, "CompleteGuestContact", guest);
        Assert.Equal(1, Assert.Single(guest.Contacts).InteractionCount);
    }

    [Fact]
    public void EmptyWorldCannotReceiveAnExtinctionRescue()
    {
        var engine = new SimulationEngine(Village.Value);
        foreach (var citizen in Field<Dictionary<long, Citizen>>(engine, "_citizens").Values) citizen.DeathMinute = engine.CurrentMinute.Value;
        var counters = engine.CounterSnapshot;
        Assert.False((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        Assert.Empty(Field<LivingWorldState>(engine, "_living").Newcomers!.Visitors);
        Assert.Equal(counters, engine.CounterSnapshot);
    }

    [Fact]
    public void GuestDeathIsExternalFactAndDoesNotIncrementResidentDeaths()
    {
        var engine = new SimulationEngine(Village.Value);
        Assert.True((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        var guest = Guest(engine); var population = engine.Population; var deaths = engine.DeadPopulation;
        // Run death through its authoritative event to consume the one pending guest event.
        guest.Person!.Health = 1; guest.Person.Needs = new CitizenNeeds(10000, 10000, 10000, 10000);
        guest.Person.HealthUpdatedMinute = engine.CurrentMinute.Value - CitizenSimulationRules.SurvivalCheckIntervalMinutes;
        engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + 360));
        Assert.Equal(NewcomerPhase.Dead, guest.Phase);
        Assert.Equal(0, guest.ProvisionsRemaining); Assert.Equal(guest.InitialProvisions, guest.ProvisionsLost + guest.ProvisionsConsumed);
        Assert.Equal(population, engine.Population); Assert.Equal(deaths, engine.DeadPopulation);
        var snapshot = engine.CreatePersistenceSnapshot();
        Assert.Single(snapshot.HistoricalEvents, x => x.EventType == HistoricalEventType.VisitorDied);
        Assert.DoesNotContain(snapshot.ScheduledEvents, x => x.Name == SimulationEngine.NewcomerStepEvent);
        _ = new SimulationEngine(snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdmissionRechecksFoodAndCreatesOneNativeAccountWithoutBirth(bool crossPopulationMilestone)
    {
        var engine = new SimulationEngine(Village.Value);
        Assert.True((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        var guest = Guest(engine);
        // A reserved shelter protects all existing homes while this person approaches.
        var homes = engine.Citizens.ToDictionary(x => x.Id.Value, x => x.HomeStructureId);
        Invoke(engine, "ReconcileMigrationHouseholdsAndHousing");
        Assert.All(engine.Citizens, x => Assert.Equal(homes[x.Id.Value], x.HomeStructureId));
        while (guest.Phase == NewcomerPhase.Approaching) engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + 60));
        Assert.Equal(NewcomerPhase.Visiting, guest.Phase);
        // Freeze discretionary admission by moving common supplies into an existing native account.
        var donor = Field<Dictionary<long, Household>>(engine, "_households").Values.First(x => x.DissolvedMinute is null).Id.Value;
        var common = engine.Settlement.FoodStored;
        engine.Settlement.FoodStored = 0;
        Invoke(engine, "AddPrivate", donor, ResourceType.Food, (long)common);
        engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + WorldCalendar.MinutesPerDay));
        Assert.Equal(NewcomerPhase.Visiting, guest.Phase);
        engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + 17));
        var person = guest.Person!;
        Assert.True(person.HealthUpdatedMinute < engine.CurrentMinute.Value);
        var target = Field<Dictionary<long, Citizen>>(engine, "_citizens").Values.First(x => x.IsAlive);
        var targetLocation = target.Location;
        target.Location = person.Location; person.TargetCitizenId = target.Id;
        for (var index = 0; index < 20; index++) Invoke(engine, "CompleteGuestContact", guest);
        target.Location = targetLocation;
        Assert.False((bool)Invoke(engine, "TryJoinNewcomer", guest)!);
        if (crossPopulationMilestone)
        {
            var people = Field<Dictionary<long, Citizen>>(engine, "_citizens");
            for (var index = 0; index < 4; index++)
            {
                var pair = people.Values.Where(x => x.IsAlive && x.PartnerId is { } partner && x.Id.Value < partner.Value && !people.Values.Any(c => c.ParentAId == x.Id || c.ParentBId == x.Id))
                    .Select(x => (First: x, Second: people[x.PartnerId!.Value.Value])).FirstOrDefault();
                if (pair.First is null)
                {
                    pair = people.Values.Where(x => x.IsAlive && x.PartnerId is null && x.AgeYears(engine.CurrentMinute) >= 18)
                        .SelectMany(x => people.Values.Where(y => y.IsAlive && y.PartnerId is null && y.Id.Value > x.Id.Value && y.AgeYears(engine.CurrentMinute) >= 18 && Math.Abs(x.AgeYears(engine.CurrentMinute) - y.AgeYears(engine.CurrentMinute)) <= 20).Select(y => (First: x, Second: y))).First();
                    Invoke(engine, "SetRelationship", pair.First.Id, pair.Second.Id, 10000, 10000, 10000, 0);
                    Invoke(engine, "TryFormPartnership", pair.First, pair.Second, engine.Relationships.Single(r => r.CitizenAId == pair.First.Id && r.CitizenBId == pair.Second.Id));
                    Invoke(engine, "RecordPartnershipHistory", pair.First, pair.Second);
                }
                var family = Field<Dictionary<long, Household>>(engine, "_households")[pair.First.HouseholdId!.Value.Value];
                Invoke(engine, "CreateChild", pair.First, pair.Second, family);
            }
            Invoke(engine, "ReconcileHouseholdsAndHousing"); Invoke(engine, "SynchronizeLivingPeople");
            Assert.Equal(24, engine.Population);
        }
        var birthsBeforeAdmission = Field<HistoryState>(engine, "_historyState").BirthsSinceSample;
        var stocks = Field<SortedDictionary<long, HouseholdStock>>(engine, "_householdStocks");
        var required = engine.Population * 20 + 20 - engine.Settlement.FoodStored;
        foreach (var stock in stocks.Values.ToArray())
        {
            var quantity = Math.Min(required, checked((int)stock.Holdings.Food));
            if (quantity <= 0) continue;
            Invoke(engine, "AddPrivate", stock.HouseholdId, ResourceType.Food, -(long)quantity);
            Invoke(engine, "AddCommons", 1L, ResourceType.Food, quantity);
            required -= quantity;
            if (required <= 0) break;
        }
        Assert.True(required <= 0);
        // Personality remains authoritative: retry with a cooperative/sociable controlled fixture.
        var cooperative = new Citizen(person.Id, (int?)null, person.GivenName, person.FamilyName, person.BirthMinute, person.Location,
            new CitizenTraits(5000, 10000, 5000, 10000, 5000, 5000), person.Skills, person.Needs)
        { NeedsUpdatedMinute = engine.CurrentMinute.Value, HealthUpdatedMinute = person.HealthUpdatedMinute, ActionSequence = person.ActionSequence };
        guest.Person = cooperative;
        var population = engine.Population; var householdCount = engine.Households.Count; var provisions = guest.ProvisionsRemaining;
        var nextGuest = Assert.Single(engine.CreatePersistenceSnapshot().ScheduledEvents, x => x.Name == SimulationEngine.NewcomerStepEvent);
        Invoke(engine, "SetGuestAction", cooperative, CitizenAction.Idle, checked((int)(nextGuest.Order.DueWorldMinute.Value - engine.CurrentMinute.Value)));
        engine.AdvanceUntil(nextGuest.Order.DueWorldMinute);
        Assert.Equal(NewcomerPhase.Resident, guest.Phase); Assert.Null(guest.Person);
        var admitted = engine.GetCitizen(new CitizenId(guest.CitizenId))!;
        Assert.Equal(population + 1, engine.Population); Assert.Equal(householdCount + 1, engine.Households.Count);
        Assert.Equal(provisions, stocks[admitted.HouseholdId!.Value.Value].Holdings.Food);
        Assert.NotNull(admitted.HomeStructureId); Assert.Null(admitted.ParentAId); Assert.Null(admitted.ParentBId);
        Assert.Null(admitted.FounderOrdinal); Assert.Null(admitted.PartnerId);
        Assert.Contains(engine.Relationships, x => x.CitizenAId == admitted.Id || x.CitizenBId == admitted.Id);
        var snapshot = engine.CreatePersistenceSnapshot();
        var survival = Assert.Single(snapshot.ScheduledEvents, x => x.Name == CitizenEventNames.SurvivalCheck && x.Order.EntitySortKey == guest.CitizenId);
        Assert.Equal(cooperative.HealthUpdatedMinute + CitizenSimulationRules.SurvivalCheckIntervalMinutes, survival.Order.DueWorldMinute.Value);
        Assert.Single(snapshot.HistoricalEvents, x => x.EventType == HistoricalEventType.NewcomerJoined);
        Assert.Equal(birthsBeforeAdmission, snapshot.HistoryState!.BirthsSinceSample);
        if (crossPopulationMilestone)
        {
            Assert.Equal(25, engine.Population);
            Assert.Equal(25, snapshot.HistoryState.PopulationMilestoneWatermark);
            var milestone = Assert.Single(snapshot.HistoricalEvents, x => x.EventType == HistoricalEventType.PopulationMilestone && x.WorldMinute == guest.JoinedMinute);
            Assert.Equal(HistoricalEventPayloads.PopulationMilestone(25), milestone.PayloadJson);
            var children = snapshot.Citizens.Where(x => x.ParentAId is not null && x.ParentBId is not null).ToArray();
            Assert.Equal(4, children.Length);
            Assert.All(children, child =>
            {
                Assert.True(child.Id.Value > admitted.Id.Value);
                Assert.Contains(snapshot.Citizens, parent => parent.Id == child.ParentAId);
                Assert.Contains(snapshot.Citizens, parent => parent.Id == child.ParentBId);
            });
        }
        Assert.DoesNotContain(snapshot.HistoricalEvents.Where(x => x.EventType == HistoricalEventType.CitizenBorn), x => snapshot.HistoricalEventCitizens.Any(l => l.HistoricalEventId == x.Id && l.CitizenId == admitted.Id));
        var restored = new SimulationEngine(snapshot);
        Assert.Equal(snapshot.LivingStateJson, restored.CreatePersistenceSnapshot().LivingStateJson);
        if (crossPopulationMilestone)
        {
            var due = new WorldMinute(engine.CurrentMinute.Value + 360);
            engine.AdvanceUntil(due); restored.AdvanceUntil(due);
            Assert.Equal(engine.CreatePersistenceSnapshot().LivingStateJson, restored.CreatePersistenceSnapshot().LivingStateJson);
            Assert.Equal(engine.CreatePersistenceSnapshot().ScheduledEvents, restored.CreatePersistenceSnapshot().ScheduledEvents);
        }
    }

    [Fact]
    public void DeclinedGuestPhysicallyLeavesWithinSevenDaysWithoutResidentDisplacement()
    {
        var engine = new SimulationEngine(Village.Value);
        Assert.True((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        var guest = Guest(engine);
        var donor = engine.Households.First(x => x.DissolvedMinute is null).Id.Value;
        var until = engine.CurrentMinute.Value + 12L * WorldCalendar.MinutesPerDay;
        while (guest.Phase is NewcomerPhase.Approaching or NewcomerPhase.Visiting or NewcomerPhase.Leaving && engine.CurrentMinute.Value < until)
        {
            // A coherent lean commons fixture retains food in an existing household, so admission stays blocked.
            var food = engine.Settlement.FoodStored;
            engine.Settlement.FoodStored = 0;
            Invoke(engine, "AddPrivate", donor, ResourceType.Food, (long)food);
            engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + 60));
        }
        Assert.Equal(NewcomerPhase.Departed, guest.Phase);
        Assert.NotNull(guest.VisitingStartedMinute);
        Assert.True(guest.DepartedMinute - guest.VisitingStartedMinute <= SimulationEngine.MaximumVisitorStayMinutes);
        Assert.Equal(guest.EntryTile, guest.Person!.Location);
        Assert.Equal("deadline", guest.ExitReason);
        Assert.True(guest.Person.IsAlive);
        Assert.DoesNotContain(engine.Citizens, x => x.Id.Value == guest.CitizenId);
        _ = new SimulationEngine(engine.CreatePersistenceSnapshot());
    }

    [Fact]
    public void LostHostPopulationSendsGuestToTheEdgeAndNeverRescuesExtinction()
    {
        var engine = new SimulationEngine(Village.Value);
        Assert.True((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        var guest = Guest(engine);
        engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + 180));
        foreach (var citizen in Field<Dictionary<long, Citizen>>(engine, "_citizens").Values.Where(x => x.IsAlive).ToArray()) Invoke(engine, "KillNatural", citizen);
        Assert.Equal(0, engine.Population);
        engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + 3L * WorldCalendar.MinutesPerDay));
        Assert.Equal(NewcomerPhase.Departed, guest.Phase);
        Assert.Equal("extinction", guest.ExitReason);
        Assert.Equal(guest.EntryTile, guest.Person!.Location);
        Assert.True(guest.Person.IsAlive); Assert.Equal(0, engine.Population);
        Assert.False((bool)Invoke(engine, "TryCreateNewcomer", 7L)!);
        _ = new SimulationEngine(engine.CreatePersistenceSnapshot());
    }

    [Fact]
    public void UnreachableActualEdgeRouteConsumesNoIdentityOrProvisions()
    {
        var engine = new SimulationEngine(Village.Value);
        var paths = Field<Dictionary<(TileCoordinate Start, TileCoordinate End), IReadOnlyList<TileCoordinate>?>>(engine, "_pathCache");
        foreach (var edge in engine.World.Tiles.Where(x => x.Walkable && (x.Coordinate.X == 0 || x.Coordinate.Y == 0 || x.Coordinate.X == engine.World.Width - 1 || x.Coordinate.Y == engine.World.Height - 1)))
        foreach (var shelter in engine.Structures.Where(x => x.Type == StructureType.Shelter && x.Status == StructureStatus.Complete))
            paths[(edge.Coordinate, shelter.Location)] = null;
        var counters = engine.CounterSnapshot; var food = engine.TotalStoredFood;
        Assert.False((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        Assert.Equal(counters, engine.CounterSnapshot); Assert.Equal(food, engine.TotalStoredFood);
        Assert.Empty(Field<LivingWorldState>(engine, "_living").Newcomers!.Visitors);
        Assert.Null(engine.CreatePersistenceSnapshot().MigrationState!.DaughterSettlement);
    }

    [Fact]
    public void LostCompletedShelterStartsPhysicalExitEvenIfSupportLaterRecovers()
    {
        var engine = new SimulationEngine(Village.Value);
        Assert.True((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        var guest = Guest(engine);
        var shelter = Field<Dictionary<long, Structure>>(engine, "_structures")[guest.ShelterStructureId];
        engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + 180));
        var until = engine.CurrentMinute.Value + 360;
        while (guest.Phase == NewcomerPhase.Approaching && engine.CurrentMinute.Value < until)
        {
            shelter.Status = StructureStatus.UnderConstruction;
            engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + 1));
        }
        Assert.Equal(NewcomerPhase.Leaving, guest.Phase); Assert.Equal("lost_support", guest.ExitReason);
        shelter.Status = StructureStatus.Complete;
        engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + 3L * WorldCalendar.MinutesPerDay));
        Assert.Equal(NewcomerPhase.Departed, guest.Phase);
        Assert.Equal(guest.EntryTile, guest.Person!.Location); Assert.True(guest.Person.IsAlive);
        _ = new SimulationEngine(engine.CreatePersistenceSnapshot());
    }

    [Fact]
    public void RealGuestSocialActionRequiresSixtyPhysicalMinutes()
    {
        var (engine, guest, target) = SocializingFixture();
        var start = guest.Person!.ActionStartedMinute!.Value;
        var before = guest.Contacts.Sum(x => x.InteractionCount);
        engine.AdvanceUntil(start.Add(59));
        Assert.Equal(before, guest.Contacts.Sum(x => x.InteractionCount));
        Assert.Equal(CitizenAction.Socialize, guest.Person.CurrentAction);
        engine.AdvanceUntil(start.Add(60));
        Assert.Equal(before + 1, guest.Contacts.Sum(x => x.InteractionCount));
        Assert.Equal(target.Id, guest.Person.TargetCitizenId);
        _ = new SimulationEngine(engine.CreatePersistenceSnapshot());
    }

    [Fact]
    public void DyingSocialTargetCancelsGuestAtomicallyAndRestoresWithIdenticalContinuation()
    {
        var (engine, guest, target) = SocializingFixture();
        var healthMinute = guest.Person!.HealthUpdatedMinute;
        var provisions = guest.ProvisionsRemaining;
        var sequence = guest.Person.ActionSequence;
        engine.AdvanceUntil(guest.Person.ActionStartedMinute!.Value.Add(17));
        Invoke(engine, "KillNatural", target);
        // The normal lifecycle event performs this native living-person synchronization before returning.
        Invoke(engine, "SynchronizeLivingPeople");
        Assert.Null(guest.Person.TargetCitizenId); Assert.Equal(CitizenAction.Idle, guest.Person.CurrentAction);
        Assert.True(guest.Person.ActionSequence > sequence);
        Assert.Equal(healthMinute, guest.Person.HealthUpdatedMinute); Assert.Equal(provisions, guest.ProvisionsRemaining);
        var snapshot = engine.CreatePersistenceSnapshot();
        Assert.Single(snapshot.ScheduledEvents, x => x.Name == SimulationEngine.NewcomerStepEvent);
        var restored = new SimulationEngine(snapshot);
        var until = engine.CurrentMinute.Add(360);
        engine.AdvanceUntil(until); restored.AdvanceUntil(until);
        Assert.Equal(engine.LivingStateJson, restored.LivingStateJson);
        Assert.Equal(engine.ComputeHistoryFingerprint(), restored.ComputeHistoryFingerprint());
        Assert.Equal(engine.CreatePersistenceSnapshot().ScheduledEvents, restored.CreatePersistenceSnapshot().ScheduledEvents);
        Assert.DoesNotContain(guest.Contacts, x => x.CitizenAId == target.Id || x.CitizenBId == target.Id);
    }

    [Fact]
    public void DaughterHostAdmissionKeepsExplicitResidenceAccountsAndExternalRoot()
    {
        var baseline = DaughterVillageFixture().CreatePersistenceSnapshot();
        SimulationEngine? engine = null; NewcomerState? guest = null;
        // Every trial uses the same coherent two-site village; only the independently keyed opportunity changes.
        for (long year = 6; year < 70; year++)
        {
            var candidate = new SimulationEngine(baseline);
            if (!(bool)Invoke(candidate, "TryCreateNewcomer", year)!) continue;
            if (Guest(candidate).HostSettlementId != 2) continue;
            engine = candidate; guest = Guest(candidate); break;
        }
        Assert.NotNull(engine); Assert.NotNull(guest);
        while (guest.Phase == NewcomerPhase.Approaching) engine.AdvanceUntil(engine.CurrentMinute.Add(60));
        var daughterStock = (SettlementState)Invoke(engine, "SettlementFor", 2L)!;
        var daughterCitizens = Field<Dictionary<long, Citizen>>(engine, "_citizens").Values.Where(x => x.IsAlive && (long)Invoke(engine, "SiteIdForCitizen", x)! == 2).ToArray();
        var donor = daughterCitizens.First().HouseholdId!.Value.Value;
        var food = daughterStock.FoodStored; daughterStock.FoodStored = 0;
        Invoke(engine, "AddPrivate", donor, ResourceType.Food, (long)food);
        engine.AdvanceUntil(engine.CurrentMinute.Add(WorldCalendar.MinutesPerDay + 17));
        Assert.Equal(NewcomerPhase.Visiting, guest.Phase);
        var target = daughterCitizens.First(); var location = target.Location;
        target.Location = guest.Person!.Location; guest.Person.TargetCitizenId = target.Id;
        for (var index = 0; index < 20; index++) Invoke(engine, "CompleteGuestContact", guest);
        target.Location = location;
        var old = guest.Person;
        guest.Person = new Citizen(old.Id, (int?)null, old.GivenName, old.FamilyName, old.BirthMinute, old.Location,
            new CitizenTraits(5000, 10000, 5000, 10000, 5000, 5000), old.Skills, old.Needs)
        { NeedsUpdatedMinute = engine.CurrentMinute.Value, HealthUpdatedMinute = old.HealthUpdatedMinute, ActionSequence = old.ActionSequence };
        var stocks = Field<SortedDictionary<long, HouseholdStock>>(engine, "_householdStocks");
        var needed = 20 * (daughterCitizens.Length + 1) - daughterStock.FoodStored;
        var quantity = Math.Max(0, needed);
        Assert.True(stocks[donor].Holdings.Food >= quantity);
        Invoke(engine, "AddPrivate", donor, ResourceType.Food, -(long)quantity);
        Invoke(engine, "AddCommons", 2L, ResourceType.Food, quantity);
        // Import capacity belongs to the host, so a full daughter cannot borrow original-village space.
        var capacity = daughterStock.BaseStorageCapacity;
        daughterStock.BaseStorageCapacity = daughterStock.StorageUsed;
        Assert.False((bool)Invoke(engine, "TryJoinNewcomer", guest)!);
        daughterStock.BaseStorageCapacity = capacity;
        var due = Assert.Single(engine.CreatePersistenceSnapshot().ScheduledEvents, x => x.Name == SimulationEngine.NewcomerStepEvent).Order.DueWorldMinute;
        Invoke(engine, "SetGuestAction", guest.Person, CitizenAction.Idle, checked((int)(due.Value - engine.CurrentMinute.Value)));
        engine.AdvanceUntil(due);
        Assert.Equal(NewcomerPhase.Resident, guest.Phase);
        var citizen = engine.GetCitizen(new CitizenId(guest.CitizenId))!;
        var snapshot = engine.CreatePersistenceSnapshot();
        Assert.Equal(2, snapshot.MigrationState!.CitizenResidences.Single(x => x.EntityId == citizen.Id.Value).SettlementId);
        Assert.Equal(2, snapshot.MigrationState.HouseholdResidences.Single(x => x.EntityId == citizen.HouseholdId!.Value.Value).SettlementId);
        Assert.Equal(guest.ProvisionsTransferred, snapshot.Economy!.Households.Single(x => x.HouseholdId == citizen.HouseholdId!.Value.Value).Holdings.Food);
        Assert.Null(citizen.ParentAId); Assert.Null(citizen.ParentBId); Assert.Null(citizen.FounderOrdinal);
        var restored = new SimulationEngine(snapshot);
        Assert.Equal(engine.ComputeHistoryFingerprint(), restored.ComputeHistoryFingerprint());
        Assert.Equal(engine.ComputeEconomyFingerprint(), restored.ComputeEconomyFingerprint());
        var until = engine.CurrentMinute.Add(360); engine.AdvanceUntil(until); restored.AdvanceUntil(until);
        Assert.Equal(engine.LivingStateJson, restored.LivingStateJson);
        Assert.Equal(engine.CreatePersistenceSnapshot().ScheduledEvents, restored.CreatePersistenceSnapshot().ScheduledEvents);
    }

    [Fact]
    public void NativePartnershipKeepsFourResidentsHousedAndPhysicallyReleasesVisitor()
    {
        var (engine, guest, parent, partner, children, unrelated, shelterA, shelterB) = NativeHousingFixture();
        var before = engine.CreatePersistenceSnapshot();
        Assert.Equal(NewcomerPhase.Visiting, guest.Phase);
        var guestLocation = guest.Person!.Location;
        engine.AdvanceUntil(new WorldMinute(parent.ActionCompletesMinute!.Value.Value - 1));
        var provisions = guest.ProvisionsRemaining; var healthBoundary = guest.Person.HealthUpdatedMinute;
        // Complete the ordinary native social event, including its household merge and housing reconciliation.
        engine.AdvanceUntil(parent.ActionCompletesMinute!.Value);
        Assert.Equal(partner.Id, parent.PartnerId);
        Assert.Equal(NewcomerPhase.Leaving, guest.Phase);
        Assert.Equal("lost_support", guest.ExitReason);
        Assert.Equal(guestLocation, guest.Person.Location);
        Assert.Equal(guestLocation, guest.Route[0]); Assert.Equal(guest.EntryTile, guest.Route[^1]);
        Assert.Equal(0, (int)Invoke(engine, "GuestShelterReservations", shelterA.Id.Value)!);
        Assert.All(children.Append(parent).Append(partner), citizen => Assert.Equal(shelterA.Id, citizen.HomeStructureId));
        Assert.All(unrelated, citizen => Assert.Equal(shelterB.Id, citizen.HomeStructureId));
        Assert.Equal(provisions, guest.ProvisionsRemaining);
        Assert.Equal(healthBoundary + (engine.CurrentMinute.Value - healthBoundary) / CitizenSimulationRules.SurvivalCheckIntervalMinutes * CitizenSimulationRules.SurvivalCheckIntervalMinutes, guest.Person.HealthUpdatedMinute);
        var snapshot = engine.CreatePersistenceSnapshot();
        Assert.Single(snapshot.ScheduledEvents, item => item.Name == SimulationEngine.NewcomerStepEvent);
        var restored = new SimulationEngine(snapshot);
        var replay = new SimulationEngine(before); replay.AdvanceUntil(engine.CurrentMinute);
        Assert.Equal(engine.LivingStateJson, replay.LivingStateJson);
        Assert.Equal(engine.HistoryFingerprint, replay.HistoryFingerprint);
        Assert.Equal(snapshot.ScheduledEvents, replay.CreatePersistenceSnapshot().ScheduledEvents);
        var until = engine.CurrentMinute.Add(3L * WorldCalendar.MinutesPerDay);
        engine.AdvanceUntil(until); restored.AdvanceUntil(until);
        Assert.Equal(NewcomerPhase.Departed, Guest(engine).Phase);
        Assert.Equal(engine.LivingStateJson, restored.LivingStateJson);
        Assert.Equal(engine.HistoryFingerprint, restored.HistoryFingerprint);
        Assert.Equal(engine.CreatePersistenceSnapshot().ScheduledEvents, restored.CreatePersistenceSnapshot().ScheduledEvents);
    }

    private static (SimulationEngine Engine, NewcomerState Guest, Citizen Parent, Citizen Partner, Citizen[] Children, Citizen[] Unrelated, Structure ShelterA, Structure ShelterB) NativeHousingFixture()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.NewcomersRulesVersion);
        var people = Field<Dictionary<long, Citizen>>(engine, "_citizens");
        var households = Field<Dictionary<long, Household>>(engine, "_households");
        var adults = people.Values.Where(x => x.AgeYears(engine.CurrentMinute) is >= 18 and <= 45).OrderBy(x => x.Id.Value).Take(5).ToArray();
        Assert.Equal(5, adults.Length);
        var parent = adults[0]; var deceased = adults[1]; var partner = adults[2];
        var unrelatedParents = (First: adults[3], Second: adults[4]);
        Assert.True(Math.Abs(parent.AgeYears(engine.CurrentMinute) - partner.AgeYears(engine.CurrentMinute)) <= 20);
        foreach (var citizen in people.Values.Where(x => !adults.Contains(x)).ToArray()) Invoke(engine, "KillNatural", citizen);
        foreach (var pair in new[] { (First: parent, Second: deceased), unrelatedParents })
        {
            Invoke(engine, "SetRelationship", pair.First.Id, pair.Second.Id, 10000, 10000, 10000, 0);
            Invoke(engine, "TryFormPartnership", pair.First, pair.Second, engine.Relationships.Single(x => x.CitizenAId == pair.First.Id && x.CitizenBId == pair.Second.Id));
            Invoke(engine, "RecordPartnershipHistory", pair.First, pair.Second);
        }
        var family = households[parent.HouseholdId!.Value.Value];
        var otherFamily = households[unrelatedParents.First.HouseholdId!.Value.Value];
        Invoke(engine, "CreateChild", parent, deceased, family); Invoke(engine, "CreateChild", parent, deceased, family);
        Invoke(engine, "CreateChild", unrelatedParents.First, unrelatedParents.Second, otherFamily);
        Invoke(engine, "SynchronizeLivingPeople");
        Invoke(engine, "KillNatural", deceased); Invoke(engine, "SynchronizeLivingPeople");
        Invoke(engine, "ReconcileHouseholdsAndHousing");
        Assert.Equal(7, engine.Population);
        var structures = Field<Dictionary<long, Structure>>(engine, "_structures");
        var deadline = engine.CurrentMinute.Add(120L * WorldCalendar.MinutesPerDay);
        while (structures.Values.Count(x => x.Type == StructureType.Shelter && x.Status == StructureStatus.Complete) < 2 && engine.CurrentMinute < deadline)
            engine.AdvanceUntil(engine.CurrentMinute.Add(60));
        var shelters = structures.Values.Where(x => x.Type == StructureType.Shelter && x.Status == StructureStatus.Complete).OrderBy(x => x.Id.Value).ToArray();
        Assert.Equal(2, shelters.Length);
        var shelterA = shelters[0]; var shelterB = shelters[1];
        var children = people.Values.Where(x => x.IsAlive && (x.ParentAId == parent.Id || x.ParentBId == parent.Id)).ToArray();
        var unrelated = people.Values.Where(x => x.IsAlive && x.HouseholdId == otherFamily.Id).ToArray();
        Assert.Equal(2, children.Length); Assert.Equal(3, unrelated.Length);
        var selected = children.Append(parent).Append(partner).Concat(unrelated).ToArray();
        foreach (var citizen in selected)
        {
            Invoke(engine, "RecoverInterruptedCargo", citizen); Invoke(engine, "ReleaseLivingClaim", citizen);
            Invoke(engine, "FinishFoundingTravel", citizen); Invoke(engine, "WaitForFoundingParty", citizen);
            citizen.HomeStructureId = children.Contains(citizen) || citizen.Id == parent.Id ? shelterA.Id : shelterB.Id;
            citizen.Location = citizen.HomeStructureId == shelterA.Id ? shelterA.Location : shelterB.Location;
        }
        family.DwellingStructureId = shelterA.Id; otherFamily.DwellingStructureId = shelterB.Id;
        households[partner.HouseholdId!.Value.Value].DwellingStructureId = shelterB.Id;
        Invoke(engine, "ReconcileHouseholdsAndHousing");
        Assert.True((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        var guest = Guest(engine); Assert.Equal(shelterA.Id.Value, guest.ShelterStructureId);
        while (guest.Phase == NewcomerPhase.Approaching) engine.AdvanceUntil(engine.CurrentMinute.Add(60));
        foreach (var citizen in new[] { parent, partner })
        {
            Invoke(engine, "RecoverInterruptedCargo", citizen); Invoke(engine, "ReleaseLivingClaim", citizen);
            Invoke(engine, "FinishFoundingTravel", citizen); Invoke(engine, "WaitForFoundingParty", citizen);
            citizen.Location = shelterA.Location;
        }
        Invoke(engine, "SetRelationship", parent.Id, partner.Id, 10000, 10000, 10000, 0);
        // Replace the controlled actor's wait with a canonical 60-minute native social completion.
        var queue = typeof(SimulationEngine).GetField("_scheduledEvents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
        foreach (var pending in ((System.Collections.IEnumerable)queue).Cast<object>().Where(x =>
            (string)x.GetType().GetProperty("Name")!.GetValue(x)! == CitizenEventNames.ActionComplete &&
            ((ScheduledEventOrder)x.GetType().GetProperty("Order")!.GetValue(x)!).EntitySortKey == parent.Id.Value).ToArray())
            queue.GetType().GetMethod("Remove")!.Invoke(queue, [pending]);
        parent.CurrentAction = CitizenAction.Socialize; parent.ActionPhase = CitizenActionPhase.Perform;
        parent.TargetCitizenId = partner.Id; parent.ActionStartedMinute = engine.CurrentMinute; parent.ActionCompletesMinute = engine.CurrentMinute.Add(60);
        Invoke(engine, "ScheduleCitizen", parent, CitizenEventNames.ActionComplete, parent.ActionCompletesMinute.Value, CitizenEventNames.CompletionPriority);
        return (engine, guest, parent, partner, children, unrelated, shelterA, shelterB);
    }

    private static SimulationEngine DaughterVillageFixture()
    {
        var engine = new SimulationEngine(Village.Value);
        var people = Field<Dictionary<long, Citizen>>(engine, "_citizens");
        var households = Field<Dictionary<long, Household>>(engine, "_households");
        var household = households.Values.Where(x => x.DissolvedMinute is null).OrderBy(x => people.Values.Count(p => p.IsAlive && p.HouseholdId == x.Id)).First();
        var members = people.Values.Where(x => x.IsAlive && x.HouseholdId == household.Id).ToArray();
        var shelter = Field<Dictionary<long, Structure>>(engine, "_structures").Values.First(x => x.Type == StructureType.Shelter && x.Status == StructureStatus.Complete && !people.Values.Any(p => p.IsAlive && p.HomeStructureId == x.Id));
        foreach (var member in members)
        {
            Invoke(engine, "RecoverInterruptedCargo", member); Invoke(engine, "ReleaseLivingClaim", member);
            Invoke(engine, "FinishFoundingTravel", member); Invoke(engine, "WaitForFoundingParty", member);
            member.Location = shelter.Location; member.HomeStructureId = shelter.Id;
        }
        household.DwellingStructureId = shelter.Id;
        var living = Field<LivingWorldState>(engine, "_living");
        foreach (var order in living.Orders.Where(x => x.SubjectId is { } subject && members.Any(p => p.Id.Value == subject)).ToArray()) Invoke(engine, "CancelLivingOrder", order);
        var state = (MigrationWorldState)Invoke(engine, "CaptureMigrationState")!;
        var memberIds = members.Select(x => x.Id.Value).ToHashSet();
        var next = new MigrationWorldState(state.Version,
            state.CitizenResidences.Select(x => memberIds.Contains(x.EntityId) ? new MigrationEntityResidence(x.EntityId, 2) : x).ToArray(),
            state.HouseholdResidences.Select(x => x.EntityId == household.Id.Value ? new MigrationEntityResidence(x.EntityId, 2) : x).ToArray(),
            state.StructureOwners.Select(x => x.EntityId == shelter.Id.Value ? new MigrationEntityResidence(x.EntityId, 2) : x).ToArray(),
            state.FacilityOwners, state.WorkOrderOwners,
            new MigrationDaughterSettlementState(shelter.Location, new MigrationSettlementStockState(0, 0, 0, 10000, engine.CurrentMinute.Value, engine.CurrentMinute.Value + CitizenSimulationRules.ExposureGraceDurationMinutes), Enum.GetValues<LivingGood>().Select(x => new LivingStock(x, 0)).ToArray()),
            state.InTransitParties, state.FoundingPressure, state.LastRelocations, state.LastVisitAttemptYear, state.Roads);
        typeof(SimulationEngine).GetField("_migrationState", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, next);
        Invoke(engine, "InitializeMigrationRuntime");
        return engine;
    }

    private static (SimulationEngine Engine, NewcomerState Guest, Citizen Target) SocializingFixture()
    {
        var engine = new SimulationEngine(Village.Value);
        Assert.True((bool)Invoke(engine, "TryCreateNewcomer", 6L)!);
        var guest = Guest(engine);
        while (guest.Phase == NewcomerPhase.Approaching) engine.AdvanceUntil(engine.CurrentMinute.Add(60));
        var target = Field<Dictionary<long, Citizen>>(engine, "_citizens").Values.First(x => x.IsAlive);
        // Native cancellation/recovery helpers put the local actor into a canonical timed wait.
        Invoke(engine, "RecoverInterruptedCargo", target); Invoke(engine, "ReleaseLivingClaim", target);
        Invoke(engine, "FinishFoundingTravel", target); Invoke(engine, "WaitForFoundingParty", target);
        target.Location = guest.Person!.Location;
        guest.Person.Needs = new CitizenNeeds(); guest.Person.NeedsUpdatedMinute = engine.CurrentMinute.Value;
        var until = engine.CurrentMinute.Add(300);
        while (guest.Person.CurrentAction != CitizenAction.Socialize && engine.CurrentMinute < until) engine.AdvanceUntil(engine.CurrentMinute.Add(1));
        Assert.Equal(CitizenAction.Socialize, guest.Person.CurrentAction);
        Assert.Equal(target.Id, guest.Person.TargetCitizenId);
        return (engine, guest, target);
    }

    private static NewcomerState Guest(SimulationEngine engine) => Assert.Single(Field<LivingWorldState>(engine, "_living").Newcomers!.Visitors);
    private static T Field<T>(SimulationEngine engine, string name) => (T)typeof(SimulationEngine).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
    private static object? Invoke(SimulationEngine engine, string name, params object[] arguments) => typeof(SimulationEngine).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(x => x.Name == name && x.GetParameters().Length == arguments.Length && x.GetParameters().Zip(arguments).All(pair => pair.First.ParameterType.IsInstanceOfType(pair.Second))).Invoke(engine, arguments);
}
