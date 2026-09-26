using LittleAges.Domain;

namespace LittleAges.Simulation;

public sealed partial class SimulationEngine
{
    private static readonly long MinutesPerTradeMonth = (long)WorldCalendar.DaysPerMonth * WorldCalendar.MinutesPerDay;
    internal const int TradeBaseLoad = 30;
    internal const int TradeTrailLoad = 30;
    internal const int TradeRoadLoad = 30;

    /// <summary>The goods settlements trade, in good ID order. Meal, Fiber, and Hide are not traded.</summary>
    private static readonly MigrationCargoGood[] TradedGoods =
    [
        MigrationCargoGood.Food, MigrationCargoGood.Wood, MigrationCargoGood.Stone, MigrationCargoGood.Grain,
        MigrationCargoGood.PreservedFood, MigrationCargoGood.Fuel, MigrationCargoGood.Tool,
        MigrationCargoGood.Clothing, MigrationCargoGood.Medicine
    ];

    internal static int TradeValue(MigrationCargoGood good) => good switch
    {
        MigrationCargoGood.Food or MigrationCargoGood.Grain or MigrationCargoGood.Wood or MigrationCargoGood.Fiber => 1,
        MigrationCargoGood.Stone or MigrationCargoGood.Hide or MigrationCargoGood.Meal or MigrationCargoGood.Fuel => 2,
        MigrationCargoGood.PreservedFood => 3,
        MigrationCargoGood.Medicine => 8,
        MigrationCargoGood.Clothing => 12,
        MigrationCargoGood.Tool => 15,
        _ => throw new ArgumentOutOfRangeException(nameof(good), good, null)
    };

    internal static long TradeTarget(MigrationCargoGood good, int population) => good switch
    {
        MigrationCargoGood.Food => 60L * population,
        MigrationCargoGood.Wood => 20L + 2L * population,
        MigrationCargoGood.Stone => 10L + 2L * population,
        MigrationCargoGood.Grain => 200L * population,
        MigrationCargoGood.Fuel => 3L * population,
        MigrationCargoGood.Tool or MigrationCargoGood.Clothing => Math.Max(2, population / 4),
        MigrationCargoGood.Medicine => population,
        MigrationCargoGood.PreservedFood => 1000L * population,
        _ => throw new ArgumentOutOfRangeException(nameof(good), good, null)
    };

    /// <summary>The smallest whole lot where both sides have exactly equal value.</summary>
    internal static (int Outbound, int Return) TradeLot(MigrationCargoGood outbound, MigrationCargoGood returned)
    {
        var outboundValue = TradeValue(outbound);
        var returnValue = TradeValue(returned);
        var divisor = (int)System.Numerics.BigInteger.GreatestCommonDivisor(outboundValue, returnValue);
        return (returnValue / divisor, outboundValue / divisor);
    }

    /// <summary>Carrying capacity grows with the share of the route that is trail or better, and again for road.</summary>
    internal static int TradeLoadFor(IReadOnlyList<TileCoordinate> route, Func<TileCoordinate, RoadGrade> gradeAt)
    {
        var steps = route.Count - 1;
        if (steps <= 0) return TradeBaseLoad;
        var trailOrBetter = 0;
        var road = 0;
        for (var index = 1; index < route.Count; index++)
        {
            var grade = gradeAt(route[index]);
            if (grade >= RoadGrade.Trail) trailOrBetter++;
            if (grade == RoadGrade.Road) road++;
        }
        return checked(TradeBaseLoad + TradeTrailLoad * trailOrBetter / steps + TradeRoadLoad * road / steps);
    }

    private void EvaluateMigrationTrade()
    {
        if (!RoadSystemsEnabled(SimulationRulesVersion) || _migrationState?.DaughterSettlement is null ||
            CurrentMinute.Value % MinutesPerTradeMonth != 0)
            return;
        foreach (var origin in MigrationSettlementIds) TryDepartTrade(origin);
    }

