using LittleAges.Domain;
using System.Text.Json;

namespace LittleAges.Simulation;

/// <summary>One synthetic scheduled item used by the M0 shell; gameplay event types start in later milestones.</summary>
public sealed record ScheduledEventSnapshot(
    ScheduledEventId Id,
    ScheduledEventOrder Order,
    string Name)
{
    public ScheduledEventSnapshot Validate()
    {
        WorldIdValidation.RequirePositive(Id.Value, nameof(Id));
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A scheduled event must have a stable name.", nameof(Name));
        }

        if (Order.Sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Order), "Scheduled event sequence must be positive.");
        }

        if (Id.Value != Order.Sequence)
        {
            throw new ArgumentException("The synthetic scheduled-event identity must equal its sequence.", nameof(Id));
        }

        return this;
    }
}

public sealed record SyntheticEventExecution(
    ScheduledEventId Id,
    ScheduledEventOrder Order,
    string Name);

/// <summary>Immutable observer-facing state produced by the single-writer engine.</summary>
public sealed record SimulationStatusSnapshot
{
    public SimulationStatusSnapshot(
        WorldSeed seed,
        WorldMinute worldMinute,
        int pendingEventCount,
        int processedEventCount,
        IReadOnlyList<SyntheticEventExecution> processedEvents,
        WorldMap? world = null)
    {
        ArgumentNullException.ThrowIfNull(processedEvents);
        Seed = seed;
        WorldMinute = worldMinute;
        PendingEventCount = pendingEventCount;
        ProcessedEventCount = processedEventCount;
        ProcessedEvents = Freeze(processedEvents);
        World = world;
    }

    public WorldSeed Seed { get; }
    public WorldMinute WorldMinute { get; }
    public int PendingEventCount { get; }
    public int ProcessedEventCount { get; }
    public IReadOnlyList<SyntheticEventExecution> ProcessedEvents { get; }
    public WorldMap? World { get; }

    private static System.Collections.ObjectModel.ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());
}

/// <summary>Immutable canonical data required to checkpoint and restore the M0 engine shell.</summary>
public sealed record SimulationPersistenceSnapshot
{
    public SimulationPersistenceSnapshot(
        WorldSeed seed,
        WorldMinute worldMinute,
        string worldSchemaVersion,
        string simulationRulesVersion,
        string applicationVersion,
        string worldConfiguration,
        DeterministicCountersSnapshot counters,
        IReadOnlyList<ScheduledEventSnapshot> scheduledEvents)
        : this(seed, worldMinute, worldSchemaVersion, simulationRulesVersion, applicationVersion, worldConfiguration, counters, scheduledEvents, null)
    {
    }

    public SimulationPersistenceSnapshot(
        WorldSeed seed,
        WorldMinute worldMinute,
        string worldSchemaVersion,
        string simulationRulesVersion,
        string applicationVersion,
        string worldConfiguration,
        DeterministicCountersSnapshot counters,
        IReadOnlyList<ScheduledEventSnapshot> scheduledEvents,
        WorldMap? world)
    {
        ArgumentNullException.ThrowIfNull(scheduledEvents);
        Seed = seed;
        WorldMinute = worldMinute;
        WorldSchemaVersion = RequireMetadata(worldSchemaVersion, nameof(worldSchemaVersion));
        SimulationRulesVersion = RequireMetadata(simulationRulesVersion, nameof(simulationRulesVersion));
        ApplicationVersion = RequireMetadata(applicationVersion, nameof(applicationVersion));
        WorldConfiguration = worldConfiguration ?? throw new ArgumentNullException(nameof(worldConfiguration));
        Counters = counters.Validate();
        var validatedEvents = scheduledEvents.Select(static item => item.Validate()).ToArray();
        var eventIds = new HashSet<long>();
        var eventSequences = new HashSet<long>();
        foreach (var scheduledEvent in validatedEvents)
        {
            if (!eventIds.Add(scheduledEvent.Id.Value) || !eventSequences.Add(scheduledEvent.Order.Sequence))
            {
                throw new ArgumentException("Queued scheduled-event IDs and sequences must be unique.", nameof(scheduledEvents));
            }
        }

        if (validatedEvents.Length > 0 && validatedEvents.Max(static item => item.Order.Sequence) >= Counters.NextScheduledEventSequence)
        {
            throw new ArgumentException("The next scheduled-event sequence must be greater than every queued event.", nameof(counters));
        }

        ScheduledEvents = Freeze(validatedEvents);
        World = world;
    }

