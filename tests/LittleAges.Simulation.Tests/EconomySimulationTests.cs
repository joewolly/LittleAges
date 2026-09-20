using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class EconomySimulationTests
{
    [Fact]
    public void LastMemberWithoutDescendantsReturnsUnclaimedInventoryToCommons()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.BarterSimulationRulesVersion);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var citizens = (IDictionary<long, Citizen>)typeof(SimulationEngine).GetField("_citizens", flags)!.GetValue(engine)!;
        var worker = citizens.Values.OrderBy(c => c.Id.Value).First();
        var household = worker.HouseholdId!.Value.Value;
        typeof(SimulationEngine).GetMethod("RecordProduction", flags)!.Invoke(engine, [worker, ResourceType.Food, 5]);
        typeof(SimulationEngine).GetMethod("DepositOwnedProduction", flags)!.Invoke(engine, [worker, ResourceType.Food, 5]);
        typeof(SimulationEngine).GetMethod("ClearCarriedState", flags)!.Invoke(engine, [worker]);
        typeof(SimulationEngine).GetMethod("KillNatural", flags)!.Invoke(engine, [worker]);
        Assert.Equal(405, engine.Settlement.FoodStored);
        Assert.DoesNotContain(engine.CaptureEconomy()!.Households, h => h.HouseholdId == household);
        Assert.Contains(engine.CaptureEconomy()!.Events, e => e.Kind == "Inheritance" && e.HouseholdId == household && e.RecipientHouseholdId is null && e.Goods.Food == 4);
        _ = engine.CreatePersistenceSnapshot();
    }

    [Fact]
    public void ProductionBelongsToTheHouseholdAtExtractionEvenIfTheWorkerMoves()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.BarterSimulationRulesVersion);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var citizens = (IDictionary<long, Citizen>)typeof(SimulationEngine).GetField("_citizens", flags)!.GetValue(engine)!;
        var worker = citizens.Values.OrderBy(c => c.Id.Value).First();
        var original = worker.HouseholdId;
        var destination = citizens.Values.First(c => c.HouseholdId != original).HouseholdId;
        typeof(SimulationEngine).GetMethod("RecordProduction", flags)!.Invoke(engine, [worker, ResourceType.Food, 5]);
        Assert.Equal(original!.Value.Value, Assert.Single(engine.CaptureEconomy()!.ProductionCargo).HouseholdId);
        worker.HouseholdId = destination;
        typeof(SimulationEngine).GetMethod("DepositOwnedProduction", flags)!.Invoke(engine, [worker, ResourceType.Food, 5]);
        var state = engine.CaptureEconomy()!;
        Assert.Equal(4, state.Households.Single(h => h.HouseholdId == original.Value.Value).Holdings.Food);
        Assert.Equal(0, state.Households.Single(h => h.HouseholdId == destination!.Value.Value).Holdings.Food);
        worker.HouseholdId = original;
        typeof(SimulationEngine).GetMethod("ClearCarriedState", flags)!.Invoke(engine, [worker]);
        _ = engine.CreatePersistenceSnapshot();
    }

    [Fact]
    public void ContributionRemaindersAndPublicPaymentsNeverCreateGoods()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.BarterSimulationRulesVersion);
        var citizens = (IDictionary<long, Citizen>)typeof(SimulationEngine).GetField("_citizens", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(engine)!;
        var worker = citizens.Values.OrderBy(c => c.Id.Value).First();
        var produce = typeof(SimulationEngine).GetMethod("RecordProduction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var deposit = typeof(SimulationEngine).GetMethod("DepositOwnedProduction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        for (var i = 0; i < 5; i++) { produce.Invoke(engine, [worker, ResourceType.Food, 1]); deposit.Invoke(engine, [worker, ResourceType.Food, 1]); }
        typeof(SimulationEngine).GetMethod("ClearCarriedState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(engine, [worker]);
        var state = engine.CaptureEconomy()!;
        var household = state.Households.Single(h => h.HouseholdId == worker.HouseholdId!.Value.Value);
        Assert.Equal(401, engine.Settlement.FoodStored);
        Assert.Equal(4, household.Holdings.Food);
        Assert.Equal(0, household.ContributionRemainders.Food);
        typeof(SimulationEngine).GetMethod("ReservePublicWork", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(engine, [worker]);
        Assert.Equal(397, engine.Settlement.FoodStored);
        Assert.Equal(4, Assert.Single(engine.CaptureEconomy()!.PublicWork).Food);
        typeof(SimulationEngine).GetMethod("CompletePublicWork", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(engine, [worker]);
        Assert.Equal(8, engine.CaptureEconomy()!.Households.Single(h => h.HouseholdId == household.HouseholdId).Holdings.Food);
        Assert.Empty(engine.CaptureEconomy()!.PublicWork);
        _ = engine.CreatePersistenceSnapshot();
    }

    private static readonly Lazy<SimulationPersistenceSnapshot> Transit = new(() =>
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.BarterSimulationRulesVersion);
        while (engine.CurrentMinute.Value < WorldCalendar.MinutesPerYear && !engine.CaptureEconomy()!.Trades.Any(t => t.Status == BarterStatus.Reserved && (t.PickedA && !t.DeliveredA || t.PickedB && !t.DeliveredB))) engine.ProcessNextEvent();
        Assert.Contains(engine.CaptureEconomy()!.Trades, t => t.Status == BarterStatus.Reserved && (t.PickedA && !t.DeliveredA || t.PickedB && !t.DeliveredB));
        return engine.CreatePersistenceSnapshot();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelledTripOrDeadCarrierConservesCargoAndReleasesReservations(bool death)
    {
        var engine = SimulationEngine.FromPersistenceSnapshot(Transit.Value);
        var before = engine.CaptureEconomy()!;
        var trade = before.Trades.First(t => t.Status == BarterStatus.Reserved && (t.PickedA && !t.DeliveredA || t.PickedB && !t.DeliveredB));
        if (death)
        {
            var id = (trade.PickedA && !trade.DeliveredA ? trade.CarrierA : trade.CarrierB)!.Value;
            var citizens = (IDictionary<long, Citizen>)typeof(SimulationEngine).GetField("_citizens", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(engine)!;
            typeof(SimulationEngine).GetMethod("KillNatural", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(engine, [citizens[id]]);
        }
        else typeof(SimulationEngine).GetMethod("CancelTrade", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(engine, [trade.Id, null]);
        var after = engine.CaptureEconomy()!;
        Assert.Equal(before.Produced, after.Produced);
        Assert.Equal(before.FoodConsumed, after.FoodConsumed);
        Assert.Equal(BarterStatus.Cancelled, after.Trades.Single(t => t.Id == trade.Id).Status);
        Assert.NotEmpty(after.Recoverable);
        _ = engine.CreatePersistenceSnapshot();
        engine.AdvanceUntil(engine.CurrentMinute.Add(WorldCalendar.MinutesPerDay));
        _ = engine.CreatePersistenceSnapshot();
    }

    [Fact]
    public void RejectsDuplicateOwnersOverdrawnReservationsAndInventedHoldings()
    {
        var snapshot = Transit.Value;
        var state = snapshot.Economy!;
        void Validate(EconomyState candidate) => candidate.Validate(snapshot.Citizens, snapshot.Households, snapshot.Structures, snapshot.Settlement!, snapshot.World!, snapshot.WorldMinute.Value,
            snapshot.Settlement!.BaseStorageCapacity + snapshot.Structures.Count(s => s.Type == StructureType.Stockpile && s.Status == StructureStatus.Complete) * CitizenSimulationRules.StockpileStorageBonus + snapshot.Structures.Count(s => s.Type == StructureType.Granary && s.Status == StructureStatus.Complete) * AgricultureRules.GranaryFoodCapacity,
            snapshot.Structures.Count(s => s.Type == StructureType.Granary && s.Status == StructureStatus.Complete) * AgricultureRules.GranaryFoodCapacity);
        Assert.Throws<ArgumentException>(() => Validate(state with { Households = state.Households.Concat([state.Households[0]]).ToArray() }));
        Assert.Throws<ArgumentException>(() => Validate(state with { Households = state.Households.Reverse().ToArray() }));
        Assert.Throws<ArgumentException>(() => Validate(state with { Households = state.Households.Select((h, i) => i == 0 ? h with { Holdings = h.Holdings.Add(ResourceType.Food, 1) } : h).ToArray() }));
        var pending = state.Trades.First(t => t.Status == BarterStatus.Reserved);
        Assert.Throws<ArgumentException>(() => Validate(state with { Trades = state.Trades.Select(t => t.Id == pending.Id ? t with { QuantityA = t.QuantityA + 1 } : t).ToArray() }));
        Assert.Throws<ArgumentException>(() => Validate(state with { Households = state.Households.Select((h, i) => i == 0 ? h with { Holdings = new Goods(1000000) } : h).ToArray() }));
        Assert.Throws<System.Text.Json.JsonException>(() => EconomyState.Parse("{\"Version\":1}"));
    }

    [Fact]
    public void ProductionLossCanExhaustFiniteSuppliesAndCauseExtinction()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.BarterSimulationRulesVersion);
        var resources = (IDictionary<long, ResourceState>)typeof(SimulationEngine).GetField("_resourceStates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(engine)!;
        var foodNodes = engine.World.Resources.Where(n => n.Type == ResourceType.Food).Select(n => n.Id.Value).ToArray();
        // Explicit external production-loss fixture: prevent regeneration from leaving
        // available forage. Keep the original founding supplies, needs, and mortality.
        while (engine.LivingPopulation > 0 && engine.CurrentMinute.Value < WorldCalendar.MinutesPerYear)
        {
            foreach (var id in foodNodes) resources[id].CurrentQuantity = 0;
            engine.ProcessNextEvent();
        }
        Assert.Equal(0, engine.LivingPopulation);
        Assert.Equal(20, engine.DeadPopulation);
        Assert.Equal(0, engine.CaptureEconomy()!.Produced.Food);
        Assert.True(engine.CaptureEconomy()!.FoodConsumed <= 400);
        _ = engine.CreatePersistenceSnapshot();
    }

    [Fact]
    public void DailyCheckpointsConserveOwnershipAndPhysicalTrades()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.BarterSimulationRulesVersion);
        for (var day = 0; day <= 360; day++)
        {
            engine.AdvanceUntil(new WorldMinute(day * (long)WorldCalendar.MinutesPerDay));
            try { _ = engine.CreatePersistenceSnapshot(); }
            catch (ArgumentException error) { throw new InvalidOperationException($"Invalid economy on day {day}: {error.Message}", error); }
        }
        var economy = engine.CaptureEconomy()!;
        Assert.Contains(economy.Trades, t => t.Status == BarterStatus.Completed);
        Assert.Contains(economy.Assignments, a => a.Specialization == WorkSpecialization.Farmer);
        Assert.True(economy.Households.Select(h => h.Holdings.Value).Distinct().Count() > 1);
        Assert.True(economy.PublicWorkPaid > 0);
        Assert.True(economy.EmergencyFoodConsumed > 0);
        Assert.Equal(engine.ComputeEconomyFingerprint(), new SimulationEngine(engine.CreatePersistenceSnapshot()).ComputeEconomyFingerprint());
    }
}

