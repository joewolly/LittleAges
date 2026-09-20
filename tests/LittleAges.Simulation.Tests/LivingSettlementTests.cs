using LittleAges.Domain;
using Xunit;
using Xunit.Abstractions;

namespace LittleAges.Simulation.Tests;

public sealed class LivingSettlementTests(ITestOutputHelper output)
{
    private static SimulationEngine Create(ulong seed = 42) => new(new WorldSeed(seed), simulationRulesVersion: SimulationEngine.LivingSimulationRulesVersion);

    [Fact]
    public void FreshWorldHasCompleteCanonicalStateAndLegacyWorldsHaveNone()
    {
        var engine = Create();
        var snapshot = engine.CreatePersistenceSnapshot();
        var state = LivingWorldCodec.Deserialize(snapshot.LivingStateJson!);
        Assert.Equal(snapshot.LivingStateJson, LivingWorldCodec.Serialize(state));
        Assert.Equal(20, state.People.Count);
        Assert.Single(snapshot.ScheduledEvents, x => x.Name == SimulationEngine.LivingPulseEvent);
        Assert.Null(new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion).CreatePersistenceSnapshot().LivingStateJson);
    }

    [Fact]
    public void ChunkingAndReloadPreserveJobsWeatherAndPersonalState()
    {
        var uninterrupted = Create();
        var split = Create();
        split.AdvanceUntil(new WorldMinute(4777));
        CheckCitizens(split);
        var checkpoint = split.CreatePersistenceSnapshot();
        split = new SimulationEngine(checkpoint);
        for (var minute = 5000; minute < 30000; minute += 317) { split.AdvanceUntil(new WorldMinute(minute)); _ = split.CreateReadSnapshot(); }
        split.AdvanceUntil(new WorldMinute(30000));
        uninterrupted.AdvanceUntil(new WorldMinute(30000));
        Assert.Equal(uninterrupted.HistoryFingerprint, split.HistoryFingerprint);
        Assert.Equal(uninterrupted.LivingStateJson, split.CreatePersistenceSnapshot().LivingStateJson);
    }

    [Fact]
    public void SettlementAutonomouslyDiscoversCultivatesAndProducesFood()
    {
        var engine = Create();
        for (var day = 1; day <= 120; day++)
        {
            engine.AdvanceUntil(new WorldMinute(day * WorldCalendar.MinutesPerDay));
            CheckCitizens(engine);
            var snapshot = engine.CreatePersistenceSnapshot();
            if (day % 15 != 0) continue;
            var state = LivingWorldCodec.Deserialize(snapshot.LivingStateJson!);
            output.WriteLine($"day={day} pop={engine.LivingPopulation} food={engine.Settlement.FoodStored} wood={engine.Settlement.WoodStored} stone={engine.Settlement.StoneStored} structures={engine.Structures.Count} fields={state.Fields.Count} harvest={state.FoodHarvested} prepared={state.FoodPrepared} orders={state.CompletedOrders} tech={string.Join(',', state.People.SelectMany(x => x.Knowledge).Distinct())}");
            output.WriteLine(string.Join(';', state.Orders.Take(12).Select(x => $"{x.Kind}:{x.CitizenId}:{x.WorkDone}/{x.RequiredWork}:{x.BlockedReason}")));
        }
        var final = LivingWorldCodec.Deserialize(engine.LivingStateJson!);
        Assert.NotEmpty(final.Fields);
        Assert.True(final.FoodHarvested > 0);
        Assert.True(final.FoodPrepared > 0);
        Assert.Contains(final.Facts, x => x.Kind == LivingFactKind.TechniqueDiscovered);
        Assert.Contains(final.Facts, x => x.Kind == LivingFactKind.TechniqueTaught);
        Assert.Contains(final.Facilities, x => x.Kind == LivingFacilityKind.Hearth);
    }
    private static void CheckCitizens(SimulationEngine engine)
    {
        foreach (var citizen in engine.Citizens)
        {
            try { citizen.Validate(engine.World); }
            catch (ArgumentException error) { throw new InvalidOperationException($"minute={engine.CurrentMinute.Value} citizen={System.Text.Json.JsonSerializer.Serialize(citizen)}", error); }
        }
    }

}
