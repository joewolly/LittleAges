using LittleAges.Domain;
using LittleAges.Simulation;
using Xunit;

namespace LittleAges.Simulation.Tests;

public sealed class SimulationEngineTests
{
    private static readonly string[] RequiredTupleOrder = ["priority-first", "entity-first", "sequence-second", "sequence-first"];
    private static readonly string[] RestoredFutureOrder = ["sooner", "later"];

    [Fact]
    public void EventsUseTheRequiredStableTupleOrder()
    {
        var engine = new SimulationEngine(new WorldSeed(7), simulationRulesVersion: "m0-rng1");
        engine.ScheduleSyntheticEvent(new WorldMinute(10), 2, 10, "sequence-first");
        engine.ScheduleSyntheticEvent(new WorldMinute(10), 1, 99, "priority-first");
        engine.ScheduleSyntheticEvent(new WorldMinute(10), 2, 5, "entity-first");
        engine.ScheduleSyntheticEvent(new WorldMinute(10), 2, 5, "sequence-second");

        engine.AdvanceUntil(new WorldMinute(10));

        var processed = engine.CreateReadSnapshot().ProcessedEvents;
        Assert.Equal(RequiredTupleOrder, processed.Select(static item => item.Name));
        Assert.Equal(processed.Select(static item => item.Order.Sequence), processed.Select(static item => item.Id.Value));
    }

    [Fact]
    public void ClockAdvancesOnlyThroughEventProcessingOrExplicitTarget()
    {
        var engine = new SimulationEngine(new WorldSeed(1), simulationRulesVersion: "m0-rng1");
        Assert.False(engine.ProcessNextEvent());
        Assert.Equal(new WorldMinute(0), engine.CurrentMinute);

        engine.AdvanceUntil(new WorldMinute(100));
        Assert.Equal(new WorldMinute(100), engine.CurrentMinute);
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.AdvanceUntil(new WorldMinute(99)));
    }

    [Fact]
    public void SyntheticEventsArePureDataAndRestoreBehaviorEquivalently()
    {
        var engine = new SimulationEngine(new WorldSeed(1), simulationRulesVersion: "m0-rng1");
        engine.ScheduleSyntheticEvent(new WorldMinute(2), 0, 0, "first");
        engine.ScheduleSyntheticEvent(new WorldMinute(3), 0, 0, "second");
        var restored = SimulationEngine.FromPersistenceSnapshot(engine.CreatePersistenceSnapshot());

        Assert.Equal(2, engine.AdvanceUntil(new WorldMinute(3)));
        Assert.Equal(2, restored.AdvanceUntil(new WorldMinute(3)));
        Assert.Equal(engine.CreateReadSnapshot().ProcessedEvents, restored.CreateReadSnapshot().ProcessedEvents);
    }

    [Fact]
    public void PersistenceSnapshotPreservesFutureOrderAndCounters()
    {
        var engine = new SimulationEngine(new WorldSeed(123), simulationRulesVersion: "m0-rng1", worldConfiguration: "{\"test\":true}");
        engine.ScheduleSyntheticEvent(new WorldMinute(20), 2, 1, "later");
        engine.ScheduleSyntheticEvent(new WorldMinute(10), 1, 1, "sooner");
        engine.AdvanceUntil(new WorldMinute(5));

        var restored = SimulationEngine.FromPersistenceSnapshot(engine.CreatePersistenceSnapshot());
        restored.AdvanceUntil(new WorldMinute(20));

        Assert.Equal(new WorldSeed(123), restored.Seed);
        Assert.Equal(new WorldMinute(20), restored.CurrentMinute);
        Assert.Equal(RestoredFutureOrder, restored.CreateReadSnapshot().ProcessedEvents.Select(static item => item.Name));
        Assert.Equal(engine.CounterSnapshot.NextScheduledEventSequence, restored.CounterSnapshot.NextScheduledEventSequence);
    }

    [Fact]
    public void PersistenceSnapshotRejectsIdentityAndSequenceCollisions()
    {
        var duplicate = new[]
        {
            new ScheduledEventSnapshot(new ScheduledEventId(1), new ScheduledEventOrder(new WorldMinute(1), 0, 0, 1), "one"),
            new ScheduledEventSnapshot(new ScheduledEventId(1), new ScheduledEventOrder(new WorldMinute(2), 0, 0, 1), "duplicate")
        };
        Assert.Throws<ArgumentException>(() => new SimulationPersistenceSnapshot(
            new WorldSeed(1), new WorldMinute(0), "schema", "rules", "app", "{}",
            new DeterministicCountersSnapshot(1, 1, 3), duplicate));

        var mismatched = new[]
        {
            new ScheduledEventSnapshot(new ScheduledEventId(2), new ScheduledEventOrder(new WorldMinute(1), 0, 0, 1), "mismatch")
        };
        Assert.Throws<ArgumentException>(() => new SimulationPersistenceSnapshot(
            new WorldSeed(1), new WorldMinute(0), "schema", "rules", "app", "{}",
            new DeterministicCountersSnapshot(1, 1, 3), mismatched));

        var counterCollision = new[]
        {
            new ScheduledEventSnapshot(new ScheduledEventId(2), new ScheduledEventOrder(new WorldMinute(1), 0, 0, 2), "reused-sequence")
        };
        Assert.Throws<ArgumentException>(() => new SimulationPersistenceSnapshot(
            new WorldSeed(1), new WorldMinute(0), "schema", "rules", "app", "{}",
            new DeterministicCountersSnapshot(1, 1, 2), counterCollision));
    }
}
