namespace LittleAges.Domain;

/// <summary>A persisted, monotonic positive Int64 counter. It has no process or wall-clock state.</summary>
public sealed class MonotonicCounter
{
    public MonotonicCounter(long nextValue = 1)
    {
        NextValue = WorldIdValidation.RequirePositive(nextValue, nameof(nextValue));
    }

    public long NextValue { get; private set; }

    public long Allocate()
    {
        // Reject before changing NextValue. A failed allocation is observationally a no-op.
        if (NextValue == long.MaxValue)
        {
            throw new InvalidOperationException("The deterministic Int64 counter is exhausted.");
        }

        var allocated = NextValue;
        NextValue = checked(NextValue + 1);
        return allocated;
    }
}

public readonly record struct DeterministicCountersSnapshot(
    long NextEntityId,
    long NextHistoricalEventId,
    long NextScheduledEventSequence)
{
    public DeterministicCountersSnapshot Validate()
    {
        WorldIdValidation.RequirePositive(NextEntityId, nameof(NextEntityId));
        WorldIdValidation.RequirePositive(NextHistoricalEventId, nameof(NextHistoricalEventId));
        WorldIdValidation.RequirePositive(NextScheduledEventSequence, nameof(NextScheduledEventSequence));
        return this;
    }
}

/// <summary>
/// The world-local deterministic counters that are part of a persistence snapshot.
/// Separate streams prevent adding a historical event from renumbering scheduled events.
/// </summary>
public sealed class DeterministicCounters
{
    private readonly MonotonicCounter _entityIds;
    private readonly MonotonicCounter _historicalEventIds;
    private readonly MonotonicCounter _scheduledEventSequences;

    public DeterministicCounters(DeterministicCountersSnapshot snapshot)
    {
        snapshot.Validate();
        _entityIds = new MonotonicCounter(snapshot.NextEntityId);
        _historicalEventIds = new MonotonicCounter(snapshot.NextHistoricalEventId);
        _scheduledEventSequences = new MonotonicCounter(snapshot.NextScheduledEventSequence);
    }

    public DeterministicCounters() : this(new DeterministicCountersSnapshot(1, 1, 1)) { }

    public DeterministicCountersSnapshot Snapshot => new(
        _entityIds.NextValue,
        _historicalEventIds.NextValue,
        _scheduledEventSequences.NextValue);

    public CitizenId AllocateCitizenId() => new(AllocateEntityId());
    public StructureId AllocateStructureId() => new(AllocateEntityId());
    public HouseholdId AllocateHouseholdId() => new(AllocateEntityId());
    public HistoricalEventId AllocateHistoricalEventId() => new(AllocateHistoricalEventIdValue());
    public long AllocateScheduledEventSequence() => _scheduledEventSequences.Allocate();

    private long AllocateEntityId() => _entityIds.Allocate();
    private long AllocateHistoricalEventIdValue() => _historicalEventIds.Allocate();
}
