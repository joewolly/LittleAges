using System.Reflection;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M13EmergencyFoodAidPersistenceTests
{
    [Fact]
    public async Task M13EmergencyFoodAidEventAndConsumptionSurviveSqliteCheckpointReplay()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
        var citizens = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens");
        var recipient = citizens.Values.First(x => x.IsAlive && x.HouseholdId is not null);
        recipient.Needs = new CitizenNeeds(hunger: 9000);
        var recipientHousehold = recipient.HouseholdId!.Value.Value;
        var stocks = GetPrivateField<SortedDictionary<long, HouseholdStock>>(engine, "_householdStocks");
        var originalStored = engine.TotalStoredFood;
        foreach (var id in stocks.Keys.ToArray()) stocks[id] = stocks[id] with { Holdings = stocks[id].Holdings with { Food = 0 } };

        var donor = stocks.Keys.Where(id => id != recipientHousehold).Order().First();
        var donorMembers = citizens.Values.Count(x => x.IsAlive && x.HouseholdId?.Value == donor);
        Assert.True(donorMembers > 0);
        var donorFood = Math.Max(400, donorMembers * 60L + 10);
        stocks[donor] = stocks[donor] with { Holdings = stocks[donor].Holdings with { Food = donorFood } };
        engine.Settlement.FoodStored = 0;
        InvokeReevaluateFoodShortage(engine);

        // Keep this focused checkpoint canonical while arranging the donor case.
        // Any synthetic fixture stock beyond the founding 400 is recorded as Food production.
        var fixtureProduction = donorFood - originalStored;
        Assert.True(fixtureProduction >= 0);
        if (fixtureProduction > 0)
        {
            var producedField = GetPrivateField<Goods>(engine, "_producedGoods");
            SetPrivateField(engine, "_producedGoods", producedField.Add(ResourceType.Food, fixtureProduction));
            var history = GetPrivateField<HistoryState>(engine, "_historyState");
            history.FoodProducedSinceSample = checked(history.FoodProducedSinceSample + fixtureProduction);
        }

        InvokeEat(engine, recipient);
        var before = engine.CreatePersistenceSnapshot();
        var expectedEconomy = before.Economy!.ToCanonicalJson();
        var eventRow = Assert.Single(before.Economy.Events);
        Assert.Equal("EmergencyFoodAid", eventRow.Kind);
        Assert.Equal(donor, eventRow.HouseholdId);
        Assert.Equal(recipientHousehold, eventRow.RecipientHouseholdId);
        Assert.Equal(10, before.Economy.FoodConsumed);

        var root = Path.Combine(Path.GetTempPath(), "littleages-m13-emergency-food-aid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "world.db");
        try
        {
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(before);

            SimulationEngine reopened;
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                Assert.Equal(expectedEconomy, loaded.Economy!.ToCanonicalJson());
                reopened = SimulationEngine.FromPersistenceSnapshot(loaded);
                Assert.Equal(engine.ComputeEconomyFingerprint(), reopened.ComputeEconomyFingerprint());
            }

            var uninterrupted = SimulationEngine.FromPersistenceSnapshot(before);
            var next = before.ScheduledEvents.Min(x => x.Order.DueWorldMinute);
            uninterrupted.AdvanceUntil(next);
            reopened.AdvanceUntil(next);
            Assert.Equal(uninterrupted.ComputeEconomyFingerprint(), reopened.ComputeEconomyFingerprint());
            Assert.Equal(uninterrupted.SurvivalFingerprint, reopened.SurvivalFingerprint);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void InvokeEat(SimulationEngine engine, Citizen citizen) =>
        (typeof(SimulationEngine).GetMethod("Eat", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing Eat implementation."))
        .Invoke(engine, [citizen]);

    private static void InvokeReevaluateFoodShortage(SimulationEngine engine) =>
        (typeof(SimulationEngine).GetMethod("ReevaluateFoodShortage", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing ReevaluateFoodShortage implementation."))
        .Invoke(engine, [false]);

    private static T GetPrivateField<T>(object instance, string name) =>
        (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException($"Missing {name}."));

    private static void SetPrivateField<T>(object instance, string name, T value) =>
        (instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing {name}.")).SetValue(instance, value);
}
