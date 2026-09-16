using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M6HistoryPersistenceTests
{
    private static readonly HistoricalEventType[] InitializationTypes = [HistoricalEventType.WorldCreated, HistoricalEventType.SettlementFounded, HistoricalEventType.SeasonStarted];

    [Fact]
    public async Task M6CheckpointAppendsOnlyNewHistoryAndRepeatedCheckpointIsIdempotent()
    {
        await WithDatabaseAsync(async path =>
        {
            var engine = Advance(new WorldSeed(0), 7);
            var snapshotA = engine.CreatePersistenceSnapshot();
            var checkpointUtc = new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc);
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(snapshotA, checkpointUtc);

            var firstEvents = await database.Context.HistoricalEvents.AsNoTracking().OrderBy(row => row.Id).ToListAsync();
            var firstCitizenLinks = await database.Context.HistoricalEventCitizens.AsNoTracking().OrderBy(row => row.EventId).ThenBy(row => row.CitizenId).ThenBy(row => row.Role).ToListAsync();
            var firstStats = await database.Context.StatisticsSamples.AsNoTracking().OrderBy(row => row.WorldMinute).ToListAsync();
            Assert.Equal(snapshotA.HistoricalEvents.Count, firstEvents.Count);
            Assert.Equal(snapshotA.HistoricalEventCitizens.Count, firstCitizenLinks.Count);
            Assert.Equal(snapshotA.StatisticsSamples.Count, firstStats.Count);

            engine.AdvanceUntil(new WorldMinute(30L * WorldCalendar.MinutesPerDay));
            var snapshotB = engine.CreatePersistenceSnapshot();
            Assert.True(snapshotB.HistoricalEvents.Count >= snapshotA.HistoricalEvents.Count);
            Assert.True(snapshotB.StatisticsSamples.Count > snapshotA.StatisticsSamples.Count);
            await store.CheckpointAsync(snapshotB, checkpointUtc.AddMinutes(1));

            var secondEvents = await database.Context.HistoricalEvents.AsNoTracking().OrderBy(row => row.Id).ToListAsync();
            var secondCitizenLinks = await database.Context.HistoricalEventCitizens.AsNoTracking().OrderBy(row => row.EventId).ThenBy(row => row.CitizenId).ThenBy(row => row.Role).ToListAsync();
            var secondStats = await database.Context.StatisticsSamples.AsNoTracking().OrderBy(row => row.WorldMinute).ToListAsync();
            Assert.Equal(snapshotB.HistoricalEvents.Count, secondEvents.Count);
            Assert.Equal(snapshotB.HistoricalEventCitizens.Count, secondCitizenLinks.Count);
            Assert.Equal(snapshotB.StatisticsSamples.Count, secondStats.Count);
            Assert.Equal(firstEvents.Select(EventRowKey), secondEvents.Take(firstEvents.Count).Select(EventRowKey));
            Assert.Equal(firstCitizenLinks.Select(LinkRowKey), secondCitizenLinks.Take(firstCitizenLinks.Count).Select(LinkRowKey));
            Assert.Equal(firstStats.Select(StatisticsRowKey), secondStats.Take(firstStats.Count).Select(StatisticsRowKey));

            var eventsBeforeRepeat = secondEvents.Count;
            var linksBeforeRepeat = secondCitizenLinks.Count;
            var statsBeforeRepeat = secondStats.Count;
            await store.CheckpointAsync(snapshotB, checkpointUtc.AddMinutes(2));
            Assert.Equal(eventsBeforeRepeat, await database.Context.HistoricalEvents.CountAsync());
            Assert.Equal(linksBeforeRepeat, await database.Context.HistoricalEventCitizens.CountAsync());
            Assert.Equal(statsBeforeRepeat, await database.Context.StatisticsSamples.CountAsync());
            var loaded = await store.LoadAsync();
            Assert.Equal(SimulationEngine.FromPersistenceSnapshot(snapshotB).HistoryFingerprint, SimulationEngine.FromPersistenceSnapshot(loaded).HistoryFingerprint);
            Assert.Equal(snapshotB.Counters.NextHistoricalEventId, loaded.Counters.NextHistoricalEventId);
        });
    }

    [Fact]
    public async Task M6CheckpointRollbackRestoresHistoryPrefixAndRetryPersistsTail()
    {
        await WithDatabaseAsync(async path =>
        {
            var engine = Advance(new WorldSeed(0), 7);
            var snapshotA = engine.CreatePersistenceSnapshot();
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            var checkpointUtc = new DateTime(2026, 9, 16, 1, 0, 0, DateTimeKind.Utc);
            await store.CheckpointAsync(snapshotA, checkpointUtc);

            engine.AdvanceUntil(new WorldMinute(30L * WorldCalendar.MinutesPerDay));
            var snapshotB = engine.CreatePersistenceSnapshot();
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.CheckpointAsync(snapshotB, checkpointUtc.AddMinutes(1), CheckpointFailurePoint.AfterRowsWritten));

            var restoredA = await store.LoadAsync();
            Assert.Equal(SimulationEngine.FromPersistenceSnapshot(snapshotA).HistoryFingerprint, SimulationEngine.FromPersistenceSnapshot(restoredA).HistoryFingerprint);
            Assert.Equal(snapshotA.HistoricalEvents, restoredA.HistoricalEvents);
            Assert.Equal(snapshotA.StatisticsSamples, restoredA.StatisticsSamples);
            Assert.Equal(snapshotA.Counters, restoredA.Counters);

            await store.CheckpointAsync(snapshotB, checkpointUtc.AddMinutes(2));
            var restoredB = await store.LoadAsync();
            Assert.Equal(SimulationEngine.FromPersistenceSnapshot(snapshotB).HistoryFingerprint, SimulationEngine.FromPersistenceSnapshot(restoredB).HistoryFingerprint);
            Assert.Equal(snapshotB.HistoricalEvents, restoredB.HistoricalEvents);
            Assert.Equal(snapshotB.StatisticsSamples, restoredB.StatisticsSamples);
        });
    }

    [Fact]
    public async Task M5ToM6BackfillUsesNonOneHistoricalStartAndIsIdempotentWithoutInventingTransitions()
    {
        await WithDatabaseAsync(async path =>
        {
            var m5 = AdvanceM5(new WorldSeed(42), 7).CreatePersistenceSnapshot();
            var nonOneCounter = WithHistoricalCounter(m5, 987_654);
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                await database.CreateCheckpointStore().CheckpointAsync(nonOneCounter, new DateTime(2026, 9, 16, 2, 0, 0, DateTimeKind.Utc));
            }

            SimulationPersistenceSnapshot upgraded;
            await using (var database = await WorldDatabase.OpenAsync(path)) upgraded = await database.CreateCheckpointStore().LoadAsync();
            Assert.Equal(SimulationEngine.CurrentSimulationRulesVersion, upgraded.SimulationRulesVersion);
            Assert.Equal(SimulationEngine.HistoryVersion, upgraded.HistoryVersion);
            Assert.NotNull(upgraded.HistoryState);
            Assert.Equal(987_654, upgraded.HistoryState!.HistoryStartEventId);
            Assert.Equal(987_654, upgraded.HistoricalEvents[0].Id.Value);
            Assert.All(upgraded.HistoricalEvents, item => Assert.Equal(HistoricalEventOrigin.MigrationBackfill, item.Origin));
            Assert.Equal(InitializationTypes, upgraded.HistoricalEvents.Take(3).Select(item => item.EventType));
            Assert.DoesNotContain(upgraded.HistoricalEvents, item => item.EventType is HistoricalEventType.FriendshipFormed or HistoricalEventType.RivalryFormed or HistoricalEventType.CitizenSpecializationChanged);
            Assert.Empty(upgraded.StatisticsSamples);

            var fingerprint = SimulationEngine.FromPersistenceSnapshot(upgraded).HistoryFingerprint;
            var eventCount = upgraded.HistoricalEvents.Count;
            var linkCount = upgraded.HistoricalEventCitizens.Count;
            var memoryCount = upgraded.Memories.Count;
            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var reopened = await database.CreateCheckpointStore().LoadAsync();
                Assert.Equal(fingerprint, SimulationEngine.FromPersistenceSnapshot(reopened).HistoryFingerprint);
                Assert.Equal(eventCount, reopened.HistoricalEvents.Count);
                Assert.Equal(linkCount, reopened.HistoricalEventCitizens.Count);
                Assert.Equal(memoryCount, reopened.Memories.Count);
                Assert.Equal(987_654, reopened.HistoryState!.HistoryStartEventId);
            }
        });
    }

    [Fact]
    public async Task M6LoadRejectsFutureHistoricalEventRowAndMalformedPayload()
    {
        await WithDatabaseAsync(async path =>
        {
            var snapshot = Advance(new WorldSeed(0), 1).CreatePersistenceSnapshot();
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(snapshot);
            await database.Context.Database.ExecuteSqlRawAsync("UPDATE historical_events SET world_minute = 999999 WHERE id = 1");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
            var malformedPayload = "{\"seed\":\"042\"}";
            await database.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE historical_events SET world_minute = 0, payload_json = {malformedPayload} WHERE id = 1");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        });
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public async Task M6LoadRejectsRelationshipRowWhoseMetricsDoNotReachItsFormationLabel(int eventType)
    {
        await WithDatabaseAsync(async path =>
        {
            var snapshot = Advance(new WorldSeed(0), 1).CreatePersistenceSnapshot();
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(snapshot);
            const string relationshipPayload = "{\"familiarity\":500,\"affinity\":0,\"trust\":500,\"conflict\":0}";
            await database.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE historical_events SET event_type = {eventType}, importance = 2, payload_json = {relationshipPayload} WHERE id = 3");
            await database.Context.Database.ExecuteSqlRawAsync("INSERT INTO historical_event_citizens (event_id, citizen_id, role) VALUES (3, 1, 'participant'), (3, 2, 'participant')");

            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        });
    }

    [Theory]
    [InlineData(12, "{\"resourceType\":\"Food\",\"quantity\":200,\"livingPopulation\":20,\"preexistingAtHistoryStart\":false}")]
    [InlineData(13, "{\"resourceType\":\"Food\",\"quantity\":399,\"livingPopulation\":20,\"preexistingAtHistoryStart\":false}")]
    public async Task M6LoadRejectsFoodShortageRowsOutsideTheirHysteresisThreshold(int eventType, string payload)
    {
        await WithDatabaseAsync(async path =>
        {
            var snapshot = Advance(new WorldSeed(0), 1).CreatePersistenceSnapshot();
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(snapshot);
            await database.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE historical_events SET event_type = {eventType}, importance = 3, payload_json = {payload} WHERE id = 3");

            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        });
    }

    [Fact]
    public async Task M6LoadRejectsPopulationMilestoneHistoryThatCannotBeObservedOrWatermarked()
    {
        await WithDatabaseAsync(async path =>
        {
            var snapshot = Advance(new WorldSeed(0), 1).CreatePersistenceSnapshot();
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(snapshot);
            const string payload = "{\"population\":25}";
            await database.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE historical_events SET event_type = 11, importance = 4, payload_json = {payload} WHERE id = 3");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        });

        await WithDatabaseAsync(async path =>
        {
            var snapshot = Advance(new WorldSeed(0), 1).CreatePersistenceSnapshot();
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(snapshot);
            await database.Context.Database.ExecuteSqlRawAsync("UPDATE history_state SET population_milestone_watermark = 25 WHERE id = 1");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        });
    }

    [Fact]
    public async Task M6LoadRejectsNonCanonicalEventImportance()
    {
        await WithDatabaseAsync(async path =>
        {
            var snapshot = Advance(new WorldSeed(0), 1).CreatePersistenceSnapshot();
            await using var database = await WorldDatabase.OpenAsync(path);
            var store = database.CreateCheckpointStore();
            await store.CheckpointAsync(snapshot);
            await database.Context.Database.ExecuteSqlRawAsync("UPDATE historical_events SET importance = 2 WHERE id = 3");

            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        });
    }

    private static SimulationEngine Advance(WorldSeed seed, long days)
    {
        var engine = new SimulationEngine(seed, simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(checked(days * WorldCalendar.MinutesPerDay)));
        return engine;
    }

    private static SimulationEngine AdvanceM5(WorldSeed seed, long days)
    {
        var engine = new SimulationEngine(seed, simulationRulesVersion: SimulationEngine.M5SimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(checked(days * WorldCalendar.MinutesPerDay)));
        return engine;
    }

    private static SimulationPersistenceSnapshot WithHistoricalCounter(SimulationPersistenceSnapshot snapshot, long nextHistoricalEventId) =>
        new(snapshot.Seed, snapshot.WorldMinute, snapshot.WorldSchemaVersion, snapshot.SimulationRulesVersion, snapshot.ApplicationVersion, snapshot.WorldConfiguration,
            new DeterministicCountersSnapshot(snapshot.Counters.NextEntityId, nextHistoricalEventId, snapshot.Counters.NextScheduledEventSequence), snapshot.ScheduledEvents,
            snapshot.World, snapshot.Citizens, snapshot.CitizenGenerationVersion, snapshot.ResourceStates, snapshot.Settlement, snapshot.SurvivalVersion, snapshot.SettlementVersion,
            snapshot.Structures, snapshot.StructureContributions, snapshot.SocialVersion, snapshot.Relationships, snapshot.Households);

    private static async Task WithDatabaseAsync(Func<string, Task> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "LittleAges-M6-Persistence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "world.db");
        try { await action(path); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static string EventRowKey(HistoricalEventRow row) => $"{row.Id}:{row.WorldMinute}:{row.EventType}:{row.Importance}:{row.Origin}:{row.LocationX}:{row.LocationY}:{row.PayloadJson}:{row.SchemaVersion}";
    private static string LinkRowKey(HistoricalEventCitizenLinkRow row) => $"{row.EventId}:{row.CitizenId}:{row.Role}";
    private static string StatisticsRowKey(StatisticsSampleRow row) => $"{row.WorldMinute}:{row.PeriodStartMinute}:{row.Population}:{row.BirthsPeriod}:{row.DeathsPeriod}:{row.FoodStored}:{row.FoodProducedPeriod}:{row.FoodConsumedPeriod}:{row.WoodStored}:{row.StoneStored}:{row.ShelterCapacity}:{row.AverageHealth}:{row.AverageHunger}";
}