    private void TryDepartTrade(long origin)
    {
        var destination = OtherSettlement(origin);
        if (_migrationState!.InTransitParties.Any(x => x.JourneyKind == MigrationJourneyKind.Trade && x.OriginSettlementId == origin) ||
            PopulationAt(origin) == 0 || PopulationAt(destination) == 0 || SelectTrader(origin) is not { } trader)
            return;

        var originSite = SiteLocation(origin);
        var destinationSite = SiteLocation(destination);
        var route = FindPathCached(originSite, destinationSite);
        var outboundPath = FindPathCached(trader.Location, destinationSite);
        if (route is not { Count: >= 2 } || outboundPath is not { Count: >= 2 } ||
            FindPathCached(destinationSite, originSite) is not { Count: >= 2 })
            return;

        var provisions = FoundingProvisionFood(CurrentMinute.Value);
        var originStock = SettlementFor(origin);
        if (provisions > originStock.FoodStored) return;
        var load = TradeLoadFor(route, Roads!.GradeAt);
        if (PlanTrade(origin, destination, provisions, load) is not { } plan) return;

        originStock.FoodStored = checked(originStock.FoodStored - (int)provisions);
        ChangeTradeStock(origin, plan.Outbound, -plan.OutboundQuantity);
        var cargo = new[]
        {
            new MigrationCargoStackState(_counters.AllocateMigrationCargoStackId(), MigrationCargoGood.Food,
                provisions, MigrationCargoPurpose.Provisions),
            new MigrationCargoStackState(_counters.AllocateMigrationCargoStackId(), plan.Outbound,
                plan.OutboundQuantity, MigrationCargoPurpose.Cargo)
        };
        var party = new MigrationTransitPartyState(_counters.AllocateMigrationPartyId(), trader.HouseholdId!.Value.Value,
            origin, destination, trader.Location, destinationSite, [trader.Id.Value], cargo,
            checked((int)TravelPathCost(outboundPath)), CurrentMinute.Value, journeyKind: MigrationJourneyKind.Trade,
            visitPhase: MigrationVisitPhase.Outbound, tradeReturnGood: plan.Return, tradeLoad: load);
        SetMigrationParty(party);
        EmitHistory(HistoricalEventType.TradeDeparted, HistoricalImportance.Routine, party.Location,
            HistoricalEventPayloads.TradeDeparted(party.Id, trader.Id.Value, origin, destination, plan.Outbound,
                plan.OutboundQuantity, plan.Return),
            [(trader.Id, "subject")]);
        StartFoundingTravel(trader, destinationSite);
    }

    /// <summary>The capable adult resident with the most hauling skill, then the lowest ID.</summary>
    private Citizen? SelectTrader(long origin) => _citizens.Values
        .Where(x => x.IsAlive && x.AgeYears(CurrentMinute) >= 18 && x.HouseholdId is { } householdId &&
            SiteIdForCitizen(x) == origin && FoundingPartyForCitizen(x.Id.Value) is null &&
            LivingPerson(x) is { Injury: < 7000, Illness: < 7000 } &&
            !HasMigrationWorkOrBarterObligations(householdId.Value, [x]))
        .OrderByDescending(x => x.Skills.Hauling).ThenBy(x => x.Id.Value).FirstOrDefault();

    private (MigrationCargoGood Outbound, int OutboundQuantity, MigrationCargoGood Return)? PlanTrade(long origin,
        long destination, long provisions, int load)
    {
        var originPopulation = PopulationAt(origin);
        var destinationPopulation = PopulationAt(destination);
        long OriginStock(MigrationCargoGood good) => TradeStock(origin, good) - (good == MigrationCargoGood.Food ? provisions : 0);
        long Short(long stock, MigrationCargoGood good, int population) => Math.Max(0, TradeTarget(good, population) - stock);
        long Surplus(long stock, MigrationCargoGood good, int population) => Math.Max(0, stock - 2 * TradeTarget(good, population));

        var pairs = new List<(MigrationCargoGood Outbound, MigrationCargoGood Return, long Priority, long Lots)>();
        foreach (var returned in TradedGoods)
        {
            var originShort = Short(OriginStock(returned), returned, originPopulation);
            var destinationSurplus = Surplus(TradeStock(destination, returned), returned, destinationPopulation);
            if (originShort == 0 || destinationSurplus == 0) continue;
            foreach (var outbound in TradedGoods.Where(x => x != returned))
            {
                var originSurplus = Surplus(OriginStock(outbound), outbound, originPopulation);
                var destinationShort = Short(TradeStock(destination, outbound), outbound, destinationPopulation);
                if (originSurplus == 0 || destinationShort == 0) continue;
                var (outboundLot, returnLot) = TradeLot(outbound, returned);
                var lots = new[]
                {
                    originSurplus / outboundLot, destinationShort / outboundLot, (long)load / outboundLot,
                    destinationSurplus / returnLot, originShort / returnLot, (long)load / returnLot
                }.Min();
                pairs.Add((outbound, returned, checked(originShort * TradeValue(returned)), lots));
            }
        }

        foreach (var pair in pairs.OrderByDescending(x => x.Priority).ThenBy(x => x.Return).ThenBy(x => x.Outbound))
        {
            if (pair.Lots < 1) continue;
            return (pair.Outbound, checked((int)(pair.Lots * TradeLot(pair.Outbound, pair.Return).Outbound)), pair.Return);
        }
        return null;
    }

