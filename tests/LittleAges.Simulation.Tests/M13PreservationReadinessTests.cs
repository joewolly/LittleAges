using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M13PreservationReadinessTests
{
    [Fact]
    public void PreservationReadinessUsesUnifiedFarmFoodAndKeepsLivingRulesOnTheirOriginalCounter()
    {
        var discoveryReady = typeof(SimulationEngine).GetMethod("DiscoveryReady", BindingFlags.Instance | BindingFlags.NonPublic)!;

        foreach (var rulesVersion in new[]
        {
            SimulationEngine.LivingSimulationRulesVersion,
            SimulationEngine.Living2SimulationRulesVersion,
            SimulationEngine.UnifiedSimulationRulesVersion
        })
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: rulesVersion);
            var state = GetPrivateField<LivingWorldState>(engine, "_living");
            var citizen = GetPrivateField<Dictionary<long, Citizen>>(engine, "_citizens").Values.First(x => x.IsAlive);
            var person = state.People.Single(x => x.CitizenId == citizen.Id.Value);
            person.Practice = 12;
            state.FarmFoodHarvested = 1;
            state.FoodHarvested = 0;

            var readyFromUnifiedFarmFood = (bool)discoveryReady.Invoke(engine, [citizen, LivingTechnique.Preservation])!;
            Assert.Equal(rulesVersion == SimulationEngine.UnifiedSimulationRulesVersion, readyFromUnifiedFarmFood);

            if (rulesVersion != SimulationEngine.UnifiedSimulationRulesVersion)
            {
                state.FoodHarvested = 1;
                Assert.True((bool)discoveryReady.Invoke(engine, [citizen, LivingTechnique.Preservation])!);
            }
        }
    }

    [Fact]
    public void M13DiscoversPreservationAndBuildsACommunalBufferBeforeTheNextHarvest()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
        var beforeNextHarvest = new WorldMinute(checked(5 * WorldCalendar.MinutesPerYear + 180L * WorldCalendar.MinutesPerDay - 1));
        engine.AdvanceUntil(beforeNextHarvest);

        var state = LivingWorldCodec.Deserialize(engine.LivingStateJson!);
        var discoveryFact = state.Facts.FirstOrDefault(x => x.Kind == LivingFactKind.TechniqueDiscovered && x.Value == (int)LivingTechnique.Preservation);
        var preservers = state.People.Count(x => !x.DeathObserved && x.Knowledge.Contains(LivingTechnique.Preservation));
        var preservedFood = state.Stock.Single(x => x.Good == LivingGood.PreservedFood).Quantity;
        var preserveOrders = state.Orders.Count(x => x.Kind == LivingWorkKind.Preserve);
        // The daily environment releases preserves into M12 communal Food when
        // its stock falls below population * 30, so preserved stock is transient.
        // M13 counters distinguish the 20 Grain spent by each Cook from Preserve.
        var cookedGrain = checked((state.FoodPrepared / 30) * 20);
        var preservedFoodProduced = state.CommunalGrainConsumed - cookedGrain;
        Assert.True(state.FarmFoodHarvested > 0, "M13 must expose completed physical farm-food deliveries to Living knowledge.");
        Assert.NotNull(discoveryFact);
        Assert.True(preservers > 0);
        Assert.Equal(0, state.FoodPrepared % 30);
        Assert.True(preservedFoodProduced > 0 && preservedFoodProduced % 20 == 0,
            $"Expected completed Preserve production before the next harvest; population={engine.LivingPopulation}, grain={state.Stock.Single(x => x.Good == LivingGood.Grain).Quantity}, preservedStock={preservedFood}, preservationInputs={preservedFoodProduced}, preserveOrders={preserveOrders}.");
    }

    private static T GetPrivateField<T>(SimulationEngine engine, string name) =>
        (T)typeof(SimulationEngine).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
}
