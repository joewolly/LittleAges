using System.Globalization;
using System.Text.Json;
using LittleAges.Domain;
using LittleAges.Simulation;
using Microsoft.EntityFrameworkCore;

namespace LittleAges.Persistence;

internal enum CheckpointFailurePoint
{
    AfterRowsWritten
}

/// <summary>Persists and restores the M0 canonical snapshot in one explicit transaction.</summary>
public sealed class WorldCheckpointStore
{
    private const int SingletonWorldId = 1;
    private readonly LittleAgesDbContext _context;

    internal WorldCheckpointStore(LittleAgesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>Uses UTC only for operational checkpoint metadata; it never enters the canonical snapshot.</summary>
    public Task CheckpointAsync(SimulationPersistenceSnapshot snapshot, CancellationToken cancellationToken = default) =>
        CheckpointAsync(snapshot, DateTime.UtcNow, cancellationToken);

    public async Task CheckpointAsync(
        SimulationPersistenceSnapshot snapshot,
        DateTime checkpointUtc,
        CancellationToken cancellationToken = default)
        => await CheckpointCoreAsync(snapshot, checkpointUtc, failurePoint: null, cancellationToken: cancellationToken);

    internal async Task CheckpointAsync(
        SimulationPersistenceSnapshot snapshot,
        DateTime checkpointUtc,
        CheckpointFailurePoint? failurePoint,
        CancellationToken cancellationToken = default)
        => await CheckpointCoreAsync(snapshot, checkpointUtc, failurePoint, cancellationToken);

    private async Task CheckpointCoreAsync(
        SimulationPersistenceSnapshot snapshot,
        DateTime checkpointUtc,
        CheckpointFailurePoint? failurePoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateWorldConfiguration(snapshot.WorldConfiguration);
        SimulationEngine.ValidatePersistenceSnapshotCompatibility(snapshot);
        if (checkpointUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Checkpoint metadata must be UTC.", nameof(checkpointUtc));
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _context.ChangeTracker.Clear();
            var existingMetadata = await _context.WorldMeta.AsNoTracking().ToListAsync(cancellationToken);
            if (existingMetadata.Count > 1)
            {
                throw new InvalidDataException("A checkpoint cannot replace a database with multiple world_meta rows.");
            }

            var createdUtc = existingMetadata.Count == 1 ? existingMetadata[0].CreatedUtc : checkpointUtc;
            _context.ScheduledEvents.RemoveRange(await _context.ScheduledEvents.ToListAsync(cancellationToken));
            _context.WorldMeta.RemoveRange(await _context.WorldMeta.ToListAsync(cancellationToken));
            await _context.SaveChangesAsync(cancellationToken);

            _context.WorldMeta.Add(ToWorldMetaRow(snapshot, createdUtc, checkpointUtc));
            _context.ScheduledEvents.AddRange(snapshot.ScheduledEvents.Select(ToScheduledEventRow));
            await _context.SaveChangesAsync(cancellationToken);

            if (failurePoint == CheckpointFailurePoint.AfterRowsWritten)
            {
                throw new InvalidOperationException("Controlled checkpoint failure requested by the test hook.");
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<SimulationPersistenceSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var metadataRows = await _context.WorldMeta.AsNoTracking().ToListAsync(cancellationToken);
        if (metadataRows.Count != 1 || metadataRows[0].Id != SingletonWorldId)
        {
            throw new InvalidDataException($"Expected exactly one world_meta row with id {SingletonWorldId}, found {metadataRows.Count}.");
        }

        var metadata = metadataRows[0];
        ValidateWorldConfiguration(metadata.WorldConfigurationJson);
        var seed = WorldSeedCodec.Decode(metadata.WorldSeedValue);
        var events = await _context.ScheduledEvents
            .AsNoTracking()
            .OrderBy(row => row.DueWorldMinute)
            .ThenBy(row => row.Priority)
            .ThenBy(row => row.EntitySortKey)
            .ThenBy(row => row.Sequence)
            .ToListAsync(cancellationToken);

        var scheduledEvents = events.Select(ToScheduledEventSnapshot).ToArray();
        var snapshot = new SimulationPersistenceSnapshot(
            seed,
            new WorldMinute(metadata.WorldMinute),
            metadata.WorldSchemaVersion,
            metadata.SimulationRulesVersion,
            metadata.ApplicationVersion,
            metadata.WorldConfigurationJson,
            new DeterministicCountersSnapshot(metadata.NextEntityId, metadata.NextHistoricalEventId, metadata.NextScheduledEventSequence),
            scheduledEvents);
        SimulationEngine.ValidatePersistenceSnapshotCompatibility(snapshot);
        return snapshot;
    }

    public async Task<bool> HasCheckpointAsync(CancellationToken cancellationToken = default)
    {
        var metadataCount = await _context.WorldMeta.AsNoTracking().CountAsync(cancellationToken);
        var scheduledEventCount = await _context.ScheduledEvents.AsNoTracking().CountAsync(cancellationToken);
        if (metadataCount == 0 && scheduledEventCount > 0)
        {
            throw new InvalidDataException("Scheduled events exist without a world_meta checkpoint.");
        }

        if (metadataCount > 1)
        {
            throw new InvalidDataException("More than one world_meta checkpoint exists.");
        }

        return metadataCount == 1;
    }

    private static WorldMetaRow ToWorldMetaRow(SimulationPersistenceSnapshot snapshot, DateTime createdUtc, DateTime checkpointUtc) => new()
    {
        Id = SingletonWorldId,
        WorldSeedValue = WorldSeedCodec.Encode(snapshot.Seed),
        WorldMinute = snapshot.WorldMinute.Value,
        WorldSchemaVersion = snapshot.WorldSchemaVersion,
        SimulationRulesVersion = snapshot.SimulationRulesVersion,
        ApplicationVersion = snapshot.ApplicationVersion,
        WorldConfigurationJson = snapshot.WorldConfiguration,
        NextEntityId = snapshot.Counters.NextEntityId,
        NextHistoricalEventId = snapshot.Counters.NextHistoricalEventId,
        NextScheduledEventSequence = snapshot.Counters.NextScheduledEventSequence,
        CreatedUtc = createdUtc,
        LastCheckpointUtc = checkpointUtc
    };

    private static ScheduledEventRow ToScheduledEventRow(ScheduledEventSnapshot scheduledEvent) => new()
    {
        Id = scheduledEvent.Id.Value,
        DueWorldMinute = scheduledEvent.Order.DueWorldMinute.Value,
        Priority = scheduledEvent.Order.Priority,
        EntitySortKey = scheduledEvent.Order.EntitySortKey,
        Sequence = scheduledEvent.Order.Sequence,
        EventName = scheduledEvent.Name,
        EventPayloadJson = "{}"
    };

    private static ScheduledEventSnapshot ToScheduledEventSnapshot(ScheduledEventRow row)
    {
        if (row.Id <= 0 || row.Sequence <= 0 || row.Id != row.Sequence)
        {
            throw new InvalidDataException("A scheduled event must have one positive identity equal to its sequence.");
        }

        if (string.IsNullOrWhiteSpace(row.EventName) || string.IsNullOrWhiteSpace(row.EventPayloadJson))
        {
            throw new InvalidDataException("A scheduled event must contain pure data name and payload values.");
        }

        return new ScheduledEventSnapshot(
            new ScheduledEventId(row.Id),
            new ScheduledEventOrder(new WorldMinute(row.DueWorldMinute), row.Priority, row.EntitySortKey, row.Sequence),
            row.EventName);
    }

    private static void ValidateWorldConfiguration(string configuration)
    {
        try
        {
            using var document = JsonDocument.Parse(configuration);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The persisted world configuration is not valid JSON.", exception);
        }
    }
}

/// <summary>Lossless UInt64-to-SQLite-text codec for world seeds.</summary>
public static class WorldSeedCodec
{
    public static string Encode(WorldSeed seed) => seed.Value.ToString(CultureInfo.InvariantCulture);

    public static WorldSeed Decode(string value)
    {
        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidDataException("The persisted world seed is not a lossless UInt64 decimal value.");
        }

        return new WorldSeed(parsed);
    }
}
