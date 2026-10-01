using System.Reflection;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M16FestivalPersistenceTests
{
    [Fact]
    public async Task FestivalVisitorReopensDuringOutboundDwellMealAttendanceAndReturn()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.FestivalsSimulationRulesVersion);
        var start = FestivalRules.Start(2, 6);
        engine.AdvanceUntil(new WorldMinute(FestivalRules.Start(1, 6) - 14L * WorldCalendar.MinutesPerDay));
        var citizens = Field<Dictionary<long, Citizen>>(engine, "_citizens");
        var residences = engine.CreatePersistenceSnapshot().MigrationState!.CitizenResidences.ToDictionary(x => x.EntityId, x => x.SettlementId);
        var visitor = citizens.Values.Where(x => x.IsAlive && x.AgeYears(engine.CurrentMinute) >= 18 && residences[x.Id.Value] == 1).OrderBy(x => x.Id.Value).First();
        var relative = citizens.Values.Where(x => x.IsAlive && x.AgeYears(engine.CurrentMinute) >= 18 && residences[x.Id.Value] == 2).OrderBy(x => x.Id.Value).First();
        var parent = citizens.Keys.Order().First(x => x != visitor.Id.Value && x != relative.Id.Value);
        var originalVisitorParent = visitor.ParentAId;
        var originalRelativeParent = relative.ParentAId;
        // The natural six-year world supplies coherent roads, homes, food and work.
        // Inject kin only for deterministic selection, then restore biography before
        // checkpointing; every journey thereafter runs through the normal engine.
        visitor.ParentAId = new CitizenId(parent);
        relative.ParentAId = new CitizenId(parent);
        FestivalVisitPlan? plan = null;
        while (engine.CurrentMinute.Value < start)
        {
            plan = Field<LivingWorldState>(engine, "_living").FestivalVisit;
            if (plan?.PartyId is not null) break;
            engine.AdvanceUntil(engine.CurrentMinute.Add(30));
        }
        Assert.NotNull(plan);
        Assert.NotNull(plan.PartyId);
        Assert.Equal(2, plan.SettlementId);
        visitor.ParentAId = originalVisitorParent;
        relative.ParentAId = originalRelativeParent;
        var checkpoints = new List<SimulationPersistenceSnapshot> { engine.CreatePersistenceSnapshot() };
        engine.AdvanceUntil(new WorldMinute(start - 120));
        var party = engine.CreatePersistenceSnapshot().MigrationState!.InTransitParties.Single(x => x.Id == plan.PartyId);
        Assert.Equal(MigrationVisitPhase.Dwell, party.VisitPhase);
        Assert.Equal(start + FestivalRules.DurationMinutes, party.VisitDwellEndsMinute);
        checkpoints.Add(engine.CreatePersistenceSnapshot());
        var traveler = citizens[plan.CitizenId];
        traveler.Needs = traveler.GetProjectedNeeds(engine.CurrentMinute) with { Hunger = 3500 };
        traveler.NeedsUpdatedMinute = engine.CurrentMinute.Value;
        Assert.True((bool)Invoke(engine, "TryPauseMigrationVisitForMeal", traveler)!);
        checkpoints.Add(engine.CreatePersistenceSnapshot());
        engine.AdvanceUntil(new WorldMinute(start));
        // Begin an uninterrupted eligible interval after the meal/rest phases.
        // Each checkpoint below has its own uninterrupted comparison timeline.
        traveler.Needs = new CitizenNeeds();
        traveler.NeedsUpdatedMinute = engine.CurrentMinute.Value;
        var person = Field<LivingWorldState>(engine, "_living").People.Single(x => x.CitizenId == traveler.Id.Value);
        person.Injury = person.Illness = 0;
        party = engine.CreatePersistenceSnapshot().MigrationState!.InTransitParties.Single(x => x.Id == plan.PartyId);
        Assert.Equal(MigrationVisitPhase.Dwell, party.VisitPhase);
        Invoke(engine, "ResumeMigrationVisit", traveler, party);
        engine.AdvanceUntil(new WorldMinute(start + FestivalRules.AttendanceMinutes));
        var festival = LivingWorldCodec.Deserialize(engine.LivingStateJson!).Festivals!.Single(x => x.SettlementId == 2);
        Assert.True(festival.Attendance.Any(x => x.CitizenId == traveler.Id.Value && x.BenefitsGranted),
            $"Visitor {traveler.Id.Value}: {traveler.CurrentAction}/{traveler.ActionPhase} at {traveler.Location}; festival {festival.Location}/{festival.StartMinute}/{festival.Started}/{festival.Finished}; needs {traveler.GetProjectedNeeds(engine.CurrentMinute)}; injury {person.Injury}, illness {person.Illness}; marker {plan.LastAttendanceMinute}; party {engine.CreatePersistenceSnapshot().MigrationState!.InTransitParties.Single(x => x.Id == plan.PartyId).VisitPhase}.");
        checkpoints.Add(engine.CreatePersistenceSnapshot());
        engine.AdvanceUntil(new WorldMinute(start + FestivalRules.DurationMinutes));
        checkpoints.Add(engine.CreatePersistenceSnapshot());
        var end = new WorldMinute(start + FestivalRules.DurationMinutes + 2L * WorldCalendar.MinutesPerDay);
        engine.AdvanceUntil(end);
        Assert.Contains(engine.CreatePersistenceSnapshot().HistoricalEvents, x => x.EventType == HistoricalEventType.FamilyVisitReturned);
        var root = Path.Combine(Path.GetTempPath(), "littleages-m16-visit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (var i = 0; i < checkpoints.Count; i++)
            {
                var expected = new SimulationEngine(checkpoints[i]);
                expected.AdvanceUntil(end);
                var path = Path.Combine(root, i + ".db");
                await using (var database = await WorldDatabase.OpenAsync(path))
                    await database.CreateCheckpointStore().CheckpointAsync(checkpoints[i]);
                await using (var database = await WorldDatabase.OpenAsync(path))
                {
                    var reopened = new SimulationEngine(await database.CreateCheckpointStore().LoadAsync());
                    reopened.AdvanceUntil(end);
                    Assert.Equal(expected.LivingStateJson, reopened.LivingStateJson);
                    Assert.Equal(expected.CreatePersistenceSnapshot().MigrationStateJson, reopened.CreatePersistenceSnapshot().MigrationStateJson);
                    Assert.Equal(expected.HistoryFingerprint, reopened.HistoryFingerprint);
                    Assert.Equal(expected.SettlementFingerprint, reopened.SettlementFingerprint);
                }
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FestivalReplaysAcrossSqliteReopenBeforeOpeningDuringAttendanceAndAfterClosing()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.FestivalsSimulationRulesVersion);
        var start = FestivalRules.Start(1, 0);
        var root = Path.Combine(Path.GetTempPath(), "littleages-m16-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var minute in new[] { start - 30, start, start + 90, start + 360 })
            {
                engine.AdvanceUntil(new WorldMinute(minute));
                var checkpoint = engine.CreatePersistenceSnapshot();
                var path = Path.Combine(root, minute + ".db");
                await using (var database = await WorldDatabase.OpenAsync(path))
                    await database.CreateCheckpointStore().CheckpointAsync(checkpoint);
                await using (var database = await WorldDatabase.OpenAsync(path))
                {
                    var loaded = await database.CreateCheckpointStore().LoadAsync();
                    Assert.Equal(checkpoint.LivingStateJson, loaded.LivingStateJson);
                    Assert.Equal(checkpoint.Memories, loaded.Memories);
                    var expected = new SimulationEngine(checkpoint);
                    var reopened = new SimulationEngine(loaded);
                    expected.AdvanceUntil(new WorldMinute(start + 480));
                    reopened.AdvanceUntil(new WorldMinute(start + 480));
                    Assert.Equal(expected.LivingStateJson, reopened.LivingStateJson);
                    Assert.Equal(expected.HistoryFingerprint, reopened.HistoryFingerprint);
                    Assert.Equal(expected.SettlementFingerprint, reopened.SettlementFingerprint);
                    Assert.Equal(expected.CreatePersistenceSnapshot().MigrationStateJson, reopened.CreatePersistenceSnapshot().MigrationStateJson);
                    await database.CreateCheckpointStore().CheckpointAsync(reopened.CreatePersistenceSnapshot());
                    await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => database.Context.GetService<IMigrator>()
                        .MigrateAsync("20260926000000_M15HistoricalEventTypes"));
                }
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AdditiveMigrationPreservesM15RulesHistoryAndFingerprints()
    {
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.RoadsSimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(WorldCalendar.MinutesPerDay));
        var checkpoint = engine.CreatePersistenceSnapshot();
        var root = Path.Combine(Path.GetTempPath(), "littleages-m16-upgrade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "world.db");
        try
        {
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                await database.Context.GetService<IMigrator>().MigrateAsync("20260926000000_M15HistoricalEventTypes");
                await database.CreateCheckpointStore().CheckpointAsync(checkpoint);
            }
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var loaded = await database.CreateCheckpointStore().LoadAsync();
                Assert.Equal(checkpoint.SimulationRulesVersion, loaded.SimulationRulesVersion);
                Assert.Equal(checkpoint.LivingStateJson, loaded.LivingStateJson);
                Assert.Equal(checkpoint.HistoricalEvents, loaded.HistoricalEvents);
                Assert.Equal(checkpoint.Memories, loaded.Memories);
                var reopened = new SimulationEngine(loaded);
                Assert.Equal(engine.HistoryFingerprint, reopened.HistoryFingerprint);
                Assert.Equal(engine.SettlementFingerprint, reopened.SettlementFingerprint);
                await Assert.ThrowsAsync<InvalidDataException>(() => database.CreateCheckpointStore().CheckpointAsync(
                    new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.FestivalsSimulationRulesVersion).CreatePersistenceSnapshot()));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static object? Invoke(object instance, string name, params object?[] args) => instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Single(x => x.Name == name && x.GetParameters().Length == args.Length).Invoke(instance, args);
}
