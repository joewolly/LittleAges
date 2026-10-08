using LittleAges.Domain;
using LittleAges.Simulation;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class WorldRulesUpgradePersistenceTests
{
    [Theory]
    [InlineData(SimulationEngine.MigrationSimulationRulesVersion)]
    [InlineData(SimulationEngine.RoadsSimulationRulesVersion)]
    [InlineData(SimulationEngine.PlannedSimulationRulesVersion)]
    [InlineData(SimulationEngine.FestivalsSimulationRulesVersion)]
    public async Task LegacyDaughterFarmCargoIsReroutedPhysicallyAndSurvivesReload(string rules)
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-RulesUpgradeFarm", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var engine = M17FarmReturnPersistenceTests.CreateHarvestReturn(rules);
            var source = engine.CreatePersistenceSnapshot();
            var worker = Assert.Single(source.Citizens, c => c.CurrentAction == CitizenAction.HaulHarvest && c.ActionPhase == CitizenActionPhase.ReturnToStockpile);
            var plan = WorldRulesUpgrades.Plan(source);
            var convertedWorker = plan.Snapshot.Citizens.Single(c => c.Id == worker.Id);
            Assert.Equal(worker.Location, convertedWorker.Location);
            Assert.Equal(worker.CarriedResourceQuantity, convertedWorker.CarriedResourceQuantity);
            Assert.Equal(source.MigrationState!.DaughterSettlement!.Site, convertedWorker.ActionTarget);
            Assert.Equal(worker.ActionSequence, convertedWorker.ActionSequence);
            await using var database = await WorldDatabase.OpenAsync(Path.Combine(root, "world.db"));
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(source);
            var backup = (await WorldRulesBackup.PrepareAsync(database.DatabasePath))!;
            await store.UpgradeRulesAsync(plan, backup);
            var resumed = new SimulationEngine(await store.LoadAsync());
            var reference = new SimulationEngine(plan.Snapshot);
            var end = source.WorldMinute.Add(120);
            reference.AdvanceUntil(end);
            resumed.AdvanceUntil(source.WorldMinute.Add(60));
            await store.CheckpointAsync(resumed.CreatePersistenceSnapshot());
            resumed = new SimulationEngine(await store.LoadAsync());
            resumed.AdvanceUntil(end);
            Assert.Equal(WorldRulesUpgrades.Fingerprint(reference.CreatePersistenceSnapshot()), WorldRulesUpgrades.Fingerprint(resumed.CreatePersistenceSnapshot()));
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(SimulationEngine.MigrationSimulationRulesVersion)]
    [InlineData(SimulationEngine.RoadsSimulationRulesVersion)]
    [InlineData(SimulationEngine.PlannedSimulationRulesVersion)]
    [InlineData(SimulationEngine.FestivalsSimulationRulesVersion)]
    public async Task BackupCommitReceiptAndReopenPreserveAndContinue(string rules)
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-RulesUpgrade", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "world.db");
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: rules);
            engine.AdvanceUntil(new WorldMinute(10000));
            var source = engine.CreatePersistenceSnapshot();
            // Keep the writer open so the backup must include the checkpoint's WAL pages.
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(source);
            var backup = Assert.IsType<WorldRulesBackup>(await WorldRulesBackup.PrepareAsync(path));
            Assert.Null(await WorldRulesBackup.PrepareAsync(path, rulesUpgradeRequested: false));
            Assert.True(File.Exists(backup.Path));
            Assert.Contains(backup.Sha256, await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(backup.Path)!, "manifest.json")), StringComparison.Ordinal);
            var validationPath = Path.Combine(root, "backup-validation.db");
            File.Copy(backup.Path, validationPath);
            await using (var original = await WorldDatabase.OpenAsync(validationPath))
                Assert.Equal(WorldRulesUpgrades.Fingerprint(source), WorldRulesUpgrades.Fingerprint(await original.CreateCheckpointStore().LoadAsync()));
            var plan = WorldRulesUpgrades.Plan(source);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.CheckpointAsync(plan.Snapshot));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpgradeRulesAsync(plan, backup, CheckpointFailurePoint.AfterRowsWritten));
            Assert.Equal(plan.SourceFingerprint, WorldRulesUpgrades.Fingerprint(await store.LoadAsync()));
            Assert.Null(await store.ReadLatestRulesUpgradeAsync());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.UpgradeRulesAsync(plan, backup, CheckpointFailurePoint.CancellationAfterRowsWritten));
            Assert.Equal(plan.SourceFingerprint, WorldRulesUpgrades.Fingerprint(await store.LoadAsync()));
            Assert.Null(await store.ReadLatestRulesUpgradeAsync());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.UpgradeRulesAsync(plan, backup, new CancellationToken(true)));
            Assert.Equal(plan.SourceFingerprint, WorldRulesUpgrades.Fingerprint(await store.LoadAsync()));
            await store.UpgradeRulesAsync(plan, backup);

            var receipt = Assert.IsType<WorldRulesUpgradeReceipt>(await store.ReadLatestRulesUpgradeAsync());
            Assert.Equal(source.WorldMinute.Value, receipt.ActivationMinute);
            Assert.Equal(plan.SourceFingerprint, receipt.SourceFingerprint);
            Assert.Equal(plan.TargetFingerprint, receipt.TargetFingerprint);
            Assert.Equal(backup.Sha256, receipt.BackupSha256);
            var reopened = await store.LoadAsync();
            Assert.Equal(plan.TargetFingerprint, WorldRulesUpgrades.Fingerprint(reopened));
            var reference = new SimulationEngine(plan.Snapshot);
            var actual = new SimulationEngine(reopened);
            reference.AdvanceUntil(new WorldMinute(20000));
            actual.AdvanceUntil(new WorldMinute(15000));
            await store.CheckpointAsync(actual.CreatePersistenceSnapshot());
            actual = new SimulationEngine(await store.LoadAsync());
            actual.AdvanceUntil(new WorldMinute(20000));
            Assert.Equal(WorldRulesUpgrades.Fingerprint(reference.CreatePersistenceSnapshot()), WorldRulesUpgrades.Fingerprint(actual.CreatePersistenceSnapshot()));
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task StalePlanAndModifiedBackupCannotReplaceCheckpoint()
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-RulesUpgrade", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "world.db");
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.MigrationSimulationRulesVersion);
            var source = engine.CreatePersistenceSnapshot();
            await store.CheckpointAsync(source);
            var backup = (await WorldRulesBackup.PrepareAsync(path))!;
            var plan = WorldRulesUpgrades.Plan(source);
            await File.AppendAllTextAsync(backup.Path, "changed");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.UpgradeRulesAsync(plan, backup));
            engine.AdvanceUntil(new WorldMinute(100));
            await store.CheckpointAsync(engine.CreatePersistenceSnapshot());
            backup = (await WorldRulesBackup.PrepareAsync(path))!;
            await Assert.ThrowsAsync<InvalidDataException>(() => store.UpgradeRulesAsync(plan, backup));
            Assert.Equal(SimulationEngine.MigrationSimulationRulesVersion, (await store.LoadAsync()).SimulationRulesVersion);
            Assert.Null(await store.ReadLatestRulesUpgradeAsync());
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
