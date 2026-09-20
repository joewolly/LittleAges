using System.Reflection;
using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class M8BalanceAcceptanceTests
{
    [Fact]
    public void M8FreshHistoryUsesSampledTwentyRecoveryWhileM6RemainsImmediateTwenty()
    {
        var m6 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M6SimulationRulesVersion);
        var m8 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M8SimulationRulesVersion);

        Assert.Equal(SimulationEngine.M6SimulationRulesVersion, m6.SimulationRulesVersion);
        Assert.Equal(SimulationEngine.M8SimulationRulesVersion, m8.SimulationRulesVersion);
        Assert.Equal(20, SimulationEngine.FoodShortageRecoveryMultiplier(m6.SimulationRulesVersion));
        Assert.Equal(20, SimulationEngine.FoodShortageRecoveryMultiplier(m8.SimulationRulesVersion));

        var m6Living = m6.LivingPopulation;
        m6.Settlement.FoodStored = m6Living * 10 - 1;
        InvokePrivate(m6, "ReevaluateFoodShortage", false);
        Assert.True(m6.HistoryState!.ActiveFoodShortage);
        m6.Settlement.FoodStored = m6Living * 20 - 1;
        InvokePrivate(m6, "ReevaluateFoodShortage", false);
        Assert.True(m6.HistoryState.ActiveFoodShortage);
        m6.Settlement.FoodStored = m6Living * 20;
        InvokePrivate(m6, "ReevaluateFoodShortage", false);
        Assert.False(m6.HistoryState.ActiveFoodShortage);

        var m8Living = m8.LivingPopulation;
        m8.Settlement.FoodStored = m8Living * 10 - 1;
        InvokePrivate(m8, "ReevaluateFoodShortage", false);
        Assert.True(m8.HistoryState!.ActiveFoodShortage);
        m8.Settlement.FoodStored = m8Living * 20 - 1;
        InvokePrivate(m8, "ReevaluateFoodShortage", false);
        Assert.True(m8.HistoryState.ActiveFoodShortage);
        m8.Settlement.FoodStored = m8Living * 20;
        InvokePrivate(m8, "ReevaluateFoodShortage", false);
        Assert.True(m8.HistoryState.ActiveFoodShortage);
        const long sampleMinute = 30L * WorldCalendar.MinutesPerDay;
        m8.AdvanceUntil(new WorldMinute(sampleMinute - 1));
        m8.Settlement.FoodStored = m8.LivingPopulation * 20;
        m8.AdvanceUntil(new WorldMinute(sampleMinute));
        Assert.False(m8.HistoryState.ActiveFoodShortage);
        var end = Assert.Single(m8.HistoricalEvents, item => item.EventType == HistoricalEventType.ResourceShortageEnded);
        Assert.Equal(sampleMinute, end.WorldMinute);
        var sample = Assert.Single(m8.StatisticsSamples, item => item.WorldMinute == sampleMinute);
        Assert.Contains($"\"quantity\":{sample.FoodStored}", end.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public void M8OrdinaryRecoveredFoodDoesNotEndShortageAndReloadPreservesIt()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M8SimulationRulesVersion);
        var living = source.LivingPopulation;
        source.Settlement.FoodStored = living * 10 - 1;
        InvokePrivate(source, "ReevaluateFoodShortage", false);
        Assert.True(source.HistoryState!.ActiveFoodShortage);

        source.AdvanceUntil(new WorldMinute(1));
        source.Settlement.FoodStored = living * 20;
        InvokePrivate(source, "ReevaluateFoodShortage", false);
        Assert.True(source.HistoryState.ActiveFoodShortage);
        var restored = SimulationEngine.FromPersistenceSnapshot(source.CreatePersistenceSnapshot());
        InvokePrivate(restored, "ReevaluateFoodShortage", false);
        Assert.True(restored.HistoryState!.ActiveFoodShortage);
        source.AdvanceUntil(new WorldMinute(720));
        restored.AdvanceUntil(new WorldMinute(720));
        Assert.Equal(source.HistoryFingerprint, restored.HistoryFingerprint);
        Assert.DoesNotContain(restored.HistoricalEvents, item => item.EventType == HistoricalEventType.ResourceShortageEnded);
    }

    [Fact]
    public void M8ShortRunFingerprintIsRepeatableAndChunkIndependent()
    {
        var whole = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M8SimulationRulesVersion);
        var chunked = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M8SimulationRulesVersion);
        whole.AdvanceUntil(new WorldMinute(720));
        chunked.AdvanceUntil(new WorldMinute(240));
        chunked.AdvanceUntil(new WorldMinute(720));

        Assert.Equal("70ebab666beb9e5e15ac228c23dbff6cdf27559fda28ca9d45b5d09abcbe123e", whole.HistoryFingerprint);
        Assert.Equal(whole.HistoryFingerprint, chunked.HistoryFingerprint);
        Assert.Equal(whole.SurvivalFingerprint, chunked.SurvivalFingerprint);
        Assert.Equal(whole.SettlementFingerprint, chunked.SettlementFingerprint);
        Assert.Equal(whole.SocialFingerprint, chunked.SocialFingerprint);
    }

    [Fact]
    public void M6AndM8ShortRunGameplayQueueRemainIdenticalOutsideHistory()
    {
        var m6 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M6SimulationRulesVersion);
        var m8 = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M8SimulationRulesVersion);
        m6.AdvanceUntil(new WorldMinute(720));
        m8.AdvanceUntil(new WorldMinute(720));
        var left = m6.CreatePersistenceSnapshot();
        var right = m8.CreatePersistenceSnapshot();

        Assert.Equal(left.World!.Fingerprint, right.World!.Fingerprint);
        Assert.Equal(left.Counters.NextEntityId, right.Counters.NextEntityId);
        Assert.Equal(left.Counters.NextScheduledEventSequence, right.Counters.NextScheduledEventSequence);
        Assert.Equal(left.ScheduledEvents, right.ScheduledEvents);
        Assert.Equal(left.ResourceStates, right.ResourceStates);
        Assert.Equal(left.Settlement, right.Settlement);
        Assert.Equal(left.Citizens, right.Citizens);
        Assert.Equal(left.Structures.Select(StructureKey), right.Structures.Select(StructureKey));
        Assert.Equal(left.StructureContributions.Select(ContributionKey), right.StructureContributions.Select(ContributionKey));
        Assert.Equal(left.Relationships, right.Relationships);
        Assert.Equal(left.Households, right.Households);
        Assert.NotEqual(left.SimulationRulesVersion, right.SimulationRulesVersion);
    }

    [Fact]
    public void M8SnapshotReloadPreservesRulesAndHistoryContinuation()
    {
        var source = new SimulationEngine(new WorldSeed(42), simulationRulesVersion: SimulationEngine.M8SimulationRulesVersion);
        source.AdvanceUntil(new WorldMinute(720));
        var restored = SimulationEngine.FromPersistenceSnapshot(source.CreatePersistenceSnapshot());

        Assert.Equal(SimulationEngine.M8SimulationRulesVersion, restored.SimulationRulesVersion);
        source.AdvanceUntil(new WorldMinute(1_440));
        restored.AdvanceUntil(new WorldMinute(1_440));
        Assert.Equal(source.HistoryFingerprint, restored.HistoryFingerprint);
    }

    [Fact]
    public void UnknownRulesRemainRejected()
    {
        var snapshot = new SimulationPersistenceSnapshot(new WorldSeed(42), WorldMinute.Zero, SimulationEngine.CurrentWorldSchemaVersion, "m9-rng1-unknown", "test", "{}", new DeterministicCountersSnapshot(1, 1, 1), []);
        Assert.Throws<NotSupportedException>(() => SimulationEngine.ValidatePersistenceSnapshotCompatibility(snapshot));
    }

    private static void InvokePrivate(SimulationEngine engine, string method, params object[] arguments) => typeof(SimulationEngine).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, arguments);

    private static object StructureKey(Structure item) => new { item.Id, item.Type, item.Status, item.Location, item.ConstructionStartedMinute, item.CompletedMinute, item.RequiredWood, item.DeliveredWood, item.RequiredStone, item.DeliveredStone, item.RequiredWork, item.CompletedWork };
    private static object ContributionKey(StructureContribution item) => new { item.StructureId, item.CitizenId, item.ConstructionWork, item.WoodDelivered, item.StoneDelivered };
}