    public WorldSeed Seed { get; }
    public WorldMinute WorldMinute { get; }
    public string WorldSchemaVersion { get; }
    public string SimulationRulesVersion { get; }
    public string ApplicationVersion { get; }
    public string WorldConfiguration { get; }
    public DeterministicCountersSnapshot Counters { get; }
    public IReadOnlyList<ScheduledEventSnapshot> ScheduledEvents { get; }
    /// <summary>Persisted generated world when available. Null is accepted for legacy M0 snapshots.</summary>
    public WorldMap? World { get; }

    private static string RequireMetadata(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Metadata must not be empty.", parameterName)
            : value;

    private static System.Collections.ObjectModel.ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());
}

/// <summary>
/// Minimal authoritative single-writer simulation shell. It contains no gameplay concepts.
/// Callers must use one logical execution path for all mutation methods.
/// </summary>
public sealed class SimulationEngine
{
    public const string CurrentWorldSchemaVersion = "0.1";
    public const string CurrentSimulationRulesVersion = "m0-rng1";

    private sealed record PendingEvent(
        ScheduledEventId Id,
        ScheduledEventOrder Order,
        string Name);

    private readonly SortedSet<PendingEvent> _scheduledEvents = new(
        Comparer<PendingEvent>.Create(static (left, right) => left.Order.CompareTo(right.Order)));
    private readonly List<SyntheticEventExecution> _processedEvents = [];
    private readonly DeterministicCounters _counters;

