using System.Globalization;
using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M6HistoryAcceptanceTests
{
    private static readonly long[] InitialHistoryIds = [1L, 2L, 3L];
    private static readonly HistoricalEventType[] InitialHistoryTypes = [HistoricalEventType.WorldCreated, HistoricalEventType.SettlementFounded, HistoricalEventType.SeasonStarted];

    [Fact]
    public void FreshM6InitializationHasCanonicalHistoryOrderPayloadsAndIsolatedCounters()
    {
        var m5 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M5SimulationRulesVersion);
        var m6 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M6SimulationRulesVersion);
        var history = m6.CreateHistoryReadSnapshot();
        Assert.NotNull(history);

        var snapshot = history!;
        Assert.Equal(SimulationEngine.HistoryVersion, snapshot.Version);
        Assert.Equal(0, snapshot.State.HistoryStartMinute);
        Assert.Equal(1, snapshot.State.HistoryStartEventId);
        Assert.Equal(InitialHistoryIds, snapshot.Events.Select(item => item.Id.Value));
        Assert.Equal(InitialHistoryTypes, snapshot.Events.Select(item => item.EventType));
        Assert.All(snapshot.Events, item =>
        {
            Assert.Equal(0, item.WorldMinute);
            Assert.Equal(HistoricalEventOrigin.Live, item.Origin);
            item.Validate(0);
        });
        Assert.Equal(HistoricalImportance.Historic, snapshot.Events[0].Importance);
        Assert.Equal(HistoricalImportance.Historic, snapshot.Events[1].Importance);
        Assert.Equal(HistoricalImportance.Routine, snapshot.Events[2].Importance);
        Assert.Equal("{\"seed\":\"42\"}", snapshot.Events[0].PayloadJson);
        Assert.Equal("{\"founderCount\":20}", snapshot.Events[1].PayloadJson);
        Assert.Equal("{\"season\":\"Spring\",\"year\":0}", snapshot.Events[2].PayloadJson);
        Assert.Equal(20, snapshot.CitizenLinks.Count);
        Assert.All(snapshot.CitizenLinks, link => Assert.Equal("founder", link.Role));

        Assert.Equal(m5.CounterSnapshot.NextEntityId, m6.CounterSnapshot.NextEntityId);
        Assert.Equal(m5.CounterSnapshot.NextScheduledEventSequence + 1, m6.CounterSnapshot.NextScheduledEventSequence);
        Assert.Equal(4, m6.CounterSnapshot.NextHistoricalEventId);
        var statistics = Assert.Single(m6.CreatePersistenceSnapshot().ScheduledEvents, item => item.Name == CitizenEventNames.StatisticsSample);
        Assert.Equal(30L * WorldCalendar.MinutesPerDay, statistics.Order.DueWorldMinute.Value);
        Assert.Equal(CitizenEventNames.StatisticsSamplePriority, statistics.Order.Priority);
        Assert.Equal("{\"version\":1}", statistics.PayloadJson);
    }

    [Theory]
    [InlineData(42UL, 360L)]
    [InlineData(0UL, 7L)]
    [InlineData(ulong.MaxValue, 7L)]
    public void M6HistoryFingerprintGoldenVectorsAreRepeatable(ulong seed, long days)
    {
        var first = Advance(new WorldSeed(seed), days);
        var second = Advance(new WorldSeed(seed), days);

        Assert.Equal(first.HistoryFingerprint, second.HistoryFingerprint);
        Assert.Equal(first.CreateHistoryReadSnapshot()!.Events, second.CreateHistoryReadSnapshot()!.Events);
        Assert.Equal(first.CreateHistoryReadSnapshot()!.Statistics, second.CreateHistoryReadSnapshot()!.Statistics);
        Assert.Equal(Golden(seed, days), first.HistoryFingerprint);
        if (seed == 42UL && days == 360L)
        {
            Assert.Contains(first.HistoricalEvents, item => item.EventType is not (HistoricalEventType.WorldCreated or HistoricalEventType.SettlementFounded or HistoricalEventType.SeasonStarted));
            Assert.NotEmpty(first.StatisticsSamples);
        }
    }

    [Fact]
    public void M6HistoryFingerprintIsInvariantUnderCustomNegativeSignCulture()
    {
        var invariant = Advance(new WorldSeed(0), 7).HistoryFingerprint;
        var oldCulture = CultureInfo.CurrentCulture;
        var oldUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var custom = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            custom.NumberFormat.NegativeSign = "~";
            CultureInfo.CurrentCulture = custom;
            CultureInfo.CurrentUICulture = custom;
            Assert.Equal(invariant, Advance(new WorldSeed(0), 7).HistoryFingerprint);
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUiCulture;
        }
    }

    [Fact]
    public void M6StatisticsUseExactMonthlyCadenceAndResetPeriodCounters()
    {
        var engine = new SimulationEngine(new WorldSeed(0), simulationRulesVersion: SimulationEngine.M6SimulationRulesVersion);
        var before = engine.CounterSnapshot;
        var control = new SimulationEngine(new WorldSeed(0), simulationRulesVersion: SimulationEngine.M5SimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(30L * WorldCalendar.MinutesPerDay));
        control.AdvanceUntil(new WorldMinute(30L * WorldCalendar.MinutesPerDay));
        var sample = Assert.Single(engine.StatisticsSamples);

        Assert.Equal(30L * WorldCalendar.MinutesPerDay, sample.WorldMinute);
        Assert.Equal(0, sample.PeriodStartMinute);
        Assert.Equal(engine.LivingPopulation, sample.Population);
        Assert.InRange(sample.AverageHealth, 0, 10_000);
        Assert.InRange(sample.AverageHunger, 0, 10_000);
        Assert.Equal(30L * WorldCalendar.MinutesPerDay, engine.HistoryState!.PeriodStartMinute);
        Assert.Equal(0, engine.HistoryState.BirthsSinceSample);
        Assert.Equal(0, engine.HistoryState.DeathsSinceSample);
        Assert.Equal(0, engine.HistoryState.FoodProducedSinceSample);
        Assert.Equal(0, engine.HistoryState.FoodConsumedSinceSample);
        var next = Assert.Single(engine.CreatePersistenceSnapshot().ScheduledEvents, item => item.Name == CitizenEventNames.StatisticsSample);
        Assert.Equal(60L * WorldCalendar.MinutesPerDay, next.Order.DueWorldMinute.Value);
        Assert.Equal(control.CounterSnapshot.NextEntityId, engine.CounterSnapshot.NextEntityId);
        Assert.True(engine.CounterSnapshot.NextScheduledEventSequence > before.NextScheduledEventSequence);
    }

    [Fact]
    public void FreshM6RejectsNonzeroInitialMinuteButM5RetainsCompatibility()
    {
        Assert.Throws<ArgumentException>(() => new SimulationEngine(new WorldSeed(0), new WorldMinute(1), simulationRulesVersion: SimulationEngine.M6SimulationRulesVersion));

        var m5 = new SimulationEngine(new WorldSeed(0), new WorldMinute(1), simulationRulesVersion: SimulationEngine.M5SimulationRulesVersion);
        Assert.Equal(1, m5.CurrentMinute.Value);
    }

    [Fact]
    public void SameMinuteBirthsEmitMilestoneBetweenIndividualBirthTransitions()
    {
        var engine = new SimulationEngine(new WorldSeed(0), simulationRulesVersion: SimulationEngine.M6SimulationRulesVersion);
        var counters = Assert.IsType<DeterministicCounters>(typeof(SimulationEngine).GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        var citizens = Citizens(engine);
        for (var index = 0; index < 4; index++)
        {
            var extra = new Citizen(counters.AllocateCitizenId(), null, $"Extra{index}", "History", -WorldCalendar.MinutesPerYear, engine.World.StartingSite, new CitizenTraits(0, 0, 0, 0, 0, 0), new CitizenSkills(0, 0, 0, 0, 0, 0));
            extra.NeedsUpdatedMinute = engine.CurrentMinute.Value;
            extra.HealthUpdatedMinute = engine.CurrentMinute.Value;
            citizens.Add(extra.Id.Value, extra);
        }

        var parents = citizens.Values.Where(x => x.FounderOrdinal is not null).OrderBy(x => x.Id.Value).Take(2).ToArray();
        var household = new Household(counters.AllocateHouseholdId(), engine.CurrentMinute.Value);
        Households(engine).Add(household.Id.Value, household);
        InvokePrivate(engine, "CreateChild", parents[0], parents[1], household);
        InvokePrivate(engine, "CreateChild", parents[0], parents[1], household);

        var transitions = engine.HistoricalEvents.Where(x => x.EventType is HistoricalEventType.CitizenBorn or HistoricalEventType.PopulationMilestone).TakeLast(3).ToArray();
        Assert.Equal([HistoricalEventType.CitizenBorn, HistoricalEventType.PopulationMilestone, HistoricalEventType.CitizenBorn], transitions.Select(x => x.EventType));
        Assert.Equal("{\"population\":25}", transitions[1].PayloadJson);
        Assert.Equal(25, engine.HistoryState!.PopulationMilestoneWatermark);
    }

    [Fact]
    public void PopulationShortageUsesIndividualDeathPopulationBeforeGlobalEventSettles()
    {
        var engine = new SimulationEngine(new WorldSeed(0), simulationRulesVersion: SimulationEngine.M6SimulationRulesVersion);
        var counters = Assert.IsType<DeterministicCounters>(typeof(SimulationEngine).GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        var citizens = Citizens(engine);
        for (var index = 0; index < 4; index++)
        {
            var extra = new Citizen(counters.AllocateCitizenId(), null, $"Extra{index}", "Shortage", -WorldCalendar.MinutesPerYear, engine.World.StartingSite, new CitizenTraits(0, 0, 0, 0, 0, 0), new CitizenSkills(0, 0, 0, 0, 0, 0));
            extra.NeedsUpdatedMinute = engine.CurrentMinute.Value;
            extra.HealthUpdatedMinute = engine.CurrentMinute.Value;
            citizens.Add(extra.Id.Value, extra);
        }

        engine.Settlement.FoodStored = 199;
        var deaths = citizens.Values.Where(x => x.FounderOrdinal is not null).OrderBy(x => x.Id.Value).Take(2).ToArray();
        foreach (var citizen in deaths)
        {
            citizen.Health = 0;
            citizen.DeathMinute = engine.CurrentMinute.Value;
            citizen.DeathCause = "natural";
            citizen.CurrentAction = CitizenAction.Dead;
            InvokePrivate(engine, "RecordDeathHistory", citizen);
        }

        var shortage = Assert.Single(engine.HistoricalEvents, x => x.EventType == HistoricalEventType.ResourceShortageStarted);
        using var payload = System.Text.Json.JsonDocument.Parse(shortage.PayloadJson);
        Assert.Equal(23, payload.RootElement.GetProperty("livingPopulation").GetInt32());
        Assert.Equal(22, engine.LivingPopulation);
    }

    [Theory]
    [InlineData(SimulationEngine.M6SimulationRulesVersion)]
    [InlineData(SimulationEngine.CurrentSimulationRulesVersion)]
    public void PartnerDeathMemorySurvivesRecipientsLaterDeathInTheSameMinute(string rules)
    {
        var engine = new SimulationEngine(new WorldSeed(0), simulationRulesVersion: rules);
        var partners = Citizens(engine).Values.Where(citizen => citizen.AgeYears(engine.CurrentMinute) >= 18).OrderBy(citizen => citizen.Id.Value).Take(2).ToArray();
        var relationships = Assert.IsType<Dictionary<(long CitizenAId, long CitizenBId), RelationshipState>>(typeof(SimulationEngine).GetField("_relationships", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        var pair = RelationshipState.Normalize(partners[0].Id, partners[1].Id);
        var relationship = new RelationshipState(pair.A, pair.B, 6000, 6000, 5000, 0, 0, 1);
        relationships.Add((pair.A.Value, pair.B.Value), relationship);
        InvokePrivate(engine, "TryFormPartnership", partners[0], partners[1], relationship);
        Assert.Equal(partners[1].Id, partners[0].PartnerId);
        InvokePrivate(engine, "RecordPartnershipHistory", partners[0], partners[1]);
        InvokePrivate(engine, "KillNatural", partners[0]);
        var memory = Assert.Single(engine.Memories, item => item.MemoryType == MemoryType.PartnerDied);
        Assert.Equal(partners[1].Id, memory.CitizenId);
        InvokePrivate(engine, "KillNatural", partners[1]);
        Assert.Equal(partners[0].DeathMinute, partners[1].DeathMinute);

        var fingerprint = engine.HistoryFingerprint;
        var restored = SimulationEngine.FromPersistenceSnapshot(engine.CreatePersistenceSnapshot());
        Assert.Equal(fingerprint, restored.HistoryFingerprint);
        Assert.Contains(memory, restored.Memories);

        // The citizen who died first must not acquire a memory of the later
        // death merely because both transitions share a timestamp.
        var laterDeath = engine.HistoricalEvents.Last(item => item.EventType == HistoricalEventType.CitizenDied);
        var memories = Assert.IsType<List<CitizenMemory>>(typeof(SimulationEngine).GetField("_memories", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        memories.Add(new CitizenMemory(partners[0].Id, laterDeath.Id, MemoryType.PartnerDied, laterDeath.Importance, -9000, laterDeath.WorldMinute));
        Assert.Throws<ArgumentException>(() => engine.CreatePersistenceSnapshot());
    }

    [Fact]
    public void LiveCitizenBornValidationUsesBirthHouseholdAfterCurrentHouseholdChanges()
    {
        var engine = new SimulationEngine(new WorldSeed(0), simulationRulesVersion: SimulationEngine.M6SimulationRulesVersion);
        var counters = Assert.IsType<DeterministicCounters>(typeof(SimulationEngine).GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        var citizens = Citizens(engine);
        var households = Households(engine);
        var relationships = Assert.IsType<Dictionary<(long CitizenAId, long CitizenBId), RelationshipState>>(typeof(SimulationEngine).GetField("_relationships", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        var parents = citizens.Values.Where(x => x.FounderOrdinal is not null).OrderBy(x => x.Id.Value).Take(2).ToArray();
        var birthHousehold = new Household(counters.AllocateHouseholdId(), 0);
        households.Add(birthHousehold.Id.Value, birthHousehold);
        parents[0].PartnerId = parents[1].Id;
        parents[1].PartnerId = parents[0].Id;
        parents[0].HouseholdId = birthHousehold.Id;
        parents[1].HouseholdId = birthHousehold.Id;
        var pair = RelationshipState.Normalize(parents[0].Id, parents[1].Id);
        relationships.Add((pair.A.Value, pair.B.Value), new RelationshipState(pair.A, pair.B, 6000, 6000, 5000, 0, 0, 1));
        InvokePrivate(engine, "RecordPartnershipHistory", parents[0], parents[1]);

        var child = new Citizen(counters.AllocateCitizenId(), (int?)null, "BirthProof", parents[0].FamilyName, 0, parents[0].Location,
            new CitizenTraits(0, 0, 0, 0, 0, 0), new CitizenSkills(0, 0, 0, 0, 0, 0))
        {
            ParentAId = parents[0].Id,
            ParentBId = parents[1].Id,
            HouseholdId = birthHousehold.Id,
            NeedsUpdatedMinute = 0,
            HealthUpdatedMinute = 0
        };
        citizens.Add(child.Id.Value, child);
        InvokePrivate(engine, "RecordBirthHistory", child);
        InvokePrivate(engine, "ScheduleCitizen", child, CitizenEventNames.Decision, new WorldMinute(0), CitizenEventNames.DecisionPriority);
        InvokePrivate(engine, "ScheduleSurvival", child);

        var birth = Assert.Single(engine.HistoricalEvents, item => item.EventType == HistoricalEventType.CitizenBorn && engine.HistoricalEventCitizens.Any(link => link.HistoricalEventId == item.Id && link.CitizenId == child.Id && link.Role == "subject"));
        Assert.Equal(HistoricalEventPayloads.CitizenBorn(birthHousehold.Id), birth.PayloadJson);

        var currentHousehold = new Household(counters.AllocateHouseholdId(), 0);
        households.Add(currentHousehold.Id.Value, currentHousehold);
        child.HouseholdId = currentHousehold.Id;
        var historyFingerprint = engine.HistoryFingerprint;

        var restored = SimulationEngine.FromPersistenceSnapshot(engine.CreatePersistenceSnapshot());
        var restoredBirth = Assert.Single(restored.HistoricalEvents, item => item.Id == birth.Id);
        Assert.Equal(HistoricalEventPayloads.CitizenBorn(birthHousehold.Id), restoredBirth.PayloadJson);
        Assert.Equal(historyFingerprint, restored.HistoryFingerprint);
        Assert.Equal(currentHousehold.Id, restored.Citizens.Single(item => item.Id == child.Id).HouseholdId);
    }

    [Fact]
    public void ExplicitM5RulesRetainNoHistoryAndTheLockedM5Golden()
    {
        var engine = new SimulationEngine(new WorldSeed(0), simulationRulesVersion: SimulationEngine.M5SimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(7L * WorldCalendar.MinutesPerDay));

        Assert.Equal(0, engine.CreatePersistenceSnapshot().HistoryVersion);
        Assert.Null(engine.CreateHistoryReadSnapshot());
        Assert.Equal("c89ececb36dea0cff34ce1e8ad7da885a8c5ac3dc457b4bdeb62d1ef95556de1", engine.SocialFingerprint);
    }

    [Fact]
    [Trait("Category", "Long")]
    public void Seed42TenYearHistoryReportPath()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M6SimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(10L * WorldCalendar.MinutesPerYear));
        stopwatch.Stop();

        var history = engine.CreateHistoryReadSnapshot();
        Assert.NotNull(history);
        Assert.Contains(history!.Events, item => item.EventType == HistoricalEventType.WorldCreated);
        Assert.Contains(history.Events, item => item.EventType == HistoricalEventType.SettlementFounded);
        Assert.Contains(history.Events, item => item.EventType == HistoricalEventType.SeasonStarted);
        Assert.Contains(history.Events, item => item.EventType == HistoricalEventType.StructureStarted);
        Assert.Contains(history.Events, item => item.EventType == HistoricalEventType.StructureCompleted);
        Assert.Contains(history.Events, item => item.EventType == HistoricalEventType.PartnershipFormed);
        Assert.Contains(history.Events, item => item.EventType == HistoricalEventType.CitizenBorn);
        Assert.Contains(history.Events, item => item.EventType is HistoricalEventType.FriendshipFormed or HistoricalEventType.RivalryFormed);
        Assert.NotEmpty(history.Statistics);

        var descendant = engine.Citizens.Where(citizen => citizen.FounderOrdinal is null).OrderBy(citizen => citizen.BirthMinute).ThenBy(citizen => citizen.Id.Value).FirstOrDefault();
        Assert.NotNull(descendant);
        var birth = Assert.Single(history.Events, item =>
            item.EventType == HistoricalEventType.CitizenBorn &&
            history.CitizenLinks.Any(link => link.HistoricalEventId == item.Id && link.CitizenId == descendant!.Id && link.Role == "subject"));
        Assert.Equal(descendant!.BirthMinute, birth.WorldMinute);
        Assert.Equal(2, history.CitizenLinks.Count(link => link.HistoricalEventId == birth.Id && link.Role == "parent"));

        var snapshot = engine.CreatePersistenceSnapshot();
        var restored = SimulationEngine.FromPersistenceSnapshot(snapshot);
        Assert.Equal(engine.HistoryFingerprint, restored.HistoryFingerprint);
        Assert.True(engine.HistoricalEvents.Count < 1_000_000, "Meaningful history must remain far below action-frequency scale.");
        Assert.True(engine.HistoricalEvents.Count < engine.ProcessedEventCount, "History must not be emitted for every processed simulation action.");

        var eventsByType = engine.HistoricalEvents.GroupBy(item => item.EventType).OrderBy(group => group.Key).ToDictionary(group => group.Key, group => group.Count());
        var eventsByImportance = engine.HistoricalEvents.GroupBy(item => item.Importance).OrderBy(group => group.Key).ToDictionary(group => group.Key, group => group.Count());
        var firstMinute = (HistoricalEventType type) => engine.HistoricalEvents.Where(item => item.EventType == type).Select(item => (long?)item.WorldMinute).Min()?.ToString(CultureInfo.InvariantCulture) ?? "none";
        var milestoneText = string.Join(",", engine.HistoricalEvents.Where(item => item.EventType == HistoricalEventType.PopulationMilestone).Select(item => item.PayloadJson));
        Console.WriteLine($"seed=42 years=10 runtime={stopwatch.Elapsed};events={engine.HistoricalEvents.Count};by-type={string.Join(',', eventsByType.Select(item => $"{item.Key}:{item.Value}"))};by-importance={string.Join(',', eventsByImportance.Select(item => $"{item.Key}:{item.Value}"))};citizen-links={engine.HistoricalEventCitizens.Count};structure-links={engine.HistoricalEventStructures.Count};memories={engine.Memories.Count};samples={engine.StatisticsSamples.Count};first-birth-minute={firstMinute(HistoricalEventType.CitizenBorn)};first-partnership-minute={firstMinute(HistoricalEventType.PartnershipFormed)};first-friendship-minute={firstMinute(HistoricalEventType.FriendshipFormed)};first-rivalry-minute={firstMinute(HistoricalEventType.RivalryFormed)};structures-started={eventsByType.GetValueOrDefault(HistoricalEventType.StructureStarted)};structures-completed={eventsByType.GetValueOrDefault(HistoricalEventType.StructureCompleted)};shortage-starts={eventsByType.GetValueOrDefault(HistoricalEventType.ResourceShortageStarted)};shortage-ends={eventsByType.GetValueOrDefault(HistoricalEventType.ResourceShortageEnded)};milestones={milestoneText};fingerprint={engine.HistoryFingerprint}");
    }

    private static SimulationEngine Advance(WorldSeed seed, long days)
    {
        var engine = new SimulationEngine(seed, simulationRulesVersion: SimulationEngine.M6SimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(checked(days * WorldCalendar.MinutesPerDay)));
        return engine;
    }

    private static Dictionary<long, Citizen> Citizens(SimulationEngine engine) => Assert.IsType<Dictionary<long, Citizen>>(typeof(SimulationEngine).GetField("_citizens", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static Dictionary<long, Household> Households(SimulationEngine engine) => Assert.IsType<Dictionary<long, Household>>(typeof(SimulationEngine).GetField("_households", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
    private static void InvokePrivate(SimulationEngine engine, string method, params object[] arguments) => typeof(SimulationEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, arguments);

    private static string Golden(ulong seed, long days) => (seed, days) switch
    {
        (42UL, 360L) => "da75479a17efeddea22a5f0774ba9fdca8ea2d585cc5f3e10ab0cb2ef65f2454",
        (0UL, 7L) => "944dbbd4f643c9576d1acbc2a61afe29caa80f41a50768c18d7e925cb7adf17f",
        (ulong.MaxValue, 7L) => "8cc6b5597f958df69694716179ecef10c3264757e41c3c70ccbe357349c3f0d1",
        _ => throw new ArgumentOutOfRangeException(nameof(seed))
    };
}
