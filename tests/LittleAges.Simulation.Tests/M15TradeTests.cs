using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M15TradeTests
{
    private const string Roads = SimulationEngine.RoadsSimulationRulesVersion;
    private static readonly MigrationCargoGood[] Traded =
    [
        MigrationCargoGood.Food, MigrationCargoGood.Wood, MigrationCargoGood.Stone, MigrationCargoGood.Grain,
        MigrationCargoGood.PreservedFood, MigrationCargoGood.Fuel, MigrationCargoGood.Tool,
        MigrationCargoGood.Clothing, MigrationCargoGood.Medicine
    ];

    [Theory]
    [InlineData(MigrationCargoGood.Wood, MigrationCargoGood.Tool, 15, 1)]
    [InlineData(MigrationCargoGood.Stone, MigrationCargoGood.Fuel, 1, 1)]
    [InlineData(MigrationCargoGood.Fuel, MigrationCargoGood.Medicine, 4, 1)]
    [InlineData(MigrationCargoGood.PreservedFood, MigrationCargoGood.Stone, 2, 3)]
    [InlineData(MigrationCargoGood.Clothing, MigrationCargoGood.Tool, 5, 4)]
    public void LotsAreTheSmallestEqualValueExchange(MigrationCargoGood outbound, MigrationCargoGood returned,
        int outboundLot, int returnLot)
    {
        Assert.Equal((outboundLot, returnLot), SimulationEngine.TradeLot(outbound, returned));
        Assert.Equal(outboundLot * SimulationEngine.TradeValue(outbound), returnLot * SimulationEngine.TradeValue(returned));
    }

    [Fact]
    public void LoadGrowsWithTheShareOfTrailAndRoadAlongTheRoute()
    {
        var route = Enumerable.Range(0, 5).Select(x => new TileCoordinate(x, 0)).ToArray();
        Assert.Equal(30, SimulationEngine.TradeLoadFor(route, _ => RoadGrade.Track));
        Assert.Equal(90, SimulationEngine.TradeLoadFor(route, _ => RoadGrade.Road));
        // Two of four steps are trail and one of those is road: 30 + 30 * 2 / 4 + 30 * 1 / 4, rounding down.
        Assert.Equal(52, SimulationEngine.TradeLoadFor(route, x => x.X switch
        {
            1 => RoadGrade.Trail,
            2 => RoadGrade.Road,
            _ => RoadGrade.None
        }));
    }

    [Fact]
    public void TradersCarrySurplusOutExchangeWholeLotsAndBringTheReturnGoodHome()
    {
        var engine = SimulationEngine.FromPersistenceSnapshot(CreateStockedSnapshot(new WorldSeed(1501), depart: false));
        var totals = Totals(engine);
        InvokePrivate(engine, "EvaluateMigrationTrade");

        var parties = MigrationState(engine).InTransitParties;
        Assert.NotEmpty(parties);
        Assert.All(parties, x =>
        {
            Assert.Equal(MigrationJourneyKind.Trade, x.JourneyKind);
            Assert.Equal(MigrationVisitPhase.Outbound, x.VisitPhase);
        });
        Assert.Equal(parties.Count, parties.Select(x => x.OriginSettlementId).Distinct().Count());
        Assert.Equal(totals, Totals(engine));
        var party = parties.Single(x => x.OriginSettlementId == 1);
        var trader = Citizens(engine)[Assert.Single(party.CitizenIds)];
        var outbound = Assert.Single(party.Cargo, x => x.Purpose == MigrationCargoPurpose.Cargo);
        var (outboundLot, returnLot) = SimulationEngine.TradeLot(outbound.Good, party.TradeReturnGood!.Value);
        Assert.Equal(0, outbound.Quantity % outboundLot);
        Assert.InRange(outbound.Quantity, outboundLot, party.TradeLoad!.Value);
        var departed = Assert.Single(Events(engine), x => x.EventType == HistoricalEventType.TradeDeparted &&
            x.PayloadJson.Contains($"\"partyId\":\"{party.Id}\"", StringComparison.Ordinal));
        Assert.Equal(HistoricalImportance.Routine, departed.Importance);
        Assert.Equal(HistoricalEventPayloads.TradeDeparted(party.Id, trader.Id.Value, 1, 2, outbound.Good,
            checked((int)outbound.Quantity), party.TradeReturnGood.Value), departed.PayloadJson);
        var bestHauler = Citizens(engine).Values.Where(x => x.IsAlive && x.AgeYears(engine.CurrentMinute) >= 18 &&
                MigrationState(engine).CitizenResidences.Single(r => r.EntityId == x.Id.Value).SettlementId == 1)
            .Max(x => x.Skills.Hauling);
        Assert.Equal(bestHauler, trader.Skills.Hauling);

        Arrive(trader, party.DestinationSite);
        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", trader)!);
        var dwell = PartyFrom(engine, 1);
        Assert.Equal(MigrationVisitPhase.Dwell, dwell.VisitPhase);
        Assert.Equal(engine.CurrentMinute.Value + WorldCalendar.MinutesPerHour, dwell.VisitDwellEndsMinute);

        InvokePrivate(engine, "EndMigrationVisitDwell", trader, dwell);
        var returning = PartyFrom(engine, 1);
        Assert.Equal(MigrationVisitPhase.Returning, returning.VisitPhase);
        Assert.Equal(totals, Totals(engine));
        var bought = Assert.Single(returning.Cargo, x => x.Good == party.TradeReturnGood && x.Purpose == MigrationCargoPurpose.Cargo);
        var sold = outbound.Quantity - returning.Cargo.Where(x => x.Good == outbound.Good && x.Purpose == MigrationCargoPurpose.Cargo).Sum(x => x.Quantity);
        Assert.Equal(sold / outboundLot * returnLot, bought.Quantity);
        Assert.Equal(0, sold % outboundLot);
        Assert.True(returning.Cargo.Where(x => x.Purpose == MigrationCargoPurpose.Cargo).Sum(x => x.Quantity) <= returning.TradeLoad);
        var completed = Assert.Single(Events(engine), x => x.EventType == HistoricalEventType.TradeCompleted);
        Assert.Equal(HistoricalEventPayloads.TradeCompleted(party.Id, trader.Id.Value, 1, 2, outbound.Good,
            checked((int)sold), party.TradeReturnGood.Value, checked((int)bought.Quantity)), completed.PayloadJson);

        var homeBefore = (long)InvokePrivate(engine, "TradeStock", 1L, party.TradeReturnGood.Value)!;
        Arrive(trader, engine.World.StartingSite);
        Assert.True((bool)InvokePrivate(engine, "HandleFoundingPartyArrival", trader)!);
        Assert.DoesNotContain(MigrationState(engine).InTransitParties, x => x.OriginSettlementId == 1);
        Assert.Equal(homeBefore + bought.Quantity, (long)InvokePrivate(engine, "TradeStock", 1L, party.TradeReturnGood.Value)!);
        Assert.Equal(totals, Totals(engine));
        var returned = Assert.Single(Events(engine), x => x.EventType == HistoricalEventType.TradeReturned);
        Assert.Equal(HistoricalEventPayloads.TradeReturned(party.Id, trader.Id.Value, 1, 2, exchanged: true), returned.PayloadJson);
        MigrationValidation.Validate(engine.CreatePersistenceSnapshot());
    }

    [Fact]
    public void ADeadTraderLeavesCommunalCargoWhereThePartyFell()
    {
        var engine = SimulationEngine.FromPersistenceSnapshot(CreateStockedSnapshot(new WorldSeed(1501), depart: true));
        var party = PartyFrom(engine, 1);
        var trader = Citizens(engine)[party.CitizenIds[0]];
        var recoverable = party.Cargo.Where(x => x.Good is MigrationCargoGood.Food or MigrationCargoGood.Wood or MigrationCargoGood.Stone)
            .Sum(x => x.Quantity);

        InvokePrivate(engine, "KillNatural", trader);
        InvokePrivate(engine, "SynchronizeLivingPeople");

        var snapshot = engine.CreatePersistenceSnapshot();
        Assert.DoesNotContain(snapshot.MigrationState!.InTransitParties, x => x.Id == party.Id);
        Assert.Equal(recoverable, snapshot.Economy!.Recoverable.Where(x => x.Location == party.Location && x.HouseholdId is null)
            .Sum(x => (long)x.Quantity));
        var lost = Assert.Single(snapshot.HistoricalEvents, x => x.EventType == HistoricalEventType.TradeLost);
        Assert.Equal(HistoricalImportance.Notable, lost.Importance);
        Assert.Equal(HistoricalEventPayloads.TradeLost(party.Id, trader.Id.Value, 1, 2), lost.PayloadJson);
        Assert.DoesNotContain(snapshot.HistoricalEvents, x => x.EventType == HistoricalEventType.TradeReturned);
        MigrationValidation.Validate(snapshot);
    }

    [Fact]
    public void GrainLostWithATraderSpoilsSoCommunalGrainStaysConserved()
    {
        var engine = SimulationEngine.FromPersistenceSnapshot(CreateStockedSnapshot(new WorldSeed(1501), depart: true));
        var living = PrivateField<LivingWorldState>(engine, "_living");
        var party = PartyFrom(engine, 1);
        // The party is lost before anything validates it, so it may briefly carry grain outside the harvest accounting.
        var grain = new MigrationCargoStackState(party.Cargo.Max(x => x.Id) + 1_000, MigrationCargoGood.Grain, 10,
            MigrationCargoPurpose.Cargo);
        InvokePrivate(engine, "SetMigrationParty", new MigrationTransitPartyState(party.Id, party.HouseholdId,
            party.OriginSettlementId, party.DestinationSettlementId, party.Location, party.DestinationSite,
            party.CitizenIds, [.. party.Cargo, grain], party.RemainingPathCost, party.DepartedMinute,
            journeyKind: party.JourneyKind, visitPhase: party.VisitPhase, tradeReturnGood: party.TradeReturnGood,
            tradeLoad: party.TradeLoad));
        var spoiled = living.CommunalGrainSpoiled;

        InvokePrivate(engine, "KillNatural", Citizens(engine)[party.CitizenIds[0]]);
        InvokePrivate(engine, "SynchronizeLivingPeople");

        // The grain leaves transit and is counted as spoiled, so stock + transit + consumed + spoiled is unchanged.
        Assert.DoesNotContain(MigrationState(engine).InTransitParties, x => x.Id == party.Id);
        Assert.Equal(spoiled + 10, living.CommunalGrainSpoiled);
    }

    [Fact]
    public void TradePartiesDoNotHoldBackM14SystemsAndOnlyOneMayLeaveEachSite()
    {
        var start = CreateStockedSnapshot(new WorldSeed(1501), depart: true);
        var engine = SimulationEngine.FromPersistenceSnapshot(start);
        Assert.NotEmpty(MigrationState(engine).InTransitParties);
        Assert.False((bool)typeof(SimulationEngine).GetProperty("HasNonTradeMigrationParty",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!);

        // A second evaluation in the same month sends nobody new.
        var before = MigrationState(engine).InTransitParties.Select(x => x.Id).ToArray();
        InvokePrivate(engine, "EvaluateMigrationTrade");
        Assert.Equal(before, MigrationState(engine).InTransitParties.Select(x => x.Id));

        var party = start.MigrationState!.InTransitParties.First(x => x.OriginSettlementId == 1);
        var other = start.Citizens.Where(x => x.IsAlive && !party.CitizenIds.Contains(x.Id.Value) && x.HouseholdId is not null &&
                start.MigrationState.CitizenResidences.Single(r => r.EntityId == x.Id.Value).SettlementId == 1)
            .OrderBy(x => x.Id.Value).First();
        var duplicate = new MigrationTransitPartyState(party.Id + 1_000_000, other.HouseholdId!.Value.Value, 1, 2,
            other.Location, party.DestinationSite, [other.Id.Value], [], 1, party.DepartedMinute,
            journeyKind: MigrationJourneyKind.Trade, visitPhase: MigrationVisitPhase.Outbound,
            tradeReturnGood: party.TradeReturnGood, tradeLoad: party.TradeLoad);
        var state = start.MigrationState;
        var invalid = new MigrationWorldState(state.Version, state.CitizenResidences, state.HouseholdResidences,
            state.StructureOwners, state.FacilityOwners, state.WorkOrderOwners, state.DaughterSettlement,
            [.. state.InTransitParties, duplicate], state.FoundingPressure, state.LastRelocations,
            state.LastVisitAttemptYear, state.Roads);
        var error = Assert.Throws<ArgumentException>(() => MigrationValidation.Validate(CopySnapshot(start, invalid.ToCanonicalJson(), start.Citizens)));
        Assert.Contains("one trade party", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TradeJourneysReplayIdenticallyAcrossReopenInEveryPhase()
    {
        var start = CreateStockedSnapshot(new WorldSeed(1501), depart: true);
        var end = new WorldMinute(start.WorldMinute.Value + WorldCalendar.MinutesPerDay);
        var reference = SimulationEngine.FromPersistenceSnapshot(start);
        var checkpoints = new Dictionary<MigrationVisitPhase, WorldMinute>();
        for (var minute = start.WorldMinute.Value + 5; minute < end.Value && checkpoints.Count < 3; minute += 5)
        {
            reference.AdvanceUntil(new WorldMinute(minute));
            if (MigrationState(reference).InTransitParties.FirstOrDefault(x => x.OriginSettlementId == 1) is { VisitPhase: { } phase })
                checkpoints.TryAdd(phase, reference.CurrentMinute);
        }
        reference.AdvanceUntil(end);
        Assert.Equal([MigrationVisitPhase.Outbound, MigrationVisitPhase.Dwell, MigrationVisitPhase.Returning], checkpoints.Keys.Order());
        var history = reference.CreatePersistenceSnapshot().HistoricalEvents;
        Assert.Contains(history, x => x.EventType == HistoricalEventType.TradeCompleted);
        Assert.Contains(history, x => x.EventType == HistoricalEventType.TradeReturned);
        MigrationValidation.Validate(reference.CreatePersistenceSnapshot());

        foreach (var (phase, minute) in checkpoints)
        {
            var interrupted = SimulationEngine.FromPersistenceSnapshot(start);
            interrupted.AdvanceUntil(minute);
            var checkpoint = interrupted.CreatePersistenceSnapshot();
            MigrationValidation.Validate(checkpoint);
            Assert.Equal(phase, checkpoint.MigrationState!.InTransitParties.Single(x => x.OriginSettlementId == 1).VisitPhase);
            var reopened = new SimulationEngine(checkpoint);
            Assert.Equal(checkpoint.MigrationStateJson, reopened.CreatePersistenceSnapshot().MigrationStateJson);
            reopened.AdvanceUntil(end);
            M15RoadNetworkTests.AssertEquivalent(reference, reopened);
        }
    }

    [Fact]
    public void ChunkSizeDoesNotChangeTrade()
    {
        var start = CreateStockedSnapshot(new WorldSeed(1501), depart: true);
        var end = new WorldMinute(start.WorldMinute.Value + WorldCalendar.MinutesPerDay);
        var single = SimulationEngine.FromPersistenceSnapshot(start);
        single.AdvanceUntil(end);
        var chunked = SimulationEngine.FromPersistenceSnapshot(start);
        for (var minute = start.WorldMinute.Value + 37; minute < end.Value; minute += 37) chunked.AdvanceUntil(new WorldMinute(minute));
        chunked.AdvanceUntil(end);
        M15RoadNetworkTests.AssertEquivalent(single, chunked);
    }

    /// <summary>
    /// A new M15 world whose first eight households live at a daughter site some way off. Site 1 is given spare
    /// fuel and the daughter spare medicine, so each has a surplus the other is short of. Only site 1 has the
    /// communal food for a trader's provisions.
    /// </summary>
    internal static SimulationPersistenceSnapshot CreateStockedSnapshot(WorldSeed seed, bool depart)
    {
        var baseline = new SimulationEngine(seed, simulationRulesVersion: Roads).CreatePersistenceSnapshot();
        var daughterHouseholds = baseline.Households.Where(x => x.DissolvedMinute is null).OrderBy(x => x.Id.Value)
            .Take(8).Select(x => x.Id.Value).ToHashSet();
        var world = baseline.World!;
        var costs = LivingTravelCosts.Compute(world, world.StartingSite);
        var daughterSite = world.Tiles.Where(x => x.Walkable && costs.TryGetValue(x.Coordinate, out var cost) && cost >= 60)
            .OrderBy(x => costs[x.Coordinate]).ThenBy(x => x.Coordinate.Y).ThenBy(x => x.Coordinate.X)
            .First().Coordinate;
        var citizens = baseline.Citizens.Select(citizen =>
        {
            if (citizen.HouseholdId is { } household && daughterHouseholds.Contains(household.Value))
            {
                citizen.Location = daughterSite;
                citizen.HomeStructureId = null;
            }
            return citizen;
        }).ToArray();
        long Site(long? householdId) => householdId is { } id && daughterHouseholds.Contains(id) ? 2 : 1;
        var current = baseline.MigrationState!;
        var migration = new MigrationWorldState(current.Version,
            citizens.Select(x => new MigrationEntityResidence(x.Id.Value, Site(x.HouseholdId?.Value))).ToArray(),
            baseline.Households.Select(x => new MigrationEntityResidence(x.Id.Value, Site(x.Id.Value))).ToArray(),
            current.StructureOwners, current.FacilityOwners, current.WorkOrderOwners,
            new MigrationDaughterSettlementState(daughterSite,
                new MigrationSettlementStockState(0, 0, 0, EconomyRules.FoundingStorageCapacity,
                    baseline.WorldMinute.Value, baseline.WorldMinute.Value),
                Enum.GetValues<LivingGood>().Select(x => new LivingStock(x, x == LivingGood.Medicine ? 40 : 0)).ToArray()),
            roads: current.Roads);
        var engine = SimulationEngine.FromPersistenceSnapshot(CopySnapshot(baseline, migration.ToCanonicalJson(), citizens));
        InvokePrivate(engine, "ChangeGoodAt", 1L, LivingGood.Fuel, 150);
        if (depart) InvokePrivate(engine, "EvaluateMigrationTrade");
        var snapshot = engine.CreatePersistenceSnapshot();
        MigrationValidation.Validate(snapshot);
        return snapshot;
    }

    private static Dictionary<MigrationCargoGood, long> Totals(SimulationEngine engine)
    {
        var parties = MigrationState(engine).InTransitParties;
        var recoverable = engine.CreatePersistenceSnapshot().Economy!.Recoverable;
        return Traded.ToDictionary(good => good, good =>
            (long)InvokePrivate(engine, "TradeStock", 1L, good)! + (long)InvokePrivate(engine, "TradeStock", 2L, good)! +
            parties.SelectMany(x => x.Cargo).Where(x => x.Good == good).Sum(x => x.Quantity) +
            recoverable.Where(x => good.ToString() == x.Resource.ToString()).Sum(x => (long)x.Quantity));
    }

    private static void Arrive(Citizen traveler, TileCoordinate site)
    {
        traveler.Location = site;
        traveler.CurrentAction = CitizenAction.Explore;
        traveler.ActionTarget = site;
    }

    private static MigrationTransitPartyState PartyFrom(SimulationEngine engine, long origin) =>
        MigrationState(engine).InTransitParties.Single(x => x.OriginSettlementId == origin);

    private static MigrationWorldState MigrationState(SimulationEngine engine) => PrivateField<MigrationWorldState>(engine, "_migrationState");
    private static Dictionary<long, Citizen> Citizens(SimulationEngine engine) => PrivateField<Dictionary<long, Citizen>>(engine, "_citizens");
    private static IReadOnlyList<HistoricalEvent> Events(SimulationEngine engine) => engine.CreatePersistenceSnapshot().HistoricalEvents;

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
}