    /// <summary>
    /// The destination's needs are measured again on arrival. The trader exchanges as many whole lots as the
    /// cargo, the destination's surplus and shortfall, its free storage, and the trader's load all allow.
    /// </summary>
    private MigrationTransitPartyState ExchangeTradeCargo(Citizen trader, MigrationTransitPartyState party)
    {
        var destination = party.DestinationSettlementId ?? throw new InvalidDataException("A trade party has no destination.");
        var returned = party.TradeReturnGood ?? throw new InvalidDataException("A trade party has no return good.");
        var load = party.TradeLoad ?? throw new InvalidDataException("A trade party has no load.");
        var outboundStacks = party.Cargo.Where(x => x.Purpose == MigrationCargoPurpose.Cargo && x.Good != returned).ToArray();
        if (outboundStacks.Length == 0) return party;
        var outbound = outboundStacks[0].Good;
        var carried = outboundStacks.Sum(x => x.Quantity);
        var (outboundLot, returnLot) = TradeLot(outbound, returned);
        var population = PopulationAt(destination);

        var lots = new[]
        {
            carried / outboundLot,
            Math.Max(0, TradeStock(destination, returned) - 2 * TradeTarget(returned, population)) / returnLot,
            Math.Max(0, TradeTarget(outbound, population) - TradeStock(destination, outbound)) / outboundLot
        }.Min();
        if (returnLot > outboundLot) lots = Math.Min(lots, Math.Max(0, load - carried) / (returnLot - outboundLot));
        if (outboundLot > returnLot)
            lots = Math.Min(lots, FreeTradeStorage(destination, MigrationCargoGood.Food) / (outboundLot - returnLot));
        var nonFoodIn = IsNonFoodTradeGood(outbound) ? outboundLot : 0;
        var nonFoodOut = IsNonFoodTradeGood(returned) ? returnLot : 0;
        if (nonFoodIn > nonFoodOut)
            lots = Math.Min(lots, FreeTradeStorage(destination, MigrationCargoGood.Wood) / (nonFoodIn - nonFoodOut));
        if (lots <= 0) return party;

        var sold = checked((int)(lots * outboundLot));
        var bought = checked((int)(lots * returnLot));
        ChangeTradeStock(destination, outbound, sold);
        ChangeTradeStock(destination, returned, -bought);
        var cargo = new List<MigrationCargoStackState>();
        var unsold = (long)sold;
        foreach (var stack in party.Cargo)
        {
            if (stack.Purpose != MigrationCargoPurpose.Cargo || stack.Good != outbound || unsold == 0)
            {
                cargo.Add(stack);
                continue;
            }
            var taken = Math.Min(unsold, stack.Quantity);
            unsold -= taken;
            if (taken < stack.Quantity) cargo.Add(stack with { Quantity = stack.Quantity - taken });
        }
        cargo.Add(new MigrationCargoStackState(_counters.AllocateMigrationCargoStackId(), returned, bought,
            MigrationCargoPurpose.Cargo));
        var exchanged = new MigrationTransitPartyState(party.Id, party.HouseholdId, party.OriginSettlementId,
            party.DestinationSettlementId, party.Location, party.DestinationSite, party.CitizenIds, cargo,
            party.RemainingPathCost, party.DepartedMinute, party.Returning, party.FoundingAdultArrived,
            party.JourneyKind, party.VisitRelativeId, party.VisitPhase, party.VisitDwellEndsMinute,
            party.TradeReturnGood, party.TradeLoad);
        SetMigrationParty(exchanged);
        EmitHistory(HistoricalEventType.TradeCompleted, HistoricalImportance.Personal, party.DestinationSite,
            HistoricalEventPayloads.TradeCompleted(party.Id, trader.Id.Value, party.OriginSettlementId, destination,
                outbound, sold, returned, bought),
            [(trader.Id, "subject")]);
        return exchanged;
    }

