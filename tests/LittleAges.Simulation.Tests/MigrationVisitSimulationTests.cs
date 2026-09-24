using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class MigrationVisitSimulationTests
{
    [Fact]
    public void NoKinAndRepeatedYearBoundaryDoNotStartAnotherVisit()
    {
        var fixture = CreateFixture(new WorldSeed(1402));
        var engine = fixture.Engine;
        InvokePrivate(engine, "EvaluateMigrationVisits");
        Assert.Empty(PrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        Assert.Equal(0, PrivateField<MigrationWorldState>(engine, "_migrationState").LastVisitAttemptYear);
        var attemptedYear = PrivateField<MigrationWorldState>(engine, "_migrationState").LastVisitAttemptYear;

        var citizens = PrivateField<Dictionary<long, Citizen>>(engine, "_citizens");
        citizens[fixture.VisitorId].ParentAId = new CitizenId(fixture.ParentId);
        citizens[fixture.RelativeId].ParentAId = new CitizenId(fixture.ParentId);
        InvokePrivate(engine, "EvaluateMigrationVisits");
        Assert.Empty(PrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        Assert.Equal(attemptedYear, PrivateField<MigrationWorldState>(engine, "_migrationState").LastVisitAttemptYear);
    }

    [Fact]
    public void UnreachableVisitorAndCargoObligationAreSkippedWithoutWithdrawingFood()
    {
        var unreachable = CreateFixture(new WorldSeed(1403));
        var citizens = PrivateField<Dictionary<long, Citizen>>(unreachable.Engine, "_citizens");
        citizens[unreachable.VisitorId].ParentAId = new CitizenId(unreachable.ParentId);
        citizens[unreachable.RelativeId].ParentAId = new CitizenId(unreachable.ParentId);
        var noRoute = PrivateField<Dictionary<(TileCoordinate Start, TileCoordinate End), IReadOnlyList<TileCoordinate>?>>(
            unreachable.Engine, "_pathCache");
        var blockedFrom = citizens[unreachable.VisitorId].Location;
        foreach (var livingCitizen in citizens.Values.Where(x => x.IsAlive))
        {
            noRoute[(livingCitizen.Location, unreachable.Engine.World.StartingSite)] = null;
            noRoute[(livingCitizen.Location, unreachable.DaughterSite)] = null;
        }
        Assert.Null(InvokePrivate(unreachable.Engine, "FindPathCached", blockedFrom, unreachable.DaughterSite));
        var foodBefore = unreachable.Engine.Settlement.FoodStored;
        InvokePrivate(unreachable.Engine, "EvaluateMigrationVisits");
        Assert.Empty(PrivateField<MigrationWorldState>(unreachable.Engine, "_migrationState").InTransitParties);
        Assert.Equal(foodBefore, unreachable.Engine.Settlement.FoodStored);

        var obligated = CreateFixture(new WorldSeed(1404));
        var obligatedCitizens = PrivateField<Dictionary<long, Citizen>>(obligated.Engine, "_citizens");
        obligatedCitizens[obligated.VisitorId].ParentAId = new CitizenId(obligated.ParentId);
        obligatedCitizens[obligated.RelativeId].ParentAId = new CitizenId(obligated.ParentId);
        foreach (var adult in obligatedCitizens.Values.Where(x => x.IsAlive && x.AgeYears(obligated.Engine.CurrentMinute) >= 18))
        {
            adult.CarriedResourceType = ResourceType.Food;
            adult.CarriedResourceQuantity = 1;
        }
        var obligatedFoodBefore = obligated.Engine.Settlement.FoodStored;
        InvokePrivate(obligated.Engine, "EvaluateMigrationVisits");
        Assert.Empty(PrivateField<MigrationWorldState>(obligated.Engine, "_migrationState").InTransitParties);
        Assert.Equal(obligatedFoodBefore, obligated.Engine.Settlement.FoodStored);
    }

    [Fact]
    public void YearBoundarySelectsStableKinThenCompletesOutboundDwellAndReturn()
    {
        var fixture = CreateFixture(new WorldSeed(1401));
        var engine = fixture.Engine;
        var citizens = PrivateField<Dictionary<long, Citizen>>(engine, "_citizens");
        var before = engine.CreatePersistenceSnapshot();
        citizens[fixture.VisitorId].ParentAId = new CitizenId(fixture.ParentId);
        citizens[fixture.RelativeId].ParentAId = new CitizenId(fixture.ParentId);

        var visitor = citizens[fixture.VisitorId];
        var householdId = visitor.HouseholdId!.Value.Value;
        var visitorHome = visitor.HomeStructureId;
        var visitorResidence = before.MigrationState!.CitizenResidences.Single(x => x.EntityId == visitor.Id.Value);
        var householdResidence = before.MigrationState.HouseholdResidences.Single(x => x.EntityId == householdId);
        var originFood = engine.Settlement.FoodStored;

        InvokePrivate(engine, "EvaluateMigrationVisits");
        var outbound = Assert.Single(PrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        Assert.Equal(fixture.VisitorId, Assert.Single(outbound.CitizenIds));
        Assert.Equal(fixture.RelativeId, outbound.VisitRelativeId);
        Assert.Equal(MigrationJourneyKind.Visit, outbound.JourneyKind);
        Assert.Equal(MigrationVisitPhase.Outbound, outbound.VisitPhase);
        Assert.Equal(0, PrivateField<MigrationWorldState>(engine, "_migrationState").LastVisitAttemptYear);
        Assert.NotEmpty(outbound.Cargo);
        Assert.All(outbound.Cargo, cargo =>
        {
            Assert.Equal(MigrationCargoGood.Food, cargo.Good);
            Assert.Equal(MigrationCargoPurpose.Provisions, cargo.Purpose);
        });
        var provisions = outbound.Cargo.Sum(x => x.Quantity);
        Assert.Equal(originFood - checked((int)provisions), engine.Settlement.FoodStored);

        citizens[fixture.VisitorId].ParentAId = null;
        citizens[fixture.RelativeId].ParentAId = null;

        engine.AdvanceUntil(new WorldMinute(outbound.DepartedMinute + outbound.RemainingPathCost));
        var dwelling = Assert.Single(PrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        Assert.Equal(MigrationVisitPhase.Dwell, dwelling.VisitPhase);
        Assert.Equal(fixture.DaughterSite, dwelling.Location);
        Assert.Equal(engine.CurrentMinute.Value + WorldCalendar.MinutesPerHour, dwelling.VisitDwellEndsMinute);
        Assert.Equal(fixture.VisitorId, Assert.Single(dwelling.CitizenIds));

        engine.AdvanceUntil(new WorldMinute(dwelling.VisitDwellEndsMinute!.Value));
        var returning = Assert.Single(PrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        Assert.Equal(MigrationVisitPhase.Returning, returning.VisitPhase);
        Assert.True(returning.Returning);
        Assert.Equal(1, returning.DestinationSettlementId);

        engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + returning.RemainingPathCost));
        var completed = engine.CreatePersistenceSnapshot();
        Assert.Empty(completed.MigrationState!.InTransitParties);
        Assert.Equal(0, completed.MigrationState.LastVisitAttemptYear);
        Assert.Equal(visitorResidence, completed.MigrationState.CitizenResidences.Single(x => x.EntityId == fixture.VisitorId));
        Assert.Equal(householdResidence, completed.MigrationState.HouseholdResidences.Single(x => x.EntityId == householdId));
        Assert.Equal(visitorHome, completed.Citizens.Single(x => x.Id == visitor.Id).HomeStructureId);
        Assert.Equal(originFood, completed.Settlement!.FoodStored);
        MigrationValidation.Validate(completed);
    }

    [Fact]
    public void VisitorDeathClearsVisitPartyAndRecoversUnspentProvisions()
    {
        var (fixture, visitor, _) = StartOutboundVisit(new WorldSeed(1401));
        var engine = fixture.Engine;
        var party = Assert.Single(PrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        var routeLocation = visitor.Location;
        var provisions = party.Cargo.Sum(x => x.Quantity);

        InvokePrivate(engine, "KillNatural", visitor);
        InvokePrivate(engine, "SynchronizeLivingPeople");

        var snapshot = engine.CreatePersistenceSnapshot();
        Assert.False(snapshot.Citizens.Single(x => x.Id.Value == fixture.VisitorId).IsAlive);
        Assert.Empty(snapshot.MigrationState!.InTransitParties);
        Assert.Contains(snapshot.Economy!.Recoverable, x => x.Location == routeLocation &&
            x.Resource == ResourceType.Food && x.Quantity == provisions);
        Assert.DoesNotContain(snapshot.HistoricalEvents, x => x.EventType == HistoricalEventType.FamilyVisitReturned);
    }

    [Fact]
    public void NearThresholdHungerPausesDwellForMealThenResumesSameDeadline()
    {
        var (fixture, visitor, _) = StartOutboundVisit(new WorldSeed(1401));
        var engine = fixture.Engine;
        visitor.Location = fixture.DaughterSite;
        visitor.CurrentAction = CitizenAction.Explore;
        visitor.ActionTarget = fixture.DaughterSite;
        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", visitor)!);
        var dwell = Assert.Single(PrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        Assert.Equal(MigrationVisitPhase.Dwell, dwell.VisitPhase);
        var dwellEnd = dwell.VisitDwellEndsMinute;

        visitor.Needs = new CitizenNeeds(hunger: 8_499);
        visitor.NeedsUpdatedMinute = engine.CurrentMinute.Value;
        Assert.False((bool)InvokePrivate(engine, "TryPauseMigrationVisitForMeal", visitor)!);

        visitor.Needs = new CitizenNeeds(hunger: 8_500);
        visitor.NeedsUpdatedMinute = engine.CurrentMinute.Value;
        Assert.True((bool)InvokePrivate(engine, "TryPauseMigrationVisitForMeal", visitor)!);
        Assert.Equal(CitizenAction.Eat, visitor.CurrentAction);
        var eatCompletes = visitor.ActionCompletesMinute!.Value;
        var provisionsBeforeMeal = dwell.Cargo.Sum(x => x.Quantity);

        engine.AdvanceUntil(eatCompletes);

        var resumed = Assert.Single(PrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        Assert.Equal(MigrationVisitPhase.Dwell, resumed.VisitPhase);
        Assert.Equal(dwellEnd, resumed.VisitDwellEndsMinute);
        Assert.Equal(provisionsBeforeMeal - CitizenSimulationRules.MealFoodUnits,
            resumed.Cargo.Sum(x => x.Quantity));
        Assert.Equal(CitizenAction.Idle, visitor.CurrentAction);
        Assert.Equal(CitizenActionPhase.Perform, visitor.ActionPhase);
        Assert.True(visitor.Needs.Hunger < 8_500);
        Assert.Equal(dwellEnd, visitor.ActionCompletesMinute?.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableRelativeBeforeOutboundArrivalCausesReturnWithoutDwell(bool relativeMoves)
    {
        var (fixture, visitor, relative) = StartOutboundVisit(new WorldSeed(1401));
        var engine = fixture.Engine;
        if (relativeMoves)
        {
            var state = PrivateField<MigrationWorldState>(engine, "_migrationState");
            var householdId = relative.HouseholdId!.Value.Value;
            var movedIds = PrivateField<Dictionary<long, Citizen>>(engine, "_citizens").Values
                .Where(x => x.HouseholdId?.Value == householdId).Select(x => x.Id.Value).ToHashSet();
            foreach (var member in PrivateField<Dictionary<long, Citizen>>(engine, "_citizens").Values
                         .Where(x => movedIds.Contains(x.Id.Value)))
            {
                member.Location = engine.World.StartingSite;
                member.HomeStructureId = null;
            }
            SetPrivateField(engine, "_migrationState", new MigrationWorldState(state.Version,
                state.CitizenResidences.Select(x => movedIds.Contains(x.EntityId)
                    ? new MigrationEntityResidence(x.EntityId, 1) : x).ToArray(),
                state.HouseholdResidences.Select(x => x.EntityId == householdId
                    ? new MigrationEntityResidence(x.EntityId, 1) : x).ToArray(),
                state.StructureOwners, state.FacilityOwners, state.WorkOrderOwners, state.DaughterSettlement,
                state.InTransitParties, state.FoundingPressure, state.LastRelocations, state.LastVisitAttemptYear));
        }
        else
        {
            InvokePrivate(engine, "KillNatural", relative);
            InvokePrivate(engine, "SynchronizeLivingPeople");
        }

        visitor.Location = fixture.DaughterSite;
        visitor.CurrentAction = CitizenAction.Explore;
        visitor.ActionTarget = fixture.DaughterSite;
        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", visitor)!);
        var returning = Assert.Single(PrivateField<MigrationWorldState>(engine, "_migrationState").InTransitParties);
        Assert.Equal(MigrationVisitPhase.Returning, returning.VisitPhase);
        Assert.Equal(engine.World.StartingSite, returning.DestinationSite);
        Assert.Equal(1, PrivateField<MigrationWorldState>(engine, "_migrationState")
            .CitizenResidences.Single(x => x.EntityId == visitor.Id.Value).SettlementId);

        visitor.Location = engine.World.StartingSite;
        visitor.CurrentAction = CitizenAction.Explore;
        visitor.ActionTarget = engine.World.StartingSite;
        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", visitor)!);

        var completed = engine.CreatePersistenceSnapshot();
        Assert.Empty(completed.MigrationState!.InTransitParties);
        Assert.Equal(1, completed.MigrationState.CitizenResidences
            .Single(x => x.EntityId == visitor.Id.Value).SettlementId);
        var returned = Assert.Single(completed.HistoricalEvents,
            x => x.EventType == HistoricalEventType.FamilyVisitReturned);
        Assert.Equal(HistoricalEventPayloads.FamilyVisitReturned(fixture.VisitorId, fixture.RelativeId,
            1, MigrationDaughterSettlementState.SettlementId), returned.PayloadJson);
    }

    private static (VisitFixture Fixture, Citizen Visitor, Citizen Relative) StartOutboundVisit(WorldSeed seed)
    {
        var fixture = CreateFixture(seed);
        var citizens = PrivateField<Dictionary<long, Citizen>>(fixture.Engine, "_citizens");
        citizens[fixture.VisitorId].ParentAId = new CitizenId(fixture.ParentId);
        citizens[fixture.RelativeId].ParentAId = new CitizenId(fixture.ParentId);
        InvokePrivate(fixture.Engine, "EvaluateMigrationVisits");
        var party = Assert.Single(PrivateField<MigrationWorldState>(fixture.Engine, "_migrationState").InTransitParties);
        Assert.Equal(fixture.VisitorId, Assert.Single(party.CitizenIds));
        var visitor = citizens[fixture.VisitorId];
        var relative = citizens[fixture.RelativeId];
        visitor.ParentAId = null;
        relative.ParentAId = null;
        return (fixture, visitor, relative);
    }

    private static void SetPrivateField(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private static VisitFixture CreateFixture(WorldSeed seed)
    {
        var baseline = new SimulationEngine(seed,
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion).CreatePersistenceSnapshot();
        var world = baseline.World!;
        var households = baseline.Households.Where(x => x.DissolvedMinute is null)
            .Select(household => (Household: household, Member: baseline.Citizens.Where(x => x.IsAlive && x.HouseholdId == household.Id)
                .OrderBy(x => x.Id.Value).FirstOrDefault()))
            .Where(x => x.Member is not null).OrderBy(x => x.Member!.Id.Value).ToArray();
        var pair = households.SelectMany(visitor => households.Where(relative => relative.Household.Id != visitor.Household.Id &&
                visitor.Member!.Id.Value < relative.Member!.Id.Value)
            .Select(relative => (Visitor: visitor, Relative: relative))).First();
        var visitorId = pair.Visitor.Member!.Id.Value;
        var relativeId = pair.Relative.Member!.Id.Value;
        var parentId = baseline.Citizens.Where(x => x.Id.Value > visitorId && x.Id.Value != relativeId && x.IsAlive)
            .OrderBy(x => x.Id.Value).First(x => x.HouseholdId != pair.Relative.Household.Id).Id.Value;
        var travelCosts = LivingTravelCosts.Compute(world, world.StartingSite);
        var daughterSite = world.Tiles.Where(x => x.Coordinate != world.StartingSite && x.Walkable &&
                travelCosts.ContainsKey(x.Coordinate))
            .OrderBy(x => travelCosts[x.Coordinate]).ThenBy(x => x.Coordinate.Y).ThenBy(x => x.Coordinate.X)
            .First().Coordinate;
        var relativeHouseholdId = pair.Relative.Household.Id.Value;
        foreach (var citizen in baseline.Citizens.Where(x => x.HouseholdId?.Value == relativeHouseholdId))
        {
            citizen.Location = daughterSite;
            citizen.HomeStructureId = null;
        }

        var state = baseline.MigrationState!;
        var migration = new MigrationWorldState(state.Version,
            baseline.Citizens.Select(x => new MigrationEntityResidence(x.Id.Value,
                x.HouseholdId?.Value == relativeHouseholdId ? 2 : 1)).ToArray(),
            baseline.Households.Select(x => new MigrationEntityResidence(x.Id.Value,
                x.Id.Value == relativeHouseholdId ? 2 : 1)).ToArray(),
            state.StructureOwners, state.FacilityOwners, state.WorkOrderOwners,
            new MigrationDaughterSettlementState(daughterSite,
                new MigrationSettlementStockState(0, 0, 0, EconomyRules.FoundingStorageCapacity,
                    baseline.WorldMinute.Value, baseline.WorldMinute.Value),
                Enum.GetValues<LivingGood>().Select(x => new LivingStock(x, 0)).ToArray()));
        var snapshot = CopySnapshot(baseline, migration.ToCanonicalJson(), baseline.Citizens);
        return new VisitFixture(SimulationEngine.FromPersistenceSnapshot(snapshot), visitorId, relativeId, parentId,
            daughterSite);
    }

    private static SimulationPersistenceSnapshot CopySnapshot(SimulationPersistenceSnapshot snapshot,
        string migrationStateJson, IReadOnlyList<Citizen> citizens) => new(snapshot.Seed, snapshot.WorldMinute,
        snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion,
        snapshot.WorldConfiguration, snapshot.Counters, snapshot.ScheduledEvents, snapshot.World, citizens,
        snapshot.CitizenGenerationVersion, snapshot.ResourceStates, snapshot.Settlement, snapshot.SurvivalVersion,
        snapshot.SettlementVersion, snapshot.Structures, snapshot.StructureContributions, snapshot.SocialVersion,
        snapshot.Relationships, snapshot.Households, snapshot.HistoryVersion, snapshot.HistoryState,
        snapshot.HistoricalEvents, snapshot.HistoricalEventCitizens, snapshot.HistoricalEventStructures,
        snapshot.StatisticsSamples, snapshot.Memories, snapshot.Agriculture, snapshot.Economy,
        snapshot.LivingStateJson, migrationStateJson);

    private static T PrivateField<T>(object instance, string name) =>
        (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException($"Could not read private field '{name}'."));

    private static object? InvokePrivate(object instance, string name, params object?[] arguments)
    {
        var method = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(candidate => candidate.Name == name && candidate.GetParameters().Length == arguments.Length)
            ?? throw new InvalidOperationException($"Could not find private method '{name}'.");
        return method.Invoke(instance, arguments);
    }

    private sealed record VisitFixture(SimulationEngine Engine, long VisitorId, long RelativeId, long ParentId,
        TileCoordinate DaughterSite);
}
