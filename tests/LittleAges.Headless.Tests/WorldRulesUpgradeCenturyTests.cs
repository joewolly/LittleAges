using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LittleAges.Headless.Tests;

public sealed class WorldRulesUpgradeCenturyTests
{
    [Theory]
    [Trait("Category", "Long")]
    [InlineData(17UL, SimulationEngine.RoadsSimulationRulesVersion)]
    [InlineData(42UL, SimulationEngine.MigrationSimulationRulesVersion)]
    public async Task MigratedWorldReachesCenturyWithValidatedReload(ulong seed, string rules)
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-UpgradeCentury", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var progressDirectory = Environment.GetEnvironmentVariable("LITTLEAGES_UPGRADE_CENTURY_LOG_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(progressDirectory);
        var progress = Path.Combine(progressDirectory, $"LittleAges-UpgradeCentury-{seed}.log");
        await File.WriteAllTextAsync(progress, $"Starting seed {seed} on {rules}\n");
        try
        {
            var original = new SimulationEngine(new WorldSeed(seed), simulationRulesVersion: rules);
            original.AdvanceUntil(new WorldCalendarDate(1, 1, 1, 0, 0).ToWorldMinute());
            var source = original.CreatePersistenceSnapshot();
            var path = Path.Combine(root, "world.db");
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(source);
            var backup = Assert.IsType<WorldRulesBackup>(await WorldRulesBackup.PrepareAsync(path));
            var plan = WorldRulesUpgrades.Plan(source);
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().UpgradeRulesAsync(plan, backup);
            var engine = new SimulationEngine(plan.Snapshot);
            for (var year = 2; year <= 100; year++)
            {
                var target = new WorldCalendarDate(year, 1, 1, 0, 0).ToWorldMinute();
                engine.AdvanceUntil(target);
                var snapshot = engine.CreatePersistenceSnapshot();
                Assert.All(HeadlessInvariantValidator.Validate(engine, snapshot, target.Value), invariant => Assert.True(invariant.Passed, invariant.Details));
                if (year == 50 || year == 100)
                {
                    await using var database = await WorldDatabase.OpenAsync(path);
                    var store = database.CreateCheckpointStore();
                    await store.CheckpointAsync(snapshot);
                    var saved = await store.LoadAsync();
                    Assert.Equal(WorldRulesUpgrades.Fingerprint(snapshot), WorldRulesUpgrades.Fingerprint(saved));
                    // Compare continuation of the in-memory checkpoint with a reloaded,
                    // differently chunked engine at the midpoint.
                    if (year == 50)
                    {
                        var reference = new SimulationEngine(snapshot);
                        var resumed = new SimulationEngine(saved);
                        var end = target.Add(WorldCalendar.MinutesPerDay);
                        reference.AdvanceUntil(end);
                        resumed.AdvanceUntil(target.Add(720));
                        resumed.AdvanceUntil(end);
                        Assert.Equal(WorldRulesUpgrades.Fingerprint(reference.CreatePersistenceSnapshot()), WorldRulesUpgrades.Fingerprint(resumed.CreatePersistenceSnapshot()));
                    }
                    engine = new SimulationEngine(saved);
                }
                if (year % 10 == 0)
                    await File.AppendAllTextAsync(progress, $"Year {year}: population {engine.LivingPopulation}; checkpoint {WorldRulesUpgrades.Fingerprint(snapshot)}\n");
            }
            Assert.Empty(WorldRulesUpgrades.Plan(engine.CreatePersistenceSnapshot()).Converters);
            await File.AppendAllTextAsync(progress, "PASS: century, invariants, checkpoint reload, deterministic continuation\n");
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
