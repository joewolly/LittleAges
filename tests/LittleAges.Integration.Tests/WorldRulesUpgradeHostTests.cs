using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Server;
using LittleAges.Simulation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LittleAges.Integration.Tests;

public sealed class WorldRulesUpgradeHostTests
{
    [Theory]
    [InlineData(true, null, "Upgraded")]
    [InlineData(false, null, "Preserved")]
    [InlineData(true, "backup", "Failed")]
    [InlineData(true, "schema", "Failed")]
    [InlineData(true, "conversion", "Failed")]
    [InlineData(true, "afterCommit", "Upgraded")]
    public async Task StartupUpgradeFailureAndPreservationAreObservable(bool enabled, string? failureStage, string outcome)
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-RulesUpgradeHost", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "world.db");
            var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion).CreatePersistenceSnapshot();
            string? identity;
            await using (var database = await WorldDatabase.OpenAsync(path)) await database.CreateCheckpointStore().CheckpointAsync(source);
            using (var preservedHost = BuildHost(root, false))
            {
                await preservedHost.StartAsync();
                var simulation = preservedHost.Services.GetRequiredService<SimulationHost>();
                await simulation.WaitForRunningForTestingAsync().WaitAsync(TimeSpan.FromSeconds(30));
                identity = simulation.Status.WorldInstanceId;
                await preservedHost.StopAsync();
            }
            using (var host = BuildHost(root, enabled))
            {
                var simulation = host.Services.GetRequiredService<SimulationHost>();
                simulation.RulesUpgradeFailureHookForTesting = stage => stage == failureStage ? new InvalidOperationException("Controlled rules upgrade failure.") : null;
                await host.StartAsync();
                await simulation.WaitForRunningForTestingAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Equal(outcome, simulation.Status.RulesUpgrade!.State);
                Assert.Equal(identity, simulation.Status.WorldInstanceId);
                Assert.Equal(source.WorldMinute.Value, simulation.Status.WorldMinute);
                Assert.Equal(source.Citizens.Count, simulation.Status.TotalPopulation);
                Assert.Equal(outcome == "Upgraded" ? SimulationEngine.CurrentSimulationRulesVersion : source.SimulationRulesVersion, simulation.Status.SimulationRulesVersion);
                await simulation.AdvanceForTestingAsync(60);
                await host.StopAsync();
            }
            var backupsBefore = Directory.Exists(Path.Combine(root, "rules-upgrade-backups"))
                ? Directory.GetFiles(Path.Combine(root, "rules-upgrade-backups"), "world.db", SearchOption.AllDirectories).Length : 0;
            if (outcome == "Upgraded")
            {
                using var restarted = BuildHost(root, true);
                await restarted.StartAsync();
                var simulation = restarted.Services.GetRequiredService<SimulationHost>();
                await simulation.WaitForRunningForTestingAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Equal("Current", simulation.Status.RulesUpgrade!.State);
                Assert.Equal(backupsBefore, Directory.GetFiles(Path.Combine(root, "rules-upgrade-backups"), "world.db", SearchOption.AllDirectories).Length);
                await restarted.StopAsync();
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(SimulationEngine.MigrationSimulationRulesVersion, true, true)]
    [InlineData(SimulationEngine.MigrationSimulationRulesVersion, false, false)]
    [InlineData(SimulationEngine.CurrentSimulationRulesVersion, true, true)]
    [InlineData(SimulationEngine.CurrentSimulationRulesVersion, true, false)]
    [InlineData(SimulationEngine.CurrentSimulationRulesVersion, false, false)]
    public async Task PendingSchemaMigrationRequiresBackupEvenInPreservationMode(string rules, bool enabled, bool failBackup)
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-UpgradeSchema", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "world.db");
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(new SimulationEngine(new WorldSeed(42), simulationRulesVersion: rules).CreatePersistenceSnapshot());
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "DROP TABLE world_rules_upgrades; DELETE FROM __EFMigrationsHistory WHERE MigrationId='20261007000000_WorldRulesUpgrades';";
                await command.ExecuteNonQueryAsync();
            }
            using var host = BuildHost(root, enabled);
            var simulation = host.Services.GetRequiredService<SimulationHost>();
            simulation.RulesUpgradeFailureHookForTesting = stage => failBackup && stage == "backup" ? new IOException("Backup unavailable.") : null;
            await host.StartAsync();
            await simulation.WaitForRunningForTestingAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(failBackup ? "Failed" : enabled ? "Current" : "Preserved", simulation.Status.RulesUpgrade!.State);
            Assert.Equal(rules, simulation.Status.SimulationRulesVersion);
            await host.StopAsync();
            await using var check = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            await check.OpenAsync();
            await using var query = check.CreateCommand();
            query.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='world_rules_upgrades';";
            Assert.Equal(failBackup ? 0L : 1L, await query.ExecuteScalarAsync());
            if (!failBackup)
            {
                var backupPath = Assert.Single(Directory.GetFiles(Path.Combine(root, "rules-upgrade-backups"), "world.db", SearchOption.AllDirectories));
                await using var backupCheck = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={backupPath};Mode=ReadOnly;Pooling=False");
                await backupCheck.OpenAsync();
                await using var backupQuery = backupCheck.CreateCommand();
                backupQuery.CommandText = query.CommandText;
                Assert.Equal(0L, await backupQuery.ExecuteScalarAsync());
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task UncertainCommittedOutcomeStopsStartupWithoutAdvancing()
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-UpgradeUncertain", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "world.db");
            var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion).CreatePersistenceSnapshot();
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(source);
            using var host = BuildHost(root, true);
            var simulation = host.Services.GetRequiredService<SimulationHost>();
            simulation.RulesUpgradeFailureHookForTesting = stage =>
            {
                if (stage != "afterCommit") return null;
                // Simulate a valid checkpoint that matches neither side of the planned commit.
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE world_meta SET application_version = application_version || '-uncertain';";
                command.ExecuteNonQuery();
                return new IOException("Controlled ambiguous commit outcome.");
            };
            await host.StartAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => simulation.WaitForRunningForTestingAsync().WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Equal(SimulationHostState.Faulted, simulation.Status.State);
            Assert.Contains("uncertain", simulation.Status.Error, StringComparison.OrdinalIgnoreCase);
            await Assert.ThrowsAsync<InvalidDataException>(() => host.StopAsync());
            await using var check = await WorldDatabase.OpenExistingWithoutMigrationsAsync(path);
            var committed = await check.CreateCheckpointStore().LoadAsync();
            Assert.Equal(source.WorldMinute, committed.WorldMinute);
            Assert.Equal(SimulationEngine.CurrentSimulationRulesVersion, committed.SimulationRulesVersion);
            Assert.NotEqual(WorldRulesUpgrades.Plan(source).TargetFingerprint, WorldRulesUpgrades.Fingerprint(committed));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public void ConfigurationDefaultsToAutoAndRejectsAmbiguousValues()
    {
        Assert.True(ServerOptions.FromConfiguration(new ConfigurationBuilder().Build()).AutoUpgradeWorldRules);
        Assert.False(ServerOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["AutoUpgradeWorldRules"] = "false" }).Build()).AutoUpgradeWorldRules);
        Assert.Throws<ArgumentException>(() => ServerOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["AutoUpgradeWorldRules"] = "maybe" }).Build()));
    }

    private static IHost BuildHost(string root, bool enabled)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(new ServerOptions { DataRoot = root, ActiveWorld = "world", WorldSeed = new WorldSeed(42),
            ListenUrls = "http://127.0.0.1:0", AutoUpgradeWorldRules = enabled, SimulationMinutesPerSecond = 0 });
        builder.Services.AddSingleton<SimulationHost>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SimulationHost>());
        return builder.Build();
    }
}
