using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M13MealConsumptionTests
{
    [Fact]
    public void M13ConsumesOnlyTheProjectedHungerPortionAcrossPrivateAndCommonFood()
    {
        var (engine, citizen) = Prepare(SimulationEngine.UnifiedSimulationRulesVersion, privateFood: 2, commonFood: 5);
        var before = engine.TotalStoredFood;
        var consumedBefore = engine.CaptureEconomy()!.FoodConsumed;

        Eat(engine, citizen);

        var after = engine.CaptureEconomy()!;
        Assert.Equal(4, after.FoodConsumed - consumedBefore);
        Assert.Equal(3, engine.Settlement.FoodStored);
        Assert.Equal(0, after.Households.Single(x => x.HouseholdId == citizen.HouseholdId!.Value.Value).Holdings.Food);
        Assert.Equal(before, engine.TotalStoredFood + (after.FoodConsumed - consumedBefore));
        Assert.Equal(0, citizen.Needs.Hunger);
    }

    [Fact]
    public void M12EconomicMealStillUsesItsFixedTenUnitPortion()
    {
        var (engine, citizen) = Prepare(SimulationEngine.SpacedSimulationRulesVersion, privateFood: 8, commonFood: 4);
        var before = engine.TotalStoredFood;
        var consumedBefore = engine.CaptureEconomy()!.FoodConsumed;

        Eat(engine, citizen);

        var after = engine.CaptureEconomy()!;
        Assert.Equal(10, after.FoodConsumed - consumedBefore);
        Assert.Equal(2, engine.Settlement.FoodStored);
        Assert.Equal(0, after.Households.Single(x => x.HouseholdId == citizen.HouseholdId!.Value.Value).Holdings.Food);
        Assert.Equal(before, engine.TotalStoredFood + (after.FoodConsumed - consumedBefore));
    }

    [Fact]
    public void M13CriticalMealReceivesOnlyDeterministicDonorSurplusAndRecordsTheTransfer()
    {
        var (engine, citizen) = Prepare(SimulationEngine.UnifiedSimulationRulesVersion, privateFood: 0, commonFood: 0, hunger: 9000);
        var recipient = citizen.HouseholdId!.Value.Value;
        var stocks = GetPrivateField<SortedDictionary<long, HouseholdStock>>(engine, "_householdStocks");
        foreach (var id in stocks.Keys.ToArray()) stocks[id] = stocks[id] with { Holdings = stocks[id].Holdings with { Food = 0 } };

        var donor = stocks.Keys.Where(id => id != recipient).Order().First();
        var donorMembers = engine.Citizens.Count(x => x.IsAlive && x.HouseholdId?.Value == donor);
        Assert.True(donorMembers > 0);
        var reserve = donorMembers * 60L;
        var donorFood = Math.Max(400, reserve + 10);
        stocks[donor] = stocks[donor] with { Holdings = stocks[donor].Holdings with { Food = donorFood } };
        engine.Settlement.FoodStored = 0;

        var storedBefore = engine.TotalStoredFood;
        var consumedBefore = engine.CaptureEconomy()!.FoodConsumed;
        Assert.Contains(Evaluate(engine, citizen), decision => decision.Action == CitizenAction.Eat);
        Eat(engine, citizen);

        var after = engine.CaptureEconomy()!;
        var eventRow = Assert.Single(after.Events);
        Assert.Equal("EmergencyFoodAid", eventRow.Kind);
        Assert.Equal(donor, eventRow.HouseholdId);
        Assert.Equal(recipient, eventRow.RecipientHouseholdId);
        Assert.Equal(new Goods(Food: 10), eventRow.Goods);
        Assert.Equal(donorFood - 10, after.Households.Single(x => x.HouseholdId == donor).Holdings.Food);
        Assert.True(after.Households.Single(x => x.HouseholdId == donor).Holdings.Food >= reserve);
        Assert.Equal(10, after.FoodConsumed - consumedBefore);
        Assert.True(storedBefore == engine.TotalStoredFood + after.FoodConsumed - consumedBefore,
            $"before={storedBefore}; afterStored={engine.TotalStoredFood}; afterFoodConsumed={after.FoodConsumed}; priorFoodConsumed={consumedBefore}; commons={engine.Settlement.FoodStored}; donorFood={after.Households.Single(x => x.HouseholdId == donor).Holdings.Food}; recipientFood={after.Households.Single(x => x.HouseholdId == recipient).Holdings.Food}");
    }

    [Fact]
    public void M13CriticalMealDoesNotTransferFoodWhenDonorsHaveNoSurplus()
    {
        var (engine, citizen) = Prepare(SimulationEngine.UnifiedSimulationRulesVersion, privateFood: 0, commonFood: 0, hunger: 9000);
        var recipient = citizen.HouseholdId!.Value.Value;
        var stocks = GetPrivateField<SortedDictionary<long, HouseholdStock>>(engine, "_householdStocks");
        foreach (var id in stocks.Keys.ToArray()) stocks[id] = stocks[id] with { Holdings = stocks[id].Holdings with { Food = 0 } };
        var donor = stocks.Keys.Where(id => id != recipient).Order().First();
        var donorMembers = engine.Citizens.Count(x => x.IsAlive && x.HouseholdId?.Value == donor);
        stocks[donor] = stocks[donor] with { Holdings = stocks[donor].Holdings with { Food = donorMembers * 60L } };
        engine.Settlement.FoodStored = 0;
        Assert.DoesNotContain(Evaluate(engine, citizen), decision => decision.Action == CitizenAction.Eat);

        var consumedBefore = engine.CaptureEconomy()!.FoodConsumed;
        Eat(engine, citizen);

        var after = engine.CaptureEconomy()!;
        Assert.Empty(after.Events);
        Assert.Equal(donorMembers * 60L, after.Households.Single(x => x.HouseholdId == donor).Holdings.Food);
        Assert.Equal(0, after.FoodConsumed - consumedBefore);
    }

    [Fact]
    public void M12CriticalMealDoesNotUseEmergencyHouseholdAid()
    {
        var (engine, citizen) = Prepare(SimulationEngine.SpacedSimulationRulesVersion, privateFood: 0, commonFood: 0, hunger: 9000);
        var recipient = citizen.HouseholdId!.Value.Value;
        var stocks = GetPrivateField<SortedDictionary<long, HouseholdStock>>(engine, "_householdStocks");
        foreach (var id in stocks.Keys.ToArray()) stocks[id] = stocks[id] with { Holdings = stocks[id].Holdings with { Food = 0 } };
        var donor = stocks.Keys.Where(id => id != recipient).Order().First();
        stocks[donor] = stocks[donor] with { Holdings = stocks[donor].Holdings with { Food = 400 } };
        engine.Settlement.FoodStored = 0;
        Assert.DoesNotContain(Evaluate(engine, citizen), decision => decision.Action == CitizenAction.Eat);

        Eat(engine, citizen);

        var after = engine.CaptureEconomy()!;
        Assert.Empty(after.Events);
        Assert.Equal(400, after.Households.Single(x => x.HouseholdId == donor).Holdings.Food);
        Assert.Equal(0, after.FoodConsumed);
    }

    [Fact]
    public void M13EmergencyAidRequiresEatingAtTheCommonSettlementSite()
    {
        var (engine, citizen) = Prepare(SimulationEngine.UnifiedSimulationRulesVersion, privateFood: 0, commonFood: 0, hunger: 9000);
        var recipient = citizen.HouseholdId!.Value.Value;
        var stocks = GetPrivateField<SortedDictionary<long, HouseholdStock>>(engine, "_householdStocks");
        foreach (var id in stocks.Keys.ToArray()) stocks[id] = stocks[id] with { Holdings = stocks[id].Holdings with { Food = 0 } };
        var donor = stocks.Keys.Where(id => id != recipient).Order().First();
        stocks[donor] = stocks[donor] with { Holdings = stocks[donor].Holdings with { Food = 400 } };
        engine.Settlement.FoodStored = 0;
        citizen.Location = engine.World.EnumerateTilesRowMajor().First(tile => tile.Walkable && tile.Coordinate != engine.World.StartingSite).Coordinate;

        Eat(engine, citizen);

        var after = engine.CaptureEconomy()!;
        Assert.Empty(after.Events);
        Assert.Equal(400, after.Households.Single(x => x.HouseholdId == donor).Holdings.Food);
        Assert.Equal(0, after.FoodConsumed);
    }

    private static (SimulationEngine Engine, Citizen Citizen) Prepare(string rules, int privateFood, int commonFood, int hunger = 2000)
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: rules);
        var citizens = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens");
        var citizen = citizens.Values.First(x => x.IsAlive && x.HouseholdId is not null);
        citizen.Needs = new CitizenNeeds(hunger: hunger);
        var owner = citizen.HouseholdId!.Value.Value;
        var households = GetPrivateField<SortedDictionary<long, HouseholdStock>>(engine, "_householdStocks");
        var stock = households[owner];
        households[owner] = stock with { Holdings = stock.Holdings with { Food = privateFood } };
        engine.Settlement.FoodStored = commonFood;
        return (engine, citizen);
    }

    private static void Eat(SimulationEngine engine, Citizen citizen) =>
        (typeof(SimulationEngine).GetMethod("Eat", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing Eat implementation."))
        .Invoke(engine, [citizen]);

    private static IReadOnlyList<CitizenDecisionEvaluation> Evaluate(SimulationEngine engine, Citizen citizen) =>
        (IReadOnlyList<CitizenDecisionEvaluation>)(typeof(SimulationEngine).GetMethod("EvaluateGrowthDecision", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing EvaluateGrowthDecision implementation."))
        .Invoke(engine, [citizen])!;

    private static T GetPrivateField<T>(object instance, string name) =>
        (T)(typeof(SimulationEngine).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException($"Missing {name}."));
}
