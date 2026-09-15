using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M3PersistenceTests
{
    [Fact]
    public async Task FreshM3CheckpointRoundTripsMutableState()
    {
        await WithDatabaseAsync(async path =>
        {
            var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M3SimulationRulesVersion);
            await using var database = await WorldDatabase.OpenAsync(path);
            await database.CreateCheckpointStore().CheckpointAsync(source.CreatePersistenceSnapshot(), DateTime.UtcNow);
            var loaded = await database.CreateCheckpointStore().LoadAsync();

            Assert.Equal(SimulationEngine.SurvivalVersion, loaded.SurvivalVersion);
            Assert.Equal(source.Settlement, loaded.Settlement);
            Assert.Equal(source.ResourceStates, loaded.ResourceStates);
            Assert.Equal(source.Citizens, loaded.Citizens);
            Assert.Equal(source.World!.Fingerprint, loaded.World!.Fingerprint);
            Assert.Equal(source.World.Resources.Count, await database.Context.ResourceStates.CountAsync());
            Assert.Equal(1, await database.Context.SettlementStates.CountAsync());
        });
    }

    [Fact]
    public async Task M2ToM3UpgradeRollsBackAndRetriesDeterministically()
    {
        await WithDatabaseAsync(async path =>
        {
            var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M2SimulationRulesVersion);
            var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
            var options = new DbContextOptionsBuilder<LittleAgesDbContext>().UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(WorldDatabase).Assembly.GetName().Name)).Options;
            await using (var context = new LittleAgesDbContext(options))
            {
                await context.Database.MigrateAsync();
                await new WorldCheckpointStore(context).CheckpointAsync(source.CreatePersistenceSnapshot(), DateTime.UtcNow);
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => WorldDatabase.OpenAsync(path, new WorldDatabaseOpenOptions(null, null, null, M3UpgradeFailurePoint.AfterRowsWritten)));
            await using (var verify = new SqliteConnection(connectionString))
            {
                await verify.OpenAsync();
                await using var command = verify.CreateCommand();
                command.CommandText = "SELECT survival_version, (SELECT COUNT(*) FROM resource_state), (SELECT COUNT(*) FROM settlement_state), (SELECT COUNT(*) FROM scheduled_events WHERE event_name IN ('citizen.survival-check.v1', 'resource.regenerate.v1')) FROM world_meta;";
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(0, reader.GetInt32(0));
                Assert.Equal(0L, reader.GetInt64(1));
                Assert.Equal(0L, reader.GetInt64(2));
                Assert.Equal(0L, reader.GetInt64(3));
            }

            await using var retried = await WorldDatabase.OpenAsync(path);
            var upgraded = await retried.CreateCheckpointStore().LoadAsync();
            Assert.Equal(SimulationEngine.SurvivalVersion, upgraded.SurvivalVersion);
            Assert.Equal(400, upgraded.Settlement!.FoodStored);
            Assert.Equal(source.World!.Resources.Count, upgraded.ResourceStates.Count);
            Assert.Equal(source.Citizens, upgraded.Citizens);
            Assert.Equal(source.World.Fingerprint, upgraded.World!.Fingerprint);
        });
    }

    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LittleAges-M3-Persistence-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "world.db");
        try { await test(path); }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var file = path + suffix;
                if (File.Exists(file)) File.Delete(file);
            }
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
