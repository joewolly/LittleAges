using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M14MigrationHistoryTests
{
    [Fact]
    public void FoundingHistoryRecordsDepartureThenSuccessfulSettlement()
    {
        var fixture = CreateDepartedFoundingEngine(new WorldSeed(1421));
        var engine = fixture.Engine;
        var party = Assert.Single(MigrationState(engine).InTransitParties);
        var departure = AssertEvent(engine, HistoricalEventType.ExpeditionDeparted,
            HistoricalImportance.Notable, engine.World.StartingSite,
            HistoricalEventPayloads.ExpeditionDeparted(party.Id, party.HouseholdId, party.OriginSettlementId,
                party.DestinationSite.X, party.DestinationSite.Y, party.CitizenIds.Count));
        Assert.Equal(party.CitizenIds.Order(), EventLinks(engine, departure, "participant").Select(x => x.CitizenId.Value).Order());
        Assert.DoesNotContain(Events(engine), x => x.EventType is HistoricalEventType.DaughterSettlementFounded or
            HistoricalEventType.ExpeditionReturned or HistoricalEventType.ExpeditionLost);

        var citizens = Citizens(engine);
        var survivors = party.CitizenIds.Select(id => citizens[id]).Where(x => x.IsAlive).OrderBy(x => x.Id.Value).ToArray();
        foreach (var survivor in survivors)
        {
            survivor.Location = party.DestinationSite;
            survivor.CurrentAction = CitizenAction.Explore;
            survivor.ActionTarget = party.DestinationSite;
        }

        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", survivors[0])!);
        var founded = AssertEvent(engine, HistoricalEventType.DaughterSettlementFounded,
            HistoricalImportance.Historic, party.DestinationSite,
            HistoricalEventPayloads.DaughterSettlementFounded(MigrationDaughterSettlementState.SettlementId,
                party.Id, party.HouseholdId, survivors.Length));
        Assert.Equal(survivors.Select(x => x.Id.Value).Order(), EventLinks(engine, founded, "founder").Select(x => x.CitizenId.Value).Order());
        Assert.Single(Events(engine), x => x.EventType == HistoricalEventType.ExpeditionDeparted);
        Assert.Equal(departure.Id.Value + 1, founded.Id.Value);
    }

    [Fact]
    public void FoundingReturnAndLossAreEmittedOnlyWhenThePartyResolves()
    {
        var returning = CreateDepartedFoundingEngine(new WorldSeed(1422));
        var returnEngine = returning.Engine;
        var returnParty = Assert.Single(MigrationState(returnEngine).InTransitParties);
        var departure = Assert.Single(Events(returnEngine), x => x.EventType == HistoricalEventType.ExpeditionDeparted);
        Assert.DoesNotContain(Events(returnEngine), x => x.EventType == HistoricalEventType.ExpeditionReturned);
        var origin = (TileCoordinate)InvokePrivate(returnEngine, "SiteLocation", returnParty.OriginSettlementId)!;
        foreach (var id in returnParty.CitizenIds.Where(id => Citizens(returnEngine)[id].IsAlive))
            Citizens(returnEngine)[id].Location = origin;

        InvokePrivate(returnEngine, "BeginFoundingPartyReturn", returnParty);
        var returned = AssertEvent(returnEngine, HistoricalEventType.ExpeditionReturned,
            HistoricalImportance.Notable, origin, departure.PayloadJson);
        var survivingIds = returnParty.CitizenIds.Select(id => Citizens(returnEngine)[id])
            .Where(x => x.IsAlive).Select(x => x.Id.Value).Order();
        Assert.Equal(survivingIds, EventLinks(returnEngine, returned, "participant").Select(x => x.CitizenId.Value).Order());
        Assert.Empty(MigrationState(returnEngine).InTransitParties);

        var lostFixture = CreateDepartedFoundingEngine(new WorldSeed(1423));
        var lostEngine = lostFixture.Engine;
        var lostParty = Assert.Single(MigrationState(lostEngine).InTransitParties);
        var lostDeparture = Assert.Single(Events(lostEngine), x => x.EventType == HistoricalEventType.ExpeditionDeparted);
        var lastPartyLocation = lostParty.Location;
        for (var index = 0; index < lostParty.CitizenIds.Count; index++)
        {
            var id = lostParty.CitizenIds[index];
            InvokePrivate(lostEngine, "KillNatural", Citizens(lostEngine)[id]);
            InvokePrivate(lostEngine, "SynchronizeLivingPeople");
            if (index + 1 < lostParty.CitizenIds.Count)
                lastPartyLocation = Assert.Single(MigrationState(lostEngine).InTransitParties).Location;
        }

        var lost = AssertEvent(lostEngine, HistoricalEventType.ExpeditionLost,
            HistoricalImportance.Notable, lastPartyLocation, lostDeparture.PayloadJson);
        Assert.Equal(lostParty.CitizenIds.Order(), EventLinks(lostEngine, lost, "participant").Select(x => x.CitizenId.Value).Order());
        Assert.Empty(MigrationState(lostEngine).InTransitParties);
        Assert.DoesNotContain(Events(lostEngine), x => x.EventType == HistoricalEventType.ExpeditionReturned);
    }

    [Fact]
    public void RelocationHistoryFollowsResidenceAndPrivateCargoTransfer()
    {
        var fixture = CreateTwoSiteFixture(new WorldSeed(1424));
        var engine = fixture.Engine;
        var state = MigrationState(engine);
        var household = engine.Households.Where(x => x.DissolvedMinute is null)
            .OrderBy(x => x.Id.Value).First(x => state.HouseholdResidences.Single(r => r.EntityId == x.Id.Value).SettlementId == 1);
        var members = Citizens(engine).Values.Where(x => x.IsAlive && x.HouseholdId == household.Id)
            .OrderBy(x => x.Id.Value).ToArray();
        Assert.NotEmpty(members);

        var cargo = new[]
        {
            new MigrationCargoStackState(8_400_002, MigrationCargoGood.Food, 7, MigrationCargoPurpose.Provisions),
            new MigrationCargoStackState(8_400_003, MigrationCargoGood.Wood, 3, MigrationCargoPurpose.Cargo)
        };
        var party = new MigrationTransitPartyState(8_400_001, household.Id.Value, 1, 2,
            engine.World.StartingSite, fixture.DaughterSite, members.Select(x => x.Id.Value).ToArray(), cargo,
            1, engine.CurrentMinute.Value, journeyKind: MigrationJourneyKind.Relocation);
        InvokePrivate(engine, "SetMigrationParty", party);
        foreach (var member in members)
        {
            member.Location = fixture.DaughterSite;
            member.CurrentAction = CitizenAction.Explore;
            member.ActionTarget = fixture.DaughterSite;
        }

        InvokePrivate(engine, "CompleteHouseholdRelocation", party, 2L, members);

        var relocated = AssertEvent(engine, HistoricalEventType.HouseholdRelocated,
            HistoricalImportance.Notable, fixture.DaughterSite,
            HistoricalEventPayloads.HouseholdRelocated(household.Id.Value, 1, 2, members.Length));
        Assert.Equal(members.Select(x => x.Id.Value).Order(), EventLinks(engine, relocated, "member").Select(x => x.CitizenId.Value).Order());
        Assert.Equal(2, MigrationState(engine).HouseholdResidences.Single(x => x.EntityId == household.Id.Value).SettlementId);
        Assert.Equal(3, (long)InvokePrivate(engine, "AvailablePrivate", household.Id.Value, ResourceType.Wood)!);
        Assert.DoesNotContain(Events(engine), x => x.EventType is HistoricalEventType.ExpeditionDeparted or
            HistoricalEventType.ExpeditionReturned or HistoricalEventType.ExpeditionLost);
    }

    [Fact]
    public void FamilyVisitHistoryKeepsItsOriginalRelativeLinksThroughReturn()
    {
        var fixture = CreateTwoSiteFixture(new WorldSeed(1425));
        var engine = fixture.Engine;
        var visitor = Citizens(engine).Values.Where(x => x.IsAlive && x.AgeYears(engine.CurrentMinute) >= 18)
            .OrderBy(x => x.Id.Value).First(x => MigrationState(engine).CitizenResidences
                .Single(r => r.EntityId == x.Id.Value).SettlementId == 1);
        var relative = Citizens(engine).Values.Where(x => x.IsAlive && x.Id != visitor.Id &&
                x.AgeYears(engine.CurrentMinute) >= 18)
            .OrderBy(x => x.Id.Value).First(x => MigrationState(engine).CitizenResidences
                .Single(r => r.EntityId == x.Id.Value).SettlementId == 2);
        var parent = Citizens(engine).Values.First(x => x.IsAlive && x.Id != visitor.Id && x.Id != relative.Id);
        visitor.ParentAId = parent.Id;
        relative.ParentAId = parent.Id;
        InvokePrivate(engine, "EvaluateMigrationVisits");
        var outbound = Assert.Single(MigrationState(engine).InTransitParties);
        Assert.Equal(MigrationJourneyKind.Visit, outbound.JourneyKind);
        var outboundVisitor = Citizens(engine)[Assert.Single(outbound.CitizenIds)];
        var outboundRelative = Citizens(engine)[outbound.VisitRelativeId!.Value];
        var departed = AssertEvent(engine, HistoricalEventType.FamilyVisitDeparted,
            HistoricalImportance.Personal, outbound.Location,
            HistoricalEventPayloads.FamilyVisitDeparted(outboundVisitor.Id.Value, outboundRelative.Id.Value,
                outbound.OriginSettlementId, outbound.DestinationSettlementId!.Value));
        Assert.Equal(new[]
            {
                (CitizenId: outboundVisitor.Id.Value, Role: "subject"),
                (CitizenId: outboundRelative.Id.Value, Role: "participant")
            }
                .OrderBy(x => x.Role, StringComparer.Ordinal),
            EventLinks(engine, departed).Select(x => (CitizenId: x.CitizenId.Value, Role: x.Role))
                .OrderBy(x => x.Role, StringComparer.Ordinal));
        Assert.DoesNotContain(Events(engine), x => x.EventType == HistoricalEventType.FamilyVisitReturned);

        engine.AdvanceUntil(new WorldMinute(outbound.DepartedMinute + outbound.RemainingPathCost));
        var dwelling = Assert.Single(MigrationState(engine).InTransitParties);
        Assert.Equal(MigrationVisitPhase.Dwell, dwelling.VisitPhase);
        InvokePrivate(engine, "KillNatural", outboundRelative);
        engine.AdvanceUntil(new WorldMinute(dwelling.VisitDwellEndsMinute!.Value));
        var returning = Assert.Single(MigrationState(engine).InTransitParties);
        Assert.Equal(MigrationVisitPhase.Returning, returning.VisitPhase);
        Assert.Equal(outbound.CitizenIds, returning.CitizenIds);
        Assert.Equal(outbound.VisitRelativeId, returning.VisitRelativeId);
        engine.AdvanceUntil(new WorldMinute(engine.CurrentMinute.Value + returning.RemainingPathCost));

        var returned = AssertEvent(engine, HistoricalEventType.FamilyVisitReturned,
            HistoricalImportance.Personal,
            (TileCoordinate)InvokePrivate(engine, "SiteLocation", returning.OriginSettlementId)!,
            departed.PayloadJson);
        Assert.Equal(EventLinks(engine, departed).Select(x => (CitizenId: x.CitizenId.Value, Role: x.Role)),
            EventLinks(engine, returned).Select(x => (CitizenId: x.CitizenId.Value, Role: x.Role)));
        Assert.False(Citizens(engine)[outboundRelative.Id.Value].IsAlive);
        Assert.Empty(MigrationState(engine).InTransitParties);
    }

    private static (SimulationEngine Engine, Household Household, Citizen[] Members) CreateDepartedFoundingEngine(WorldSeed seed)
    {
        var engine = new SimulationEngine(seed,
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
        var (household, members) = EnsureTwoMemberHousehold(engine);
        SetCurrentMinute(engine, new WorldMinute(5L * WorldCalendar.MinutesPerYear));
        var provision = FoundingProvisionFood(engine.CurrentMinute.Value);
        InvokePrivate(engine, "AddPrivate", household.Id.Value, ResourceType.Food,
            checked(provision * members.Length));

        var state = MigrationState(engine);
        var seasonMinutes = (long)WorldCalendar.DaysPerSeason * WorldCalendar.MinutesPerDay;
        var pressure = engine.Households.Where(x => x.DissolvedMinute is null)
            .Select(x => new MigrationFoundingPressureState(x.Id.Value, engine.CurrentMinute.Value - seasonMinutes)).ToArray();
        SetPrivateField(engine, "_migrationState", new MigrationWorldState(state.Version,
            state.CitizenResidences, state.HouseholdResidences, state.StructureOwners, state.FacilityOwners,
            state.WorkOrderOwners, state.DaughterSettlement, state.InTransitParties, pressure,
            state.LastRelocations, state.LastVisitAttemptYear));

        InvokePrivate(engine, "EvaluateMigrationFounding");
        Assert.Single(MigrationState(engine).InTransitParties);
        return (engine, household, members);
    }

    private static (SimulationEngine Engine, TileCoordinate DaughterSite) CreateTwoSiteFixture(WorldSeed seed)
    {
        var baseline = new SimulationEngine(seed,
            simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion).CreatePersistenceSnapshot();
        var activeHouseholds = baseline.Households.Where(x => x.DissolvedMinute is null)
            .OrderBy(x => x.Id.Value).ToArray();
        var daughterHouseholdId = activeHouseholds[0].Id.Value;
        var world = baseline.World!;
        var costs = LivingTravelCosts.Compute(world, world.StartingSite);
        var daughterSite = world.Tiles.Where(x => x.Coordinate != world.StartingSite && x.Walkable && costs.ContainsKey(x.Coordinate))
            .OrderBy(x => costs[x.Coordinate]).ThenBy(x => x.Coordinate.Y).ThenBy(x => x.Coordinate.X)
            .First().Coordinate;
        var citizens = baseline.Citizens.Select(citizen =>
        {
            if (citizen.HouseholdId?.Value == daughterHouseholdId)
            {
                citizen.Location = daughterSite;
                citizen.HomeStructureId = null;
            }
            return citizen;
        }).ToArray();
        var current = baseline.MigrationState!;
        var migration = new MigrationWorldState(current.Version,
            citizens.Select(x => new MigrationEntityResidence(x.Id.Value,
                x.HouseholdId?.Value == daughterHouseholdId ? 2 : 1)).ToArray(),
            baseline.Households.Select(x => new MigrationEntityResidence(x.Id.Value,
                x.Id.Value == daughterHouseholdId ? 2 : 1)).ToArray(),
            current.StructureOwners, current.FacilityOwners, current.WorkOrderOwners,
            new MigrationDaughterSettlementState(daughterSite,
                new MigrationSettlementStockState(0, 0, 0, EconomyRules.FoundingStorageCapacity,
                    baseline.WorldMinute.Value, baseline.WorldMinute.Value),
                Enum.GetValues<LivingGood>().Select(x => new LivingStock(x, 0)).ToArray()));
        var snapshot = CopySnapshot(baseline, migration.ToCanonicalJson(), citizens);
        return (SimulationEngine.FromPersistenceSnapshot(snapshot), daughterSite);
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

    private static (Household Household, Citizen[] Members) EnsureTwoMemberHousehold(SimulationEngine engine)
    {
        var households = PrivateField<Dictionary<long, Household>>(engine, "_households");
        var citizens = Citizens(engine);
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
        var economicMembers = PrivateField<SortedDictionary<long, EconomicMember>>(engine, "_economicMembers");
        economicMembers[additional.Id.Value] = economicMembers[additional.Id.Value] with { HouseholdId = household.Id.Value };
        return (household, citizens.Values.Where(x => x.IsAlive && x.HouseholdId == household.Id)
            .OrderBy(x => x.Id.Value).ToArray());
    }

    private static HistoricalEvent AssertEvent(SimulationEngine engine, HistoricalEventType type,
        HistoricalImportance importance, TileCoordinate location, string payload)
    {
        var item = Assert.Single(Events(engine), x => x.EventType == type);
        Assert.Equal(importance, item.Importance);
        Assert.Equal(HistoricalEventOrigin.Live, item.Origin);
        Assert.True(location == item.Location,
            $"{type} event location {item.Location} differs from expected {location}; payload={item.PayloadJson}; " +
            $"startingSite={engine.World.StartingSite}; daughterSite={MigrationState(engine).DaughterSettlement?.Site}");
        Assert.Equal(payload, item.PayloadJson);
        return item;
    }

    private static List<HistoricalEvent> Events(SimulationEngine engine) => PrivateField<List<HistoricalEvent>>(engine, "_historicalEvents");
    private static List<HistoricalEventCitizenLink> CitizenLinks(SimulationEngine engine) =>
        PrivateField<List<HistoricalEventCitizenLink>>(engine, "_historicalEventCitizens");
    private static MigrationWorldState MigrationState(SimulationEngine engine) => PrivateField<MigrationWorldState>(engine, "_migrationState");
    private static Dictionary<long, Citizen> Citizens(SimulationEngine engine) => PrivateField<Dictionary<long, Citizen>>(engine, "_citizens");
    private static HistoricalEventCitizenLink[] EventLinks(SimulationEngine engine, HistoricalEvent item, string? role = null) =>
        CitizenLinks(engine).Where(x => x.HistoricalEventId == item.Id && (role is null || x.Role == role)).ToArray();

    private static void SetCurrentMinute(SimulationEngine engine, WorldMinute minute) =>
        engine.GetType().GetProperty(nameof(SimulationEngine.CurrentMinute))!.SetValue(engine, minute);

    private static long FoundingProvisionFood(long departureMinute)
    {
        var seasonOffset = departureMinute % WorldCalendar.MinutesPerYear;
        var seasonIndex = seasonOffset / ((long)WorldCalendar.DaysPerSeason * WorldCalendar.MinutesPerDay);
        var rate = seasonIndex == 3 ? NeedsProjection.WinterHungerRatePerMinute : NeedsProjection.HungerRatePerMinute;
        var hungerPoints = checked((long)rate * 14 * WorldCalendar.MinutesPerDay);
        return checked((hungerPoints * CitizenSimulationRules.MealFoodUnits +
            CitizenSimulationRules.FullHungerReduction - 1) / CitizenSimulationRules.FullHungerReduction);
    }

    private static void SetPrivateField(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private static T PrivateField<T>(object instance, string name) =>
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
}
