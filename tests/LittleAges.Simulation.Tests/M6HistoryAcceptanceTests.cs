using System.Globalization;
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
        var m6 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
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
        var engine = new SimulationEngine(new WorldSeed(0), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
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
        var engine = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
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
        var engine = new SimulationEngine(seed, simulationRulesVersion: SimulationEngine.CurrentSimulationRulesVersion);
        engine.AdvanceUntil(new WorldMinute(checked(days * WorldCalendar.MinutesPerDay)));
        return engine;
    }

    private static string Golden(ulong seed, long days) => (seed, days) switch
    {
        (42UL, 360L) => "da75479a17efeddea22a5f0774ba9fdca8ea2d585cc5f3e10ab0cb2ef65f2454",
        (0UL, 7L) => "944dbbd4f643c9576d1acbc2a61afe29caa80f41a50768c18d7e925cb7adf17f",
        (ulong.MaxValue, 7L) => "8cc6b5597f958df69694716179ecef10c3264757e41c3c70ccbe357349c3f0d1",
        _ => throw new ArgumentOutOfRangeException(nameof(seed))
    };
}
