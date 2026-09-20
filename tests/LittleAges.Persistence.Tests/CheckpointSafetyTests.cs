using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class CheckpointSafetyTests
{
    [Fact]
    public async Task OrphanedHistoryStateCannotBeOpenedAsAnEmptyWorld()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LittleAges-checkpoint-safety", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "world.db");
        try
        {
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                database.Context.HistoryStates.Add(new HistoryStateRow
                {
                    Id = 1, HistoryStartEventId = 1, HistoryStartMinute = 43200, PeriodStartMinute = 43200
                });
                await database.Context.SaveChangesAsync();
                await Assert.ThrowsAsync<InvalidDataException>(() => database.HasCheckpointAsync());
            }
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                await using var reopened = await WorldDatabase.OpenAsync(path);
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckpointRejectsChangedImmutableRowsWithoutCommittingNewMetadata(bool resource)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LittleAges-checkpoint-safety", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M8SimulationRulesVersion);
            await using var database = await WorldDatabase.OpenAsync(Path.Combine(directory, "world.db"));
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(engine.CreatePersistenceSnapshot());
            // A valid-range edit leaves row counts unchanged. A successful next
            // checkpoint must not mask it until a later restart fails.
            var sql = resource
                ? "UPDATE resource_nodes SET regeneration_potential = (regeneration_potential + 1) % 10001 WHERE id = (SELECT MIN(id) FROM resource_nodes);"
                : "UPDATE world_tiles SET elevation = (elevation + 1) % 10001 WHERE tile_index = 0;";
            await database.Context.Database.ExecuteSqlRawAsync(sql);
            engine.AdvanceUntil(new WorldMinute(1));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.CheckpointAsync(engine.CreatePersistenceSnapshot()));
            Assert.Equal(0, (await database.Context.WorldMeta.AsNoTracking().SingleAsync()).WorldMinute);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
