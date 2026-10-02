using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class NewWorldDefaultTests
{
    [Fact]
    public void ImplicitNewWorldMatchesExplicitM17AcrossChunksAndRestore()
    {
        var implicitWorld = new SimulationEngine(new WorldSeed(7));
        var explicitWorld = new SimulationEngine(new WorldSeed(7),
            simulationRulesVersion: SimulationEngine.NewcomersRulesVersion);

        Assert.Equal(SimulationEngine.NewcomersRulesVersion, implicitWorld.SimulationRulesVersion);
        Assert.NotNull(LivingWorldCodec.Deserialize(implicitWorld.LivingStateJson!).Newcomers);
        Assert.Equal(JsonSerializer.Serialize(explicitWorld.CreatePersistenceSnapshot()),
            JsonSerializer.Serialize(implicitWorld.CreatePersistenceSnapshot()));

        var end = 7L * WorldCalendar.MinutesPerDay;
        explicitWorld.AdvanceUntil(new WorldMinute(end));
        while (implicitWorld.CurrentMinute.Value < end)
            implicitWorld.AdvanceUntil(new WorldMinute(Math.Min(end, implicitWorld.CurrentMinute.Value + 37)));

        var snapshot = implicitWorld.CreatePersistenceSnapshot();
        Assert.Equal(JsonSerializer.Serialize(explicitWorld.CreatePersistenceSnapshot()), JsonSerializer.Serialize(snapshot));
        var restored = SimulationEngine.FromPersistenceSnapshot(snapshot);
        Assert.Equal(SimulationEngine.NewcomersRulesVersion, restored.SimulationRulesVersion);
        Assert.Equal(JsonSerializer.Serialize(snapshot), JsonSerializer.Serialize(restored.CreatePersistenceSnapshot()));
    }

    [Theory]
    [InlineData(SimulationEngine.PlannedSimulationRulesVersion)]
    [InlineData(SimulationEngine.FestivalsSimulationRulesVersion)]
    public void RecordedM16RulesAndCanonicalStateSurviveRestore(string rules)
    {
        var legacy = new SimulationEngine(new WorldSeed(17), simulationRulesVersion: rules);
        legacy.AdvanceUntil(new WorldMinute(3L * WorldCalendar.MinutesPerDay));
        var snapshot = legacy.CreatePersistenceSnapshot();
        var restored = SimulationEngine.FromPersistenceSnapshot(snapshot);

        Assert.Equal(rules, restored.SimulationRulesVersion);
        Assert.Null(LivingWorldCodec.Deserialize(restored.LivingStateJson!).Newcomers);
        Assert.Equal(JsonSerializer.Serialize(snapshot), JsonSerializer.Serialize(restored.CreatePersistenceSnapshot()));
    }
}