    /// <summary>Home again: deposit what fits and leave the rest where M14 recovery would.</summary>
    private void CompleteTradeReturn(Citizen trader, MigrationTransitPartyState party)
    {
        var origin = party.OriginSettlementId;
        var site = SiteLocation(origin);
        var exchanged = party.Cargo.Any(x => x.Purpose == MigrationCargoPurpose.Cargo && x.Good == party.TradeReturnGood);
        RemoveMigrationParty(party);
        foreach (var stack in party.Cargo)
        {
            var good = stack.Good == MigrationCargoGood.Meal ? MigrationCargoGood.Food : stack.Good;
            var deposited = Math.Min(stack.Quantity, FreeTradeStorage(origin, good));
            if (deposited > 0) ChangeTradeStock(origin, good, checked((int)deposited));
            var left = checked((int)(stack.Quantity - deposited));
            var resource = good switch
            {
                MigrationCargoGood.Food => ResourceType.Food,
                MigrationCargoGood.Wood => ResourceType.Wood,
                MigrationCargoGood.Stone => ResourceType.Stone,
                _ => (ResourceType?)null
            };
            if (resource is { } recoverable) AddRecoverable(null, site, recoverable, left);
            else if (good == MigrationCargoGood.Grain) SpoilLostGrain(left);
        }
        EmitHistory(HistoricalEventType.TradeReturned, HistoricalImportance.Routine, site,
            HistoricalEventPayloads.TradeReturned(party.Id, trader.Id.Value, origin, OtherSettlement(origin), exchanged),
            [(trader.Id, "subject")]);
        FinishFoundingTravel(trader);
    }

    private void RecordTradeLost(MigrationTransitPartyState party)
    {
        var trader = new CitizenId(party.CitizenIds[0]);
        EmitHistory(HistoricalEventType.TradeLost, HistoricalImportance.Notable, party.Location,
            HistoricalEventPayloads.TradeLost(party.Id, trader.Value, party.OriginSettlementId,
                OtherSettlement(party.OriginSettlementId)),
            [(trader, "subject")]);
    }

    /// <summary>A trade party's destination: M15 has exactly two settlements.</summary>
    private static long OtherSettlement(long settlementId) =>
        settlementId == 1 ? MigrationDaughterSettlementState.SettlementId : 1;

    private static bool IsNonFoodTradeGood(MigrationCargoGood good) =>
        good is MigrationCargoGood.Wood or MigrationCargoGood.Stone;

    /// <summary>Free storage measured like the harvest fix: available space less cargo citizens are carrying in.</summary>
    private long FreeTradeStorage(long settlementId, MigrationCargoGood good) => Math.Max(0L,
        AvailableStorage(IsNonFoodTradeGood(good) ? ResourceType.Wood : ResourceType.Food, settlementId) -
        M12CitizenCargoAt(settlementId));

    private long TradeStock(long settlementId, MigrationCargoGood good) => good switch
    {
        MigrationCargoGood.Food => SettlementFor(settlementId).FoodStored,
        MigrationCargoGood.Wood => SettlementFor(settlementId).WoodStored,
        MigrationCargoGood.Stone => SettlementFor(settlementId).StoneStored,
        _ => GoodAt(settlementId, Enum.Parse<LivingGood>(good.ToString()))
    };

    private void ChangeTradeStock(long settlementId, MigrationCargoGood good, int quantity)
    {
        var settlement = SettlementFor(settlementId);
        switch (good)
        {
            case MigrationCargoGood.Food:
                settlement.FoodStored = checked(settlement.FoodStored + quantity);
                break;
            case MigrationCargoGood.Wood:
                settlement.WoodStored = checked(settlement.WoodStored + quantity);
                break;
            case MigrationCargoGood.Stone:
                settlement.StoneStored = checked(settlement.StoneStored + quantity);
                break;
            default:
                ChangeGoodAt(settlementId, Enum.Parse<LivingGood>(good.ToString()), quantity);
                break;
        }
        if (settlement.FoodStored < 0 || settlement.WoodStored < 0 || settlement.StoneStored < 0)
            throw new InvalidOperationException("Trade cannot withdraw goods a settlement does not hold.");
    }
}
