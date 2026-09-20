using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class AgriculturePersistenceTests
{
    [Theory]
    [InlineData(CitizenAction.WorkFarm, 0, SimulationEngine.AgricultureSimulationRulesVersion)]
    [InlineData(CitizenAction.HaulHarvest, 180, SimulationEngine.AgricultureSimulationRulesVersion)]
    [InlineData(CitizenAction.WorkFarm, 0, SimulationEngine.BarterSimulationRulesVersion)]
    [InlineData(CitizenAction.HaulHarvest, 180, SimulationEngine.BarterSimulationRulesVersion)]
    public async Task ActiveFarmWorkAndHarvestTransitSurviveRealSqliteReopen(CitizenAction action, int startDay, string rules)
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-Agriculture", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "world.db");
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: rules);
            engine.AdvanceUntil(new WorldMinute(startDay * (long)WorldCalendar.MinutesPerDay));
            var limit = engine.CurrentMinute.Value + 90L * WorldCalendar.MinutesPerDay;
            while (!engine.Citizens.Any(c => c.CurrentAction == action && c.ActionPhase == (action == CitizenAction.HaulHarvest ? CitizenActionPhase.ReturnToStockpile : CitizenActionPhase.Perform)) && engine.CurrentMinute.Value < limit)
                engine.AdvanceUntil(engine.CurrentMinute.Add(60));
            Assert.True(engine.CurrentMinute.Value < limit, "The autonomous simulation must reach the requested work phase.");
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot());
            SimulationEngine reopened;
            await using (var database = await WorldDatabase.OpenAsync(path))
                reopened = SimulationEngine.FromPersistenceSnapshot(await database.CreateCheckpointStore().LoadAsync());
            Assert.Equal(engine.ComputeAgricultureFingerprint(), reopened.ComputeAgricultureFingerprint());
            var target = engine.CurrentMinute.Add(14400);
            engine.AdvanceUntil(target);
            reopened.AdvanceUntil(target);
            Assert.Equal(engine.ComputeSocialFingerprint(), reopened.ComputeSocialFingerprint());
            Assert.Equal(engine.ComputeHistoryFingerprint(), reopened.ComputeHistoryFingerprint());
            Assert.Equal(engine.CaptureAgriculture()!.ToCanonicalJson(), reopened.CaptureAgriculture()!.ToCanonicalJson());
            Assert.Equal(engine.ComputeEconomyFingerprint(), reopened.ComputeEconomyFingerprint());
            _ = reopened.CreatePersistenceSnapshot();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
