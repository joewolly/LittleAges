using System.Reflection;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M16StorehousePersistenceTests
{
    [Fact]
    public async Task PlannedStorehouseSurvivesSqliteReopenAndContinuesIdentically()
    {
        var seed = new WorldSeed(42);
        var uninterrupted = new SimulationEngine(seed, simulationRulesVersion: SimulationEngine.PlannedSimulationRulesVersion);
        // Start a storehouse in the planned storage yard before the first demand check.
        var select = typeof(SimulationEngine).GetMethod("SelectConstructionSite", BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, types: [typeof(long), typeof(StructureType)], modifiers: null)!;
        var create = typeof(SimulationEngine).GetMethod("CreateStructure", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var site = Assert.IsType<TileCoordinate>(select.Invoke(uninterrupted, [1L, StructureType.Storehouse]));
        create.Invoke(uninterrupted, [StructureType.Storehouse, site]);
        var checkpoint = uninterrupted.CreatePersistenceSnapshot();
        Assert.Contains(checkpoint.Structures, x => x.Type == StructureType.Storehouse && x.Location == site);

        var root = Path.Combine(Path.GetTempPath(), "littleages-m16-storehouse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "world.db");
        try
        {
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(checkpoint);

            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                Assert.Equal(SimulationEngine.PlannedSimulationRulesVersion, loaded.SimulationRulesVersion);
                var storehouse = Assert.Single(loaded.Structures, x => x.Type == StructureType.Storehouse);
                Assert.Equal(StructureDefinitions.StorehouseRequiredWood, storehouse.RequiredWood);
                Assert.Equal(StructureDefinitions.StorehouseRequiredStone, storehouse.RequiredStone);
                Assert.Equal(StructureDefinitions.StorehouseRequiredWork, storehouse.RequiredWork);

                var reopened = SimulationEngine.FromPersistenceSnapshot(loaded);
                var target = new WorldMinute(3L * WorldCalendar.MinutesPerDay);
                uninterrupted.AdvanceUntil(target);
                reopened.AdvanceUntil(target);
                Assert.Equal(uninterrupted.SettlementFingerprint, reopened.SettlementFingerprint);
                Assert.Equal(uninterrupted.HistoryFingerprint, reopened.HistoryFingerprint);
                await database.CreateCheckpointStore().CheckpointAsync(reopened.CreatePersistenceSnapshot());
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void OlderRulesRejectAStorehouse()
    {
        var planned = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.PlannedSimulationRulesVersion);
        var create = typeof(SimulationEngine).GetMethod("CreateStructure", BindingFlags.Instance | BindingFlags.NonPublic)!;
        create.Invoke(planned, [StructureType.Storehouse, new TileCoordinate(planned.World.StartingSite.X + 3, planned.World.StartingSite.Y + 3)]);
        var snapshot = planned.CreatePersistenceSnapshot();
        SimulationPersistenceSnapshot Relabel(string rules) => new(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, rules,
            snapshot.ApplicationVersion, snapshot.WorldConfiguration, snapshot.Counters, snapshot.ScheduledEvents, snapshot.World, snapshot.Citizens,
            snapshot.CitizenGenerationVersion, snapshot.ResourceStates, snapshot.Settlement, snapshot.SurvivalVersion, snapshot.SettlementVersion,
            snapshot.Structures, snapshot.StructureContributions, snapshot.SocialVersion, snapshot.Relationships, snapshot.Households,
            snapshot.HistoryVersion, snapshot.HistoryState, snapshot.HistoricalEvents, snapshot.HistoricalEventCitizens, snapshot.HistoricalEventStructures,
            snapshot.StatisticsSamples, snapshot.Memories, snapshot.Agriculture, snapshot.Economy, snapshot.LivingStateJson, snapshot.MigrationStateJson);

        Assert.Equal(SimulationEngine.PlannedSimulationRulesVersion, Relabel(SimulationEngine.PlannedSimulationRulesVersion).SimulationRulesVersion);
        var error = Assert.Throws<ArgumentException>(() => Relabel(SimulationEngine.RoadsSimulationRulesVersion));
        Assert.Contains("storehouses", error.Message, StringComparison.Ordinal);
    }
}