    public SimulationEngine(
        WorldSeed seed,
        WorldMinute initialMinute = default,
        string worldSchemaVersion = CurrentWorldSchemaVersion,
        string simulationRulesVersion = CurrentSimulationRulesVersion,
        string applicationVersion = "0.1.0",
        string worldConfiguration = "{}")
    {
        if (initialMinute.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialMinute), "World minute cannot be negative.");
        }

        Seed = seed;
        CurrentMinute = initialMinute;
        WorldSchemaVersion = RequireMetadata(worldSchemaVersion, nameof(worldSchemaVersion));
        SimulationRulesVersion = RequireMetadata(simulationRulesVersion, nameof(simulationRulesVersion));
        ApplicationVersion = RequireMetadata(applicationVersion, nameof(applicationVersion));
        WorldConfiguration = worldConfiguration ?? throw new ArgumentNullException(nameof(worldConfiguration));
        _counters = new DeterministicCounters();
        World = CreateWorld(seed, worldConfiguration);
    }

    public SimulationEngine(SimulationPersistenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidatePersistenceSnapshotCompatibility(snapshot);
        Seed = snapshot.Seed;
        CurrentMinute = snapshot.WorldMinute;
        WorldSchemaVersion = snapshot.WorldSchemaVersion;
        SimulationRulesVersion = snapshot.SimulationRulesVersion;
        ApplicationVersion = snapshot.ApplicationVersion;
        WorldConfiguration = snapshot.WorldConfiguration;
        _counters = new DeterministicCounters(snapshot.Counters);
        World = snapshot.World ?? CreateWorld(snapshot.Seed, snapshot.WorldConfiguration);

        foreach (var scheduledEvent in snapshot.ScheduledEvents)
        {
            if (scheduledEvent.Order.DueWorldMinute < CurrentMinute)
            {
                throw new ArgumentException("A restored scheduled event cannot be due in the past.", nameof(snapshot));
            }

            AddPending(scheduledEvent.Id, scheduledEvent.Order, scheduledEvent.Name);
        }
    }

    public WorldSeed Seed { get; }
    public WorldMinute CurrentMinute { get; private set; }
    public string WorldSchemaVersion { get; }
    public string SimulationRulesVersion { get; }
    public string ApplicationVersion { get; }
    public string WorldConfiguration { get; }
    public WorldMap World { get; }
    public int PendingEventCount => _scheduledEvents.Count;
    public int ProcessedEventCount => _processedEvents.Count;
    public DeterministicCountersSnapshot CounterSnapshot => _counters.Snapshot;

    public ScheduledEventId ScheduleSyntheticEvent(
        WorldMinute dueWorldMinute,
        int priority,
        long entitySortKey,
        string name)
    {
        if (dueWorldMinute < CurrentMinute)
        {
            throw new ArgumentOutOfRangeException(nameof(dueWorldMinute), dueWorldMinute, "Events cannot be scheduled in the past.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A scheduled event must have a stable name.", nameof(name));
        }

        var sequence = _counters.AllocateScheduledEventSequence();
        var id = new ScheduledEventId(sequence);
        AddPending(id, new ScheduledEventOrder(dueWorldMinute, priority, entitySortKey, sequence), name);
        return id;
    }

    public ScheduledEventId Schedule(
        WorldMinute dueWorldMinute,
        int priority,
        long entitySortKey,
        string name) =>
        ScheduleSyntheticEvent(dueWorldMinute, priority, entitySortKey, name);

    public bool ProcessNextEvent()
    {
        if (_scheduledEvents.Count == 0)
        {
            return false;
        }

        var next = _scheduledEvents.Min!;
        _scheduledEvents.Remove(next);
        CurrentMinute = CurrentMinute.AdvanceTo(next.Order.DueWorldMinute);
        _processedEvents.Add(new SyntheticEventExecution(next.Id, next.Order, next.Name));
        return true;
    }

    public int AdvanceEvents(int maximumEvents)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumEvents);

        var processed = 0;
        while (processed < maximumEvents && ProcessNextEvent())
        {
            processed++;
        }

        return processed;
    }

    public int AdvanceUntil(WorldMinute targetMinute)
    {
        if (targetMinute < CurrentMinute)
        {
            throw new ArgumentOutOfRangeException(nameof(targetMinute), targetMinute, "Canonical time may not move backward.");
        }

        var processed = 0;
        while (_scheduledEvents.Count > 0 && _scheduledEvents.Min!.Order.DueWorldMinute <= targetMinute)
        {
            ProcessNextEvent();
            processed++;
        }

        CurrentMinute = CurrentMinute.AdvanceTo(targetMinute);
        return processed;
    }

    public SimulationStatusSnapshot CreateReadSnapshot() =>
        new(Seed, CurrentMinute, PendingEventCount, ProcessedEventCount, _processedEvents, World);

    public SimulationStatusSnapshot CreateStatusSnapshot() => CreateReadSnapshot();

    public SimulationPersistenceSnapshot CreatePersistenceSnapshot()
    {
        var events = _scheduledEvents
            .Select(static item => new ScheduledEventSnapshot(item.Id, item.Order, item.Name))
            .ToArray();
        return new SimulationPersistenceSnapshot(
            Seed,
            CurrentMinute,
            WorldSchemaVersion,
            SimulationRulesVersion,
            ApplicationVersion,
            WorldConfiguration,
            _counters.Snapshot,
            events,
            World);
    }

    public static SimulationEngine FromPersistenceSnapshot(SimulationPersistenceSnapshot snapshot) => new(snapshot);

    public static void ValidatePersistenceSnapshotCompatibility(SimulationPersistenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.Equals(snapshot.WorldSchemaVersion, CurrentWorldSchemaVersion, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"World schema version '{snapshot.WorldSchemaVersion}' is not supported; expected '{CurrentWorldSchemaVersion}'.");
        }

        if (!string.Equals(snapshot.SimulationRulesVersion, CurrentSimulationRulesVersion, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Simulation rules version '{snapshot.SimulationRulesVersion}' is not supported; expected '{CurrentSimulationRulesVersion}'.");
        }
    }

    private void AddPending(ScheduledEventId id, ScheduledEventOrder order, string name)
    {
        if (id.Value != order.Sequence)
        {
            throw new ArgumentException("The synthetic scheduled-event identity must equal its sequence.", nameof(id));
        }

        if (!_scheduledEvents.Add(new PendingEvent(id, order, name)))
        {
            throw new ArgumentException("A scheduled event with the same deterministic ordering tuple already exists.", nameof(order));
        }
    }

    private static string RequireMetadata(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Metadata must not be empty.", parameterName)
            : value;

    private static WorldMap CreateWorld(WorldSeed seed, string configuration)
    {
        var parsed = string.IsNullOrWhiteSpace(configuration) || configuration == "{}"
            ? WorldGenerationConfiguration.Default
            : TryParseConfiguration(configuration);
        return new WorldGenerator().Generate(seed, parsed);
    }

    private static WorldGenerationConfiguration TryParseConfiguration(string configuration)
    {
        try { return WorldGenerationConfiguration.FromCanonicalJson(configuration); }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException or NotSupportedException)
        {
            // M0 callers used arbitrary JSON metadata blobs. Preserve that narrow legacy
            // shape, but never hide an attempted versioned M1 configuration error.
            if (configuration.Contains("\"version\"", StringComparison.Ordinal)) throw;
            return WorldGenerationConfiguration.Default;
        }
    }
}
