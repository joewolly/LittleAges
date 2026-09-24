using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class UnifiedRulesPersistenceTests
{
    [Fact]
    public async Task M13ActionSchemaUpgradePreservesExistingM12CheckpointRows()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.SpacedSimulationRulesVersion);
        var before = engine.CreatePersistenceSnapshot();
        var root = Path.Combine(Path.GetTempPath(), "littleages-m13-schema-upgrade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "world.db");
        var options = new DbContextOptionsBuilder<LittleAgesDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False", sqlite => sqlite.MigrationsAssembly(typeof(WorldDatabase).Assembly.GetName().Name))
            .Options;
        try
        {
            // Install a populated M12 checkpoint under the previous M11 schema,
            // then exercise the new schema migration as an existing-world open.
            await using (var context = new LittleAgesDbContext(options))
            {
                await context.Database.GetService<IMigrator>().MigrateAsync("20260920010000_M11Economy");
                await new WorldCheckpointStore(context).CheckpointAsync(before);
            }

            await using var database = await WorldDatabase.OpenAsync(path);
            var loaded = await database.CreateCheckpointStore().LoadAsync();
            Assert.Equal(before.SimulationRulesVersion, loaded.SimulationRulesVersion);
            Assert.Equal(engine.SurvivalFingerprint, SimulationEngine.FromPersistenceSnapshot(loaded).SurvivalFingerprint);
            Assert.Equal(before.Citizens.Select(x => x.Id).ToArray(), loaded.Citizens.Select(x => x.Id).ToArray());
            Assert.Contains("20260923010000_M13UnifiedCitizenAction", await database.Context.Database.GetAppliedMigrationsAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MidHarvestCargoAndDeliveredGrainSurviveSqliteReloadAndContinueExactly()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(180L * WorldCalendar.MinutesPerDay));
        var deadline = new WorldMinute(190L * WorldCalendar.MinutesPerDay);
        LivingWorldState living;
        LivingWorkOrder[] inTransit;
        while (true)
        {
            living = LivingWorldCodec.Deserialize(engine.LivingStateJson!);
            inTransit = living.Orders.Where(x => x.Kind == LivingWorkKind.Harvest && x.CargoInTransit && x.CitizenId is not null).ToArray();
            if (inTransit.Length > 0) break;
            if (engine.CurrentMinute >= deadline) throw new InvalidOperationException("M13 did not produce a farm grain cargo before the test deadline.");
            var nextEvent = engine.CreatePersistenceSnapshot().ScheduledEvents.Min(x => x.Order.DueWorldMinute);
            engine.AdvanceUntil(nextEvent);
        }

        var trackedOrderIds = inTransit.Select(x => x.Id).ToHashSet();
        var before = engine.CreatePersistenceSnapshot();
        var root = Path.Combine(Path.GetTempPath(), "littleages-m13-harvest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "world.db");
        try
        {
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(before);

            SimulationEngine resumed;
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                Assert.Equal(SimulationEngine.UnifiedSimulationRulesVersion, loaded.SimulationRulesVersion);
                Assert.Equal(before.LivingStateJson, loaded.LivingStateJson);
                Assert.Equal(before.Agriculture!.ToCanonicalJson(), loaded.Agriculture!.ToCanonicalJson());
                resumed = SimulationEngine.FromPersistenceSnapshot(loaded);
                var reloadedState = LivingWorldCodec.Deserialize(resumed.LivingStateJson!);
                foreach (var orderId in trackedOrderIds)
                {
                    var order = reloadedState.Orders.Single(x => x.Id == orderId);
                    Assert.True(order.CargoInTransit);
                    Assert.True(order.Produced);
                    Assert.Single(order.Cargo);
                    Assert.Equal(LivingGood.Grain, order.Cargo[0].Good);
                }
            }

            var uninterrupted = SimulationEngine.FromPersistenceSnapshot(before);
            while (trackedOrderIds.Any(id => LivingWorldCodec.Deserialize(uninterrupted.LivingStateJson!).Orders.Any(x => x.Id == id)))
            {
                var next = uninterrupted.CreatePersistenceSnapshot().ScheduledEvents.Min(x => x.Order.DueWorldMinute);
                Assert.Equal(next, resumed.CreatePersistenceSnapshot().ScheduledEvents.Min(x => x.Order.DueWorldMinute));
                uninterrupted.AdvanceUntil(next);
                resumed.AdvanceUntil(next);
            }

            var produced = uninterrupted.CreatePersistenceSnapshot();
            var producedLiving = LivingWorldCodec.Deserialize(produced.LivingStateJson!);
            Assert.True(producedLiving.CommunalGrainHarvested > 0);
            Assert.Equal(producedLiving.CommunalGrainHarvested,
                producedLiving.Stock.Single(x => x.Good == LivingGood.Grain).Quantity +
                producedLiving.Orders.Sum(x => x.Cargo.Where(y => y.Good == LivingGood.Grain).Sum(y => y.Quantity)) +
                producedLiving.CommunalGrainConsumed + producedLiving.CommunalGrainSpoiled);
            Assert.Equal(produced.LivingStateJson, resumed.LivingStateJson);
            Assert.Equal(produced.Agriculture!.ToCanonicalJson(), resumed.CaptureAgriculture()!.ToCanonicalJson());
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(produced);

            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var afterProduction = await database.CreateCheckpointStore().LoadAsync();
                Assert.Equal(produced.LivingStateJson, afterProduction.LivingStateJson);
                var reloadedAfterProduction = SimulationEngine.FromPersistenceSnapshot(afterProduction);
                var target = uninterrupted.CurrentMinute.Add(WorldCalendar.MinutesPerDay);
                uninterrupted.AdvanceUntil(target);
                resumed = reloadedAfterProduction;
                resumed.AdvanceUntil(target);
                Assert.Equal(uninterrupted.LivingStateJson, resumed.LivingStateJson);
                Assert.Equal(uninterrupted.HistoryFingerprint, resumed.HistoryFingerprint);
                Assert.Equal(uninterrupted.ComputeAgricultureFingerprint(), resumed.ComputeAgricultureFingerprint());
                Assert.Equal(uninterrupted.ComputeEconomyFingerprint(), resumed.ComputeEconomyFingerprint());
                _ = resumed.CreatePersistenceSnapshot();
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task M13RecoverableEconomyCoordinatesSurviveSqliteReopen()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.UnifiedSimulationRulesVersion);
        var source = engine.CreatePersistenceSnapshot();
        var sourceEconomy = source.Economy!;
        var fixtureRecoverable = new RecoverableGoods(sourceEconomy.NextCacheId, sourceEconomy.Households[0].HouseholdId,
            source.World!.StartingSite, ResourceType.Wood, 3);
        var expectedRecoverable = sourceEconomy.Recoverable.Append(fixtureRecoverable).OrderBy(x => x.Id).ToArray();
        var economy = sourceEconomy with
        {
            NextCacheId = checked(fixtureRecoverable.Id + 1),
            Produced = sourceEconomy.Produced.Add(fixtureRecoverable.Resource, fixtureRecoverable.Quantity),
            Recoverable = expectedRecoverable
        };
        var before = WithEconomy(source, economy);
        var fixtureEngine = SimulationEngine.FromPersistenceSnapshot(before);

        var root = Path.Combine(Path.GetTempPath(), "littleages-m13-recoverable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "world.db");
        try
        {
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                await database.CreateCheckpointStore().CheckpointAsync(before);
                var canonicalEconomyJson = await database.Context.EconomyStates.AsNoTracking().Select(x => x.CanonicalJson).SingleAsync();
                foreach (var recoverable in expectedRecoverable)
                    Assert.Contains($"\"Location\":{{\"X\":{recoverable.Location.X},\"Y\":{recoverable.Location.Y}}}", canonicalEconomyJson, StringComparison.Ordinal);
            }

            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                Assert.Equal(expectedRecoverable, loaded.Economy!.Recoverable);
                Assert.Equal(before.Economy!.ToCanonicalJson(), loaded.Economy.ToCanonicalJson());
                var resumed = SimulationEngine.FromPersistenceSnapshot(loaded);
                Assert.Equal(fixtureEngine.ComputeEconomyFingerprint(), resumed.ComputeEconomyFingerprint());
                Assert.Equal(fixtureEngine.SurvivalFingerprint, resumed.SurvivalFingerprint);
                _ = resumed.CreatePersistenceSnapshot();
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static SimulationPersistenceSnapshot WithEconomy(SimulationPersistenceSnapshot source, EconomyState economy) => new(
        source.Seed, source.WorldMinute, source.WorldSchemaVersion, source.SimulationRulesVersion, source.ApplicationVersion,
        source.WorldConfiguration, source.Counters, source.ScheduledEvents, source.World, source.Citizens,
        source.CitizenGenerationVersion, source.ResourceStates, source.Settlement, source.SurvivalVersion,
        source.SettlementVersion, source.Structures, source.StructureContributions, source.SocialVersion,
        source.Relationships, source.Households, source.HistoryVersion, source.HistoryState, source.HistoricalEvents,
        source.HistoricalEventCitizens, source.HistoricalEventStructures, source.StatisticsSamples, source.Memories,
        source.Agriculture, economy, source.LivingStateJson);
}
