using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M8HousingRegressionTests
{
    [Fact]
    public async Task SettlementDemandDoesNotSplitAnUnhousedHouseholdAndContinuationReopens()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LittleAges-M8-Housing", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Production failure: seed 20260918, 160x160, first failed checkpoint 177600.
            var configuration = WorldGenerationConfiguration.Default with { Width = 160, Height = 160 };
            var engine = new SimulationEngine(new WorldSeed(20260918), simulationRulesVersion: SimulationEngine.M8SimulationRulesVersion, worldConfiguration: configuration.CanonicalJson);
            engine.AdvanceUntil(new WorldMinute(177240));
            engine.AdvanceUntil(new WorldMinute(177600));
            var snapshot = engine.CreatePersistenceSnapshot();
            foreach (var household in snapshot.Households.Where(h => h.DissolvedMinute is null))
                Assert.All(snapshot.Citizens.Where(c => c.IsAlive && c.HouseholdId == household.Id), c => Assert.Equal(household.DwellingStructureId, c.HomeStructureId));
            await using var database = await WorldDatabase.OpenAsync(Path.Combine(directory, "world.db"));
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(snapshot);
            var restored = SimulationEngine.FromPersistenceSnapshot(await store.LoadAsync());
            engine.AdvanceUntil(new WorldMinute(178560));
            restored.AdvanceUntil(new WorldMinute(178560));
            Assert.Equal(engine.HistoryFingerprint, restored.HistoryFingerprint);
            await store.CheckpointAsync(restored.CreatePersistenceSnapshot());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
