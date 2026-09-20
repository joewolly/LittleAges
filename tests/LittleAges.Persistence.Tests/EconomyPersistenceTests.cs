using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class EconomyPersistenceTests
{
    [Fact]
    public async Task EconomicCheckpointRollsBackAtomicallyAndRejectsCorruptOwnership()
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-EconomyAtomic", Guid.NewGuid().ToString("N"));
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.BarterSimulationRulesVersion);
            var initial = engine.CreatePersistenceSnapshot();
            await using var database = await WorldDatabase.OpenAsync(Path.Combine(root, "world.db"));
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(initial);
            engine.AdvanceUntil(new WorldMinute(WorldCalendar.MinutesPerDay));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.CheckpointAsync(engine.CreatePersistenceSnapshot(), DateTime.UtcNow, CheckpointFailurePoint.AfterRowsWritten));
            var rolledBack = await store.LoadAsync();
            Assert.Equal(initial.Economy!.ToCanonicalJson(), rolledBack.Economy!.ToCanonicalJson());
            Assert.Equal(initial.WorldMinute, rolledBack.WorldMinute);
            var broken = initial.Economy with { Households = initial.Economy.Households.Concat([initial.Economy.Households[0]]).ToArray() };
            var json = broken.ToCanonicalJson();
            await database.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE economy_state SET canonical_json = {json} WHERE id = 1");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DescendantInheritanceIsOrderedAndEquivalentAcrossRealReopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-Inheritance", Guid.NewGuid().ToString("N"));
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.BarterSimulationRulesVersion);
            engine.AdvanceUntil(new WorldMinute(30L * WorldCalendar.MinutesPerYear));
            var people = engine.Citizens;
            var state = engine.CaptureEconomy()!;
            var stock = state.Households.First(h => h.Holdings.Total > 0 &&
                people.Where(c => c.IsAlive && c.HouseholdId?.Value == h.HouseholdId).All(c => c.AgeYears(engine.CurrentMinute) >= 18 && c.CarriedResourceQuantity == 0) &&
                !state.Trades.Any(t => t.Status == BarterStatus.Reserved && (t.HouseholdA == h.HouseholdId || t.HouseholdB == h.HouseholdId)) &&
                people.Any(c => c.IsAlive && c.HouseholdId?.Value != h.HouseholdId && people.Any(p => p.HouseholdId?.Value == h.HouseholdId && (c.ParentAId == p.Id || c.ParentBId == p.Id))));
            var members = people.Where(c => c.IsAlive && c.HouseholdId?.Value == stock.HouseholdId).Select(c => c.Id.Value).ToArray();
            var descendants = people.Where(c => c.HouseholdId?.Value == stock.HouseholdId).Select(c => c.Id.Value).ToHashSet();
            bool changed;
            do { changed = false; foreach (var c in people) if (!descendants.Contains(c.Id.Value) && (c.ParentAId is { } a && descendants.Contains(a.Value) || c.ParentBId is { } b && descendants.Contains(b.Value))) changed |= descendants.Add(c.Id.Value); } while (changed);
            var recipients = people.Where(c => c.IsAlive && descendants.Contains(c.Id.Value) && c.HouseholdId?.Value != stock.HouseholdId).Select(c => c.HouseholdId!.Value.Value).Distinct().Order().ToArray();
            Assert.NotEmpty(recipients);
            var path = Path.Combine(root, "before.db");
            await using (var db = await WorldDatabase.OpenAsync(path)) await db.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot());
            SimulationEngine reopened;
            await using (var db = await WorldDatabase.OpenAsync(path)) reopened = SimulationEngine.FromPersistenceSnapshot(await db.CreateCheckpointStore().LoadAsync());
            // Explicit mortality fixture applied to the same canonical people on both
            // sides. Descendants, supplies, partnerships and houses developed naturally.
            foreach (var target in new[] { engine, reopened })
            {
                var canonical = (IDictionary<long, Citizen>)typeof(SimulationEngine).GetField("_citizens", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(target)!;
                foreach (var id in members) typeof(SimulationEngine).GetMethod("KillNatural", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(target, [canonical[id]]);
            }
            var after = engine.CaptureEconomy()!;
            Assert.DoesNotContain(after.Households, h => h.HouseholdId == stock.HouseholdId);
            Assert.Contains(after.Events, e => e.Kind == "Inheritance" && e.HouseholdId == stock.HouseholdId);
            for (var i = 0; i < recipients.Length; i++) foreach (var resource in EconomyRules.Resources)
                Assert.Equal(state.Households.Single(h => h.HouseholdId == recipients[i]).Holdings.Get(resource) + stock.Holdings.Get(resource) / recipients.Length + (i < stock.Holdings.Get(resource) % recipients.Length ? 1 : 0), after.Households.Single(h => h.HouseholdId == recipients[i]).Holdings.Get(resource));
            Assert.Equal(engine.ComputeEconomyFingerprint(), reopened.ComputeEconomyFingerprint());
            await using (var db = await WorldDatabase.OpenAsync(Path.Combine(root, "after.db"))) await db.CreateCheckpointStore().CheckpointAsync(reopened.CreatePersistenceSnapshot());
            await using (var db = await WorldDatabase.OpenAsync(Path.Combine(root, "after.db"))) reopened = SimulationEngine.FromPersistenceSnapshot(await db.CreateCheckpointStore().LoadAsync());
            var end = engine.CurrentMinute.Add(WorldCalendar.MinutesPerDay);
            engine.AdvanceUntil(end); reopened.AdvanceUntil(end);
            Assert.Equal(engine.ComputeEconomyFingerprint(), reopened.ComputeEconomyFingerprint());
            Assert.Equal(engine.ComputeHistoryFingerprint(), reopened.ComputeHistoryFingerprint());
            Assert.Equal(engine.ComputeSocialFingerprint(), reopened.ComputeSocialFingerprint());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MarketplaceEscrowAndSettlementSurviveRealSqliteReopen(bool settled)
    {
        var root = Path.Combine(Path.GetTempPath(), "LittleAges-Economy", Guid.NewGuid().ToString("N"));
        try
        {
            var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.BarterSimulationRulesVersion);
            bool Reached() => engine.CaptureEconomy()!.Trades.Any(t => settled ? t.Status == BarterStatus.Completed : t.Status == BarterStatus.Reserved && t.DeliveredA != t.DeliveredB);
            while (engine.CurrentMinute.Value < WorldCalendar.MinutesPerYear && !Reached()) engine.ProcessNextEvent();
            Assert.True(Reached(), "Autonomous carriers must reach actual escrow/settlement.");
            var path = Path.Combine(root, "world.db");
            await using (var db = await WorldDatabase.OpenAsync(path)) await db.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot());
            SimulationEngine reopened;
            await using (var db = await WorldDatabase.OpenAsync(path)) reopened = SimulationEngine.FromPersistenceSnapshot(await db.CreateCheckpointStore().LoadAsync());
            Assert.Equal(engine.CaptureEconomy()!.ToCanonicalJson(), reopened.CaptureEconomy()!.ToCanonicalJson());
            var end = engine.CurrentMinute.Add(5 * WorldCalendar.MinutesPerDay);
            engine.AdvanceUntil(end); reopened.AdvanceUntil(end);
            Assert.Equal(engine.ComputeSocialFingerprint(), reopened.ComputeSocialFingerprint());
            Assert.Equal(engine.ComputeHistoryFingerprint(), reopened.ComputeHistoryFingerprint());
            Assert.Equal(engine.ComputeAgricultureFingerprint(), reopened.ComputeAgricultureFingerprint());
            Assert.Equal(engine.ComputeEconomyFingerprint(), reopened.ComputeEconomyFingerprint());
            _ = reopened.CreatePersistenceSnapshot();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
