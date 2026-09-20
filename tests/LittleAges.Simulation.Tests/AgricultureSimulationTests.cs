using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace LittleAges.Simulation.Tests;

public sealed class AgricultureSimulationTests(ITestOutputHelper output)
{
    [Fact]
    public void UnderTendedCropHasPoorYieldAndNextSeasonRecoversThroughWork()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.AgricultureSimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(179L * WorldCalendar.MinutesPerDay));
        // A diagnostic labor-loss fixture: discard completed tending, never add goods,
        // citizens, health, or completed structures. All subsequent work is autonomous.
        var farms = (SortedDictionary<long, FarmCrop>)typeof(SimulationEngine).GetField("_farms", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(engine)!;
        Assert.NotEmpty(farms);
        foreach (var farm in farms.Values.ToArray()) farms[farm.StructureId] = farm with { TendingWork = 0 };
        engine.AdvanceUntil(new WorldMinute(180L * WorldCalendar.MinutesPerDay));
        var poor = engine.CaptureAgriculture()!.Farms.Sum(f => f.Yield);
        Assert.True(poor > 0);
        engine.AdvanceUntil(new WorldMinute(540L * WorldCalendar.MinutesPerDay));
        Assert.True(engine.CaptureAgriculture()!.Farms.Sum(f => f.Yield) > poor);
        Assert.True(engine.LivingPopulation > 0);
        Assert.Contains(engine.StatisticsSamples, s => s.FoodProducedPeriod > 0 && s.FoodConsumedPeriod > 0);
        _ = engine.CreatePersistenceSnapshot();
    }

    [Fact]
    public void CropValidationRejectsInventedYieldAndSeasonMismatch()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.AgricultureSimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(40L * WorldCalendar.MinutesPerDay));
        var state = engine.CaptureAgriculture()!;
        Assert.NotEmpty(state.Farms);
        var invented = state with { Farms = state.Farms.Select(f => f with { Yield = 1 }).ToArray() };
        Assert.Throws<ArgumentException>(() => invented.Validate(engine.World, engine.Structures, engine.Citizens, engine.Settlement.DemandUpdatedMinute));
        Assert.Throws<ArgumentException>(() => (state with { SeasonIndex = 1 }).Validate(engine.World, engine.Structures, engine.Citizens, engine.Settlement.DemandUpdatedMinute));
        Assert.Throws<System.Text.Json.JsonException>(() => AgricultureState.Parse("{\"Version\":1}"));
    }

    [Fact]
    public void SeasonalCycleProducesHarvestsAndKeepsSnapshotsValid()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.AgricultureSimulationRulesVersion);
        var deaths = 0;
        for (var day = 1; day <= 360; day++)
        {
            var before = engine.Citizens.ToDictionary(c => c.Id.Value);
            engine.AdvanceUntil(new WorldMinute((long)day * WorldCalendar.MinutesPerDay));
            if (engine.DeadPopulation > deaths)
            {
                foreach (var dead in engine.Citizens.Where(c => !c.IsAlive && before[c.Id.Value].IsAlive))
                {
                    var previous = before[dead.Id.Value];
                    output.WriteLine($"death day={day} id={dead.Id} cause={dead.DeathCause} before={previous.CurrentAction}/{previous.ActionPhase} needs={previous.Needs} health={previous.Health} location={previous.Location} target={previous.ActionTarget} completes={previous.ActionCompletesMinute} food={engine.Settlement.FoodStored} capacity={engine.StorageCapacity}");
                }
                deaths = engine.DeadPopulation;
            }
            foreach (var person in engine.Citizens)
            {
                try { person.Validate(engine.World); }
                catch (ArgumentException) { output.WriteLine($"invalid day={day} id={person.Id} action={person.CurrentAction}/{person.ActionPhase} carry={person.CarriedResourceQuantity} target={person.ActionTarget} completes={person.ActionCompletesMinute}"); throw; }
            }
            _ = engine.CreatePersistenceSnapshot();
        }
        var agriculture = Assert.IsType<AgricultureState>(engine.CaptureAgriculture());
        Assert.NotEmpty(agriculture.Harvests);
        Assert.Contains(agriculture.Harvests, h => h.PlantingWork > 0 && h.TendingWork > 0 && h.Harvested > 0);
        Assert.All(agriculture.Harvests, h => Assert.Equal(h.Yield, h.Harvested + h.Lost));
        Assert.Equal(20, engine.LivingPopulation);
    }
}
