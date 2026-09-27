using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M15RoadPersistenceTests
{
    private const long Season = 90L * WorldCalendar.MinutesPerDay;

    [Fact]
    public async Task RoadStateAndActiveRoutesSurviveSqliteReopenAcrossAGradeChange()
    {
        var seed = new WorldSeed(42);
        var target = new WorldMinute(Season + 2L * WorldCalendar.MinutesPerDay);
        var uninterrupted = new SimulationEngine(seed, simulationRulesVersion: SimulationEngine.RoadsSimulationRulesVersion);
        // Checkpoint while someone is mid-route, shortly before the season's grading pass.
        var minute = Season - 12L * 60;
        uninterrupted.AdvanceUntil(new WorldMinute(minute));
        var checkpoint = uninterrupted.CreatePersistenceSnapshot();
        while (checkpoint.MigrationState!.Roads!.ActiveRoutes.Count == 0 && minute < Season - 10)
        {
            uninterrupted.AdvanceUntil(new WorldMinute(minute += 10));
            checkpoint = uninterrupted.CreatePersistenceSnapshot();
        }
        Assert.NotEmpty(checkpoint.MigrationState!.Roads!.Tiles);
        Assert.NotEmpty(checkpoint.MigrationState.Roads.ActiveRoutes);

        var root = Path.Combine(Path.GetTempPath(), "littleages-m15-roads-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "world.db");
        try
        {
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(checkpoint);

            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                Assert.Equal(SimulationEngine.RoadsSimulationRulesVersion, loaded.SimulationRulesVersion);
                Assert.Equal(checkpoint.MigrationStateJson, loaded.MigrationStateJson);
                var reopened = SimulationEngine.FromPersistenceSnapshot(loaded);
                uninterrupted.AdvanceUntil(target);
                reopened.AdvanceUntil(target);

                var expected = uninterrupted.CreatePersistenceSnapshot();
                var actual = reopened.CreatePersistenceSnapshot();
                Assert.Contains(actual.MigrationState!.Roads!.Tiles, x => x.Grade != RoadGrade.None);
                Assert.Equal(expected.MigrationStateJson, actual.MigrationStateJson);
                Assert.Equal(expected.LivingStateJson, actual.LivingStateJson);
                Assert.Equal(expected.Citizens, actual.Citizens);
                Assert.Equal(uninterrupted.HistoryFingerprint, reopened.HistoryFingerprint);
                Assert.Equal(uninterrupted.SettlementFingerprint, reopened.SettlementFingerprint);
                await database.CreateCheckpointStore().CheckpointAsync(actual);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AnM14DatabaseCannotBeCheckpointedWithM15Rules()
    {
        var root = Path.Combine(Path.GetTempPath(), "littleages-m15-convert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "world.db");
        try
        {
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(new SimulationEngine(new WorldSeed(7),
                    simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion).CreatePersistenceSnapshot());
            await using (var database = await WorldDatabase.OpenAsync(path))
                await Assert.ThrowsAsync<InvalidDataException>(() => database.CreateCheckpointStore().CheckpointAsync(
                    new SimulationEngine(new WorldSeed(7), simulationRulesVersion: SimulationEngine.RoadsSimulationRulesVersion).CreatePersistenceSnapshot()));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}
