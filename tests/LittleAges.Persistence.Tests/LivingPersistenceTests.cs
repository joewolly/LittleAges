using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class LivingPersistenceTests
{
    [Theory]
    [InlineData(LivingWorkKind.Sow)]
    [InlineData(LivingWorkKind.Preserve)]
    [InlineData(LivingWorkKind.Care)]
    [InlineData(LivingWorkKind.Teach)]
    public async Task ActiveFarmingProductionCareAndTeachingSurviveSqliteReload(LivingWorkKind kind)
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.LivingSimulationRulesVersion);
        LivingWorldState state;
        do
        {
            engine.AdvanceUntil(engine.CurrentMinute.Add(30));
            state = LivingWorldCodec.Deserialize(engine.LivingStateJson!);
        }
        while (engine.CurrentMinute.Value < 120L * WorldCalendar.MinutesPerDay &&
            !state.Orders.Any(x => x.Kind == kind && x.CitizenId is not null && x.Phase == LivingWorkPhase.Work));
        Assert.Contains(state.Orders, x => x.Kind == kind && x.CitizenId is not null && x.Phase == LivingWorkPhase.Work);
        var directory = Path.Combine(Path.GetTempPath(), "littleages-living-active-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "world.db");
        try
        {
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot());
            await using var reopened = await WorldDatabase.OpenAsync(path);
            var resumed = new SimulationEngine(await reopened.CreateCheckpointStore().LoadAsync());
            Assert.Equal(engine.LivingStateJson, resumed.LivingStateJson);
            var target = engine.CurrentMinute.Add(7L * WorldCalendar.MinutesPerDay);
            engine.AdvanceUntil(target);
            while (resumed.CurrentMinute < target)
                resumed.AdvanceUntil(new WorldMinute(Math.Min(target.Value, resumed.CurrentMinute.Value + 97)));
            Assert.Equal(engine.HistoryFingerprint, resumed.HistoryFingerprint);
            Assert.Equal(engine.LivingStateJson, resumed.LivingStateJson);
            await reopened.CreateCheckpointStore().CheckpointAsync(resumed.CreatePersistenceSnapshot());
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(4777, 30000)]
    [InlineData(42000, 50000)]
    [InlineData(160000, 172800)]
    public async Task RealSqliteReloadAndFailedCheckpointPreserveEveryLivingComponent(long seam, long target)
    {
        var directory = Path.Combine(Path.GetTempPath(), "littleages-living-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "world.db");
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.LivingSimulationRulesVersion);
            engine.AdvanceUntil(new WorldMinute(seam));
            var committed = engine.CreatePersistenceSnapshot();
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var store = database.CreateCheckpointStore();
                await store.CheckpointAsync(committed);
                engine.AdvanceUntil(new WorldMinute(seam + 4000));
                await Assert.ThrowsAsync<InvalidOperationException>(() => store.CheckpointAsync(engine.CreatePersistenceSnapshot(), DateTime.UtcNow, CheckpointFailurePoint.AfterRowsWritten));
                Assert.Equal(committed.LivingStateJson, (await store.LoadAsync()).LivingStateJson);
            }
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                Assert.Equal(SimulationEngine.LivingSimulationRulesVersion, loaded.SimulationRulesVersion);
                var resumed = new SimulationEngine(loaded);
                resumed.AdvanceUntil(new WorldMinute(target));
                engine.AdvanceUntil(new WorldMinute(target));
                Assert.Equal(engine.HistoryFingerprint, resumed.HistoryFingerprint);
                Assert.Equal(engine.LivingStateJson, resumed.LivingStateJson);
                await database.CreateCheckpointStore().CheckpointAsync(resumed.CreatePersistenceSnapshot());
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void RetainedLivingFactsCannotBeDeletedOrRewritten()
    {
        var previous = new LivingWorldState();
        previous.Facts.Add(new LivingFact(1, 10, LivingFactKind.TechniqueDiscovered, 1, null, null, 1));
        var original = LivingWorldCodec.Serialize(previous);
        previous.Facts[0] = previous.Facts[0] with { Value = 2 };
        Assert.Throws<InvalidDataException>(() => LivingValidation.ValidateRetainedFacts(original, LivingWorldCodec.Serialize(previous)));
        previous.Facts.Clear();
        Assert.Throws<InvalidDataException>(() => LivingValidation.ValidateRetainedFacts(original, LivingWorldCodec.Serialize(previous)));
        Assert.Throws<InvalidDataException>(() => LivingValidation.ValidateRetainedFacts(original, null));
    }
}
