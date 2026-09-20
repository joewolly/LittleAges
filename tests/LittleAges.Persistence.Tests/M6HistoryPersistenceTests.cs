using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Xunit;

namespace LittleAges.Persistence.Tests;

public sealed class M6HistoryPersistenceTests
{
    private static readonly HistoricalEventType[] InitializationTypes = [HistoricalEventType.WorldCreated, HistoricalEventType.SettlementFounded, HistoricalEventType.SeasonStarted];

    [Theory]
    [InlineData(SimulationEngine.M6SimulationRulesVersion)]
    [InlineData(SimulationEngine.M8SimulationRulesVersion)]
    public async Task SameMinutePartnerDeathsCheckpointAndReloadWithoutChangingHistory(string rules)
    {
        await WithDatabaseAsync(async path =>
        {
            var engine = new SimulationEngine(new WorldSeed(0), simulationRulesVersion: rules);
            var partners = Citizens(engine).Values.Where(citizen => citizen.AgeYears(engine.CurrentMinute) >= 18).OrderBy(citizen => citizen.Id.Value).Take(2).ToArray();
            var relationships = Assert.IsType<Dictionary<(long CitizenAId, long CitizenBId), RelationshipState>>(typeof(SimulationEngine).GetField("_relationships", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
            var pair = RelationshipState.Normalize(partners[0].Id, partners[1].Id);
            var relationship = new RelationshipState(pair.A, pair.B, 6000, 6000, 5000, 0, 0, 1);
            relationships.Add((pair.A.Value, pair.B.Value), relationship);
            InvokePrivate(engine, "TryFormPartnership", partners[0], partners[1], relationship);
            InvokePrivate(engine, "RecordPartnershipHistory", partners[0], partners[1]);
            InvokePrivate(engine, "KillNatural", partners[0]);
            InvokePrivate(engine, "KillNatural", partners[1]);
            var memory = Assert.Single(engine.Memories, item => item.MemoryType == MemoryType.PartnerDied);
            var fingerprint = engine.HistoryFingerprint;
            await using (var database = await WorldDatabase.OpenAsync(path))
                await database.CreateCheckpointStore().CheckpointAsync(engine.CreatePersistenceSnapshot());
            await using var reopened = await WorldDatabase.OpenAsync(path);
            var restored = SimulationEngine.FromPersistenceSnapshot(await reopened.CreateCheckpointStore().LoadAsync());
            Assert.Equal(fingerprint, restored.HistoryFingerprint);
            Assert.Contains(memory, restored.Memories);
            engine.AdvanceUntil(new WorldMinute(1440));
            restored.AdvanceUntil(new WorldMinute(1440));
            Assert.Equal(engine.HistoryFingerprint, restored.HistoryFingerprint);
        });
    }

    [Fact]
    public async Task M8CheckpointWithMoreThanTwentyCitizensReopensWithoutM2SentinelRejection()
    {
        await WithDatabaseAsync(async path =>
        {
            var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M8SimulationRulesVersion);
            var citizens = Citizens(source);
            var parents = citizens.Values
                .Where(citizen => citizen.FounderOrdinal is not null && (0L - citizen.BirthMinute) / WorldCalendar.MinutesPerYear is >= 18 and <= 45)
                .OrderBy(citizen => citizen.Id.Value)
                .Take(2)
                .ToArray();
            Assert.Equal(2, parents.Length);
            var counters = Assert.IsType<DeterministicCounters>(typeof(SimulationEngine).GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source));
            var childId = counters.AllocateCitizenId();
            var child = new Citizen(childId, (int?)null, "M8Child", parents[0].FamilyName, 0, parents[0].Location, parents[0].Traits, new CitizenSkills(0, 0, 0, 0, 0, 0))
            {
                ParentAId = parents[0].Id,
                ParentBId = parents[1].Id,
                NeedsUpdatedMinute = source.CurrentMinute.Value,
                HealthUpdatedMinute = source.CurrentMinute.Value
            };
            citizens.Add(child.Id.Value, child);
            InvokePrivate(source, "ScheduleCitizen", child, CitizenEventNames.Decision, source.CurrentMinute, CitizenEventNames.DecisionPriority);
            InvokePrivate(source, "ScheduleSurvival", child);
            var snapshot = source.CreatePersistenceSnapshot();
            Assert.Equal(21, snapshot.Citizens.Count);
            var beforeReload = SimulationEngine.FromPersistenceSnapshot(snapshot);

            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                await database.CreateCheckpointStore().CheckpointAsync(snapshot, new DateTime(2026, 9, 17, 4, 0, 0, DateTimeKind.Utc));
            }

            await using var reopened = await WorldDatabase.OpenAsync(path);
            var loaded = await reopened.CreateCheckpointStore().LoadAsync();
            Assert.Equal(SimulationEngine.M8SimulationRulesVersion, loaded.SimulationRulesVersion);
            Assert.Equal(SimulationEngine.CitizenGenerationVersion, loaded.CitizenGenerationVersion);
            Assert.Equal(SimulationEngine.SurvivalVersion, loaded.SurvivalVersion);
            Assert.Equal(SimulationEngine.SettlementVersion, loaded.SettlementVersion);
            Assert.Equal(SimulationEngine.SocialVersion, loaded.SocialVersion);
            Assert.Equal(SimulationEngine.HistoryVersion, loaded.HistoryVersion);
            Assert.Equal(snapshot.Citizens.Count, loaded.Citizens.Count);
            var afterReload = SimulationEngine.FromPersistenceSnapshot(loaded);
            Assert.Equal(beforeReload.SurvivalFingerprint, afterReload.SurvivalFingerprint);
            Assert.Equal(beforeReload.SettlementFingerprint, afterReload.SettlementFingerprint);
            Assert.Equal(beforeReload.SocialFingerprint, afterReload.SocialFingerprint);
            Assert.Equal(beforeReload.HistoryFingerprint, afterReload.HistoryFingerprint);
        });
    }

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
            Assert.Equal(SimulationEngine.M6SimulationRulesVersion, upgraded.SimulationRulesVersion);
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
    public async Task M5ToM6BackfillOmitsCurrentHouseholdAfterDescendantFormsPartnership()
    {
        await WithDatabaseAsync(async path =>
        {
            var currentMinute = 1L;
            var source = new SimulationEngine(new WorldSeed(0), simulationRulesVersion: SimulationEngine.M5SimulationRulesVersion);
            source.AdvanceUntil(new WorldMinute(currentMinute));
            var counters = Assert.IsType<DeterministicCounters>(typeof(SimulationEngine).GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source));
            var citizens = Citizens(source);
            var households = Households(source);
            var relationships = Relationships(source);
            var birthMinute = 0L;
            var eligible = citizens.Values.Where(x => x.FounderOrdinal is not null && x.AgeYears(new WorldMinute(birthMinute)) is >= 18 and <= 45).OrderBy(x => x.Id.Value).ToArray();
            Assert.True(eligible.Length >= 3);
            var parentA = eligible[0];
            var parentB = eligible[1];
            var partner = eligible[2];
            var childId = counters.AllocateCitizenId();
            var householdA = new Household(counters.AllocateHouseholdId(), birthMinute);
            var householdB = new Household(counters.AllocateHouseholdId(), currentMinute);

            parentA.PartnerId = parentB.Id;
            parentA.HouseholdId = householdA.Id;
            parentB.PartnerId = parentA.Id;
            parentB.HouseholdId = householdA.Id;
            partner.PartnerId = childId;
            partner.HouseholdId = householdB.Id;
            var child = new Citizen(childId, (int?)null, "MigrationChild", parentA.FamilyName, birthMinute, parentA.Location,
                new CitizenTraits(0, 0, 0, 0, 0, 0), new CitizenSkills(0, 0, 0, 0, 0, 0))
            {
                ParentAId = parentA.Id,
                ParentBId = parentB.Id,
                PartnerId = partner.Id,
                HouseholdId = householdB.Id,
                NeedsUpdatedMinute = currentMinute,
                HealthUpdatedMinute = currentMinute
            };
            citizens.Add(child.Id.Value, child);
            households.Add(householdA.Id.Value, householdA);
            households.Add(householdB.Id.Value, householdB);
            relationships.Add((Math.Min(parentA.Id.Value, parentB.Id.Value), Math.Max(parentA.Id.Value, parentB.Id.Value)), PairRelationship(parentA.Id, parentB.Id, currentMinute));
            relationships.Add((Math.Min(partner.Id.Value, child.Id.Value), Math.Max(partner.Id.Value, child.Id.Value)), PairRelationship(partner.Id, child.Id, currentMinute));
            InvokePrivate(source, "ScheduleCitizen", child, CitizenEventNames.Decision, new WorldMinute(currentMinute), CitizenEventNames.DecisionPriority);
            InvokePrivate(source, "ScheduleSurvival", child);
            var m5 = source.CreatePersistenceSnapshot();

            await using (var database = await WorldDatabase.OpenAsync(path))
            {
                var store = database.CreateCheckpointStore();
                await store.CheckpointAsync(m5, new DateTime(2026, 9, 16, 3, 0, 0, DateTimeKind.Utc));
                await store.UpgradeM5ToM6IfNeededAsync();
                var upgraded = await store.LoadAsync();
                Assert.Contains(upgraded.Citizens, item => item.Id == child.Id);
                Assert.Equal(birthMinute, upgraded.Citizens.Single(item => item.Id == child.Id).BirthMinute);
                var birth = Assert.Single(upgraded.HistoricalEvents, item => item.EventType == HistoricalEventType.CitizenBorn && upgraded.HistoricalEventCitizens.Any(link => link.HistoricalEventId == item.Id && link.CitizenId == child.Id && link.Role == "subject"));
                Assert.Equal("{}", birth.PayloadJson);

                var arbitraryCurrentHouseholdPayload = HistoricalEventPayloads.CitizenBorn(householdB.Id);
                await database.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE historical_events SET payload_json = {arbitraryCurrentHouseholdPayload} WHERE id = {birth.Id.Value}");
                await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
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
        var engine = new SimulationEngine(seed, simulationRulesVersion: SimulationEngine.M6SimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(checked(days * WorldCalendar.MinutesPerDay)));
        return engine;
    }

    private static SimulationEngine AdvanceM5(WorldSeed seed, long days)
    {
        var engine = new SimulationEngine(seed, simulationRulesVersion: SimulationEngine.M5SimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(checked(days * WorldCalendar.MinutesPerDay)));
        return engine;
    }

    private static RelationshipState PairRelationship(CitizenId first, CitizenId second, long minute)
    {
        var pair = RelationshipState.Normalize(first, second);
        return new RelationshipState(pair.A, pair.B, 10_000, 10_000, 10_000, 0, minute, 1);
    }
    private static Dictionary<long, Citizen> Citizens(SimulationEngine engine) => Assert.IsType<Dictionary<long, Citizen>>(typeof(SimulationEngine).GetField("_citizens", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static Dictionary<long, Household> Households(SimulationEngine engine) => Assert.IsType<Dictionary<long, Household>>(typeof(SimulationEngine).GetField("_households", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static Dictionary<(long, long), RelationshipState> Relationships(SimulationEngine engine) => Assert.IsType<Dictionary<(long, long), RelationshipState>>(typeof(SimulationEngine).GetField("_relationships", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static void InvokePrivate(SimulationEngine engine, string method, params object[] arguments) => typeof(SimulationEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, arguments);

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
