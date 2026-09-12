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

internal enum LegacyUpgradeFailurePoint
{
    AfterRowsWritten
}

/// <summary>Persists and restores the complete M1 canonical snapshot in one explicit transaction.</summary>
public sealed class WorldCheckpointStore
{
    private const int SingletonWorldId = 1;
    private readonly LittleAgesDbContext _context;

    internal WorldCheckpointStore(LittleAgesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task CheckpointAsync(SimulationPersistenceSnapshot snapshot, CancellationToken cancellationToken = default) =>
        CheckpointAsync(snapshot, DateTime.UtcNow, cancellationToken);

    public Task CheckpointAsync(SimulationPersistenceSnapshot snapshot, DateTime checkpointUtc, CancellationToken cancellationToken = default) =>
        CheckpointCoreAsync(snapshot, checkpointUtc, failurePoint: null, cancellationToken);

    internal Task CheckpointAsync(SimulationPersistenceSnapshot snapshot, DateTime checkpointUtc, CheckpointFailurePoint? failurePoint, CancellationToken cancellationToken = default) =>
        CheckpointCoreAsync(snapshot, checkpointUtc, failurePoint, cancellationToken);

    internal async Task<bool> UpgradeLegacyM0IfNeededAsync(
        DateTime? checkpointUtc = null,
        LegacyUpgradeFailurePoint? failurePoint = null,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var metadataRows = await _context.WorldMeta.AsNoTracking().ToListAsync(cancellationToken);
            var tileCount = await _context.WorldTiles.AsNoTracking().CountAsync(cancellationToken);
            var resourceCount = await _context.ResourceNodes.AsNoTracking().CountAsync(cancellationToken);
            var eventCount = await _context.ScheduledEvents.AsNoTracking().CountAsync(cancellationToken);

            if (metadataRows.Count == 0)
            {
                if (tileCount != 0 || resourceCount != 0 || eventCount != 0)
                {
                    throw new InvalidDataException("Canonical rows exist without a world_meta row.");
                }

                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            if (metadataRows.Count != 1 || metadataRows[0].Id != SingletonWorldId)
            {
                throw new InvalidDataException("Expected exactly one legacy world_meta row with id 1.");
            }

            var metadata = metadataRows[0];
            if (metadata.GenerationVersion != 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            if (metadata.GenerationAttempt != 0 || metadata.StartingX != 0 || metadata.StartingY != 0 || metadata.WorldFingerprint.Length != 0 || tileCount != 0 || resourceCount != 0)
            {
                throw new InvalidDataException("The M0-to-M1 upgrade sentinel is partial or contains canonical world rows.");
            }

            var legacySnapshot = await ReadLegacyM0SnapshotAsync(metadata, cancellationToken);
            RejectVersionedM1ConfigurationFromLegacy(legacySnapshot.WorldConfiguration);
            var world = new WorldGenerator().Generate(legacySnapshot.Seed, WorldGenerationConfiguration.Default);
            var snapshot = new SimulationPersistenceSnapshot(
                legacySnapshot.Seed,
                legacySnapshot.WorldMinute,
                legacySnapshot.WorldSchemaVersion,
                legacySnapshot.SimulationRulesVersion,
                legacySnapshot.ApplicationVersion,
                world.Configuration.CanonicalJson,
                legacySnapshot.Counters,
                legacySnapshot.ScheduledEvents,
                world);
            var upgradeUtc = checkpointUtc ?? DateTime.UtcNow;
            if (upgradeUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Legacy upgrade metadata must be UTC.", nameof(checkpointUtc));
            await WriteSnapshotRowsAsync(snapshot, world, metadata.CreatedUtc, upgradeUtc, cancellationToken);

            if (failurePoint == LegacyUpgradeFailurePoint.AfterRowsWritten)
            {
                throw new InvalidOperationException("Controlled legacy upgrade failure requested by the test hook.");
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task CheckpointCoreAsync(SimulationPersistenceSnapshot snapshot, DateTime checkpointUtc, CheckpointFailurePoint? failurePoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var world = ValidateSnapshot(snapshot);
        if (checkpointUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Checkpoint metadata must be UTC.", nameof(checkpointUtc));

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _context.ChangeTracker.Clear();
            var existingMetadata = await _context.WorldMeta.AsNoTracking().ToListAsync(cancellationToken);
            if (existingMetadata.Count > 1) throw new InvalidDataException("A checkpoint cannot replace a database with multiple world_meta rows.");
            var createdUtc = existingMetadata.Count == 1 ? existingMetadata[0].CreatedUtc : checkpointUtc;

            await WriteSnapshotRowsAsync(snapshot, world, createdUtc, checkpointUtc, cancellationToken);

            if (failurePoint == CheckpointFailurePoint.AfterRowsWritten) throw new InvalidOperationException("Controlled checkpoint failure requested by the test hook.");
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task WriteSnapshotRowsAsync(
        SimulationPersistenceSnapshot snapshot,
        WorldMap world,
        DateTime createdUtc,
        DateTime checkpointUtc,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        _context.ResourceNodes.RemoveRange(await _context.ResourceNodes.ToListAsync(cancellationToken));
        _context.WorldTiles.RemoveRange(await _context.WorldTiles.ToListAsync(cancellationToken));
        _context.ScheduledEvents.RemoveRange(await _context.ScheduledEvents.ToListAsync(cancellationToken));
        _context.WorldMeta.RemoveRange(await _context.WorldMeta.ToListAsync(cancellationToken));
        await _context.SaveChangesAsync(cancellationToken);

        var detectChanges = _context.ChangeTracker.AutoDetectChangesEnabled;
        _context.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            _context.WorldMeta.Add(ToWorldMetaRow(snapshot, world, createdUtc, checkpointUtc));
            _context.ScheduledEvents.AddRange(snapshot.ScheduledEvents.Select(ToScheduledEventRow));
            _context.WorldTiles.AddRange(world.Tiles.Select(tile => ToWorldTileRow(tile, world.Width)).ToArray());
            _context.ResourceNodes.AddRange(world.Resources.Select(node => ToResourceNodeRow(node, world.Width)).ToArray());
            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _context.ChangeTracker.AutoDetectChangesEnabled = detectChanges;
        }
    }

    public async Task<SimulationPersistenceSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var metadataRows = await _context.WorldMeta.AsNoTracking().ToListAsync(cancellationToken);
        if (metadataRows.Count != 1 || metadataRows[0].Id != SingletonWorldId) throw new InvalidDataException($"Expected exactly one world_meta row with id {SingletonWorldId}, found {metadataRows.Count}.");
        var metadata = metadataRows[0];
        var configuration = ParseConfiguration(metadata.WorldConfigurationJson);
        if (metadata.GenerationVersion != WorldGenerationConfiguration.CurrentVersion) throw new NotSupportedException($"World generation version '{metadata.GenerationVersion}' is not supported.");
        var seed = WorldSeedCodec.Decode(metadata.WorldSeedValue);
        if (metadata.GenerationAttempt < 0 || metadata.GenerationAttempt >= configuration.MaximumAttempts) throw new InvalidDataException("The persisted generation attempt is outside the configured attempt range.");

        var tileRows = await _context.WorldTiles.AsNoTracking().OrderBy(row => row.TileIndex).ToListAsync(cancellationToken);
        var expectedTileCount = checked(configuration.Width * configuration.Height);
        if (tileRows.Count != expectedTileCount) throw new InvalidDataException($"Expected {expectedTileCount} persisted world tiles, found {tileRows.Count}.");
        var tiles = new WorldTile[tileRows.Count];
        var coordinates = new HashSet<TileCoordinate>();
        for (var expectedIndex = 0; expectedIndex < tileRows.Count; expectedIndex++)
        {
            var row = tileRows[expectedIndex];
            if (row.TileIndex != expectedIndex) throw new InvalidDataException("Persisted world tile indexes must be complete row-major indexes.");
            if (row.X < 0 || row.X >= configuration.Width || row.Y < 0 || row.Y >= configuration.Height) throw new InvalidDataException("Persisted world tile coordinates are outside the configured world.");
            var expectedCoordinate = TileCoordinate.FromIndex(row.TileIndex, configuration.Width);
            var coordinate = new TileCoordinate(row.X, row.Y);
            if (coordinate != expectedCoordinate || !coordinates.Add(coordinate)) throw new InvalidDataException("Persisted world tile coordinates are not unique row-major coordinates.");
            if (!Enum.IsDefined((TerrainType)row.Terrain)) throw new InvalidDataException($"Persisted world tile {row.TileIndex} has an unknown terrain value.");
            try { tiles[expectedIndex] = new WorldTile(coordinate, (TerrainType)row.Terrain, row.Elevation, row.Fertility, row.WaterAccess, row.Walkable, row.MovementCost); }
            catch (ArgumentException exception) { throw new InvalidDataException($"Persisted world tile {row.TileIndex} has invalid values.", exception); }
        }

        var resourceRows = await _context.ResourceNodes.AsNoTracking().OrderBy(row => row.Id).ToListAsync(cancellationToken);
        var resources = new ResourceNode[resourceRows.Count];
        var resourceIds = new HashSet<long>();
        foreach (var (row, index) in resourceRows.Select((row, index) => (row, index)))
        {
            if (row.Id <= 0 || !resourceIds.Add(row.Id)) throw new InvalidDataException("Persisted resource node IDs must be unique positive values.");
            if (row.TileIndex < 0 || row.TileIndex >= tileRows.Count || row.X < 0 || row.X >= configuration.Width || row.Y < 0 || row.Y >= configuration.Height) throw new InvalidDataException($"Persisted resource node {row.Id} references an invalid tile.");
            var tileCoordinate = TileCoordinate.FromIndex(row.TileIndex, configuration.Width);
            if (new TileCoordinate(row.X, row.Y) != tileCoordinate) throw new InvalidDataException($"Persisted resource node {row.Id} has a mismatched tile coordinate.");
            if (!Enum.IsDefined((ResourceType)row.Resource)) throw new InvalidDataException($"Persisted resource node {row.Id} has an unknown resource value.");
            try { resources[index] = new ResourceNode(new ResourceNodeId(row.Id), tileCoordinate, (ResourceType)row.Resource, row.InitialQuantity, row.MaximumQuantity, row.RegenerationPotential); }
            catch (ArgumentException exception) { throw new InvalidDataException($"Persisted resource node {row.Id} has invalid values.", exception); }
        }

        if (metadata.StartingX < 0 || metadata.StartingX >= configuration.Width || metadata.StartingY < 0 || metadata.StartingY >= configuration.Height) throw new InvalidDataException("Persisted starting site is outside the configured world.");
        WorldMap world;
        try { world = new WorldMap(seed, metadata.GenerationVersion, metadata.GenerationAttempt, configuration, tiles, resources, new TileCoordinate(metadata.StartingX, metadata.StartingY)); }
        catch (ArgumentException exception) { throw new InvalidDataException("Persisted world data is not a valid world map.", exception); }
        if (string.IsNullOrWhiteSpace(metadata.WorldFingerprint) || !string.Equals(metadata.WorldFingerprint, world.Fingerprint, StringComparison.Ordinal)) throw new InvalidDataException("Persisted world fingerprint does not match the canonical world rows.");

        var events = await _context.ScheduledEvents.AsNoTracking().OrderBy(row => row.DueWorldMinute).ThenBy(row => row.Priority).ThenBy(row => row.EntitySortKey).ThenBy(row => row.Sequence).ToListAsync(cancellationToken);
        var snapshot = new SimulationPersistenceSnapshot(seed, new WorldMinute(metadata.WorldMinute), metadata.WorldSchemaVersion, metadata.SimulationRulesVersion, metadata.ApplicationVersion, configuration.CanonicalJson, new DeterministicCountersSnapshot(metadata.NextEntityId, metadata.NextHistoricalEventId, metadata.NextScheduledEventSequence), events.Select(ToScheduledEventSnapshot).ToArray(), world);
        SimulationEngine.ValidatePersistenceSnapshotCompatibility(snapshot);
        return snapshot;
    }

    public async Task<bool> HasCheckpointAsync(CancellationToken cancellationToken = default)
    {
        var metadataCount = await _context.WorldMeta.AsNoTracking().CountAsync(cancellationToken);
        var scheduledEventCount = await _context.ScheduledEvents.AsNoTracking().CountAsync(cancellationToken);
        var tileCount = await _context.WorldTiles.AsNoTracking().CountAsync(cancellationToken);
        var resourceCount = await _context.ResourceNodes.AsNoTracking().CountAsync(cancellationToken);
        if (metadataCount == 0 && (scheduledEventCount > 0 || tileCount > 0 || resourceCount > 0)) throw new InvalidDataException("Checkpoint rows exist without a world_meta checkpoint.");
        if (metadataCount > 1) throw new InvalidDataException("More than one world_meta checkpoint exists.");
        return metadataCount == 1;
    }

    private static WorldMap ValidateSnapshot(SimulationPersistenceSnapshot snapshot)
    {
        SimulationEngine.ValidatePersistenceSnapshotCompatibility(snapshot);
        var world = snapshot.World ?? throw new InvalidDataException("An M1 checkpoint requires a persisted WorldMap.");
        if (world.OriginalSeed != snapshot.Seed) throw new InvalidDataException("The world seed does not match the snapshot seed.");
        var configuration = ParseConfiguration(snapshot.WorldConfiguration);
        if (!string.Equals(configuration.CanonicalJson, world.Configuration.CanonicalJson, StringComparison.Ordinal)) throw new InvalidDataException("The world configuration does not match the persisted WorldMap configuration.");
        if (world.GenerationVersion != configuration.Version) throw new InvalidDataException("The world generation version does not match its configuration.");
        return world;
    }

    private static WorldGenerationConfiguration ParseConfiguration(string configuration)
    {
        try
        {
            var parsed = WorldGenerationConfiguration.FromCanonicalJson(configuration);
            if (!string.Equals(parsed.CanonicalJson, configuration, StringComparison.Ordinal)) throw new InvalidDataException("The persisted world configuration is not canonical JSON.");
            return parsed;
        }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or NotSupportedException) { throw new InvalidDataException("The persisted world configuration is not a valid canonical M1 configuration.", exception); }
    }

    private static void RejectVersionedM1ConfigurationFromLegacy(string configuration)
    {
        var looksVersioned = false;
        try
        {
            using var document = JsonDocument.Parse(configuration);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("version", out var version))
            {
                looksVersioned = version.ValueKind == JsonValueKind.Number;
            }
        }
        catch (JsonException)
        {
            return;
        }

        try
        {
            _ = WorldGenerationConfiguration.FromCanonicalJson(configuration);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or NotSupportedException)
        {
            if (looksVersioned)
            {
                throw new InvalidDataException("The legacy M0 checkpoint contains a versioned M1 world configuration but no persisted world rows.", exception);
            }

            return;
        }

        throw new InvalidDataException("The legacy M0 checkpoint contains a valid M1 world configuration but no persisted world rows.");
    }

    private async Task<SimulationPersistenceSnapshot> ReadLegacyM0SnapshotAsync(WorldMetaRow metadata, CancellationToken cancellationToken)
    {
        var seed = WorldSeedCodec.Decode(metadata.WorldSeedValue);
        WorldMinute worldMinute;
        try
        {
            worldMinute = new WorldMinute(metadata.WorldMinute);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The legacy world minute must be non-negative.", exception);
        }

        if (string.IsNullOrWhiteSpace(metadata.WorldSchemaVersion) || string.IsNullOrWhiteSpace(metadata.SimulationRulesVersion) || string.IsNullOrWhiteSpace(metadata.ApplicationVersion))
        {
            throw new InvalidDataException("Legacy checkpoint metadata versions and application version must be non-empty.");
        }

        try
        {
            using var configuration = JsonDocument.Parse(metadata.WorldConfigurationJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The legacy world configuration is not valid JSON.", exception);
        }

        var events = await _context.ScheduledEvents.AsNoTracking()
            .OrderBy(row => row.DueWorldMinute)
            .ThenBy(row => row.Priority)
            .ThenBy(row => row.EntitySortKey)
            .ThenBy(row => row.Sequence)
            .ToListAsync(cancellationToken);
        var scheduledEvents = events.Select(ToScheduledEventSnapshot).ToArray();
        if (scheduledEvents.Any(item => item.Order.DueWorldMinute < worldMinute))
        {
            throw new InvalidDataException("A legacy scheduled event cannot be due before the persisted world minute.");
        }

        SimulationPersistenceSnapshot snapshot;
        try
        {
            snapshot = new SimulationPersistenceSnapshot(
                seed,
                worldMinute,
                metadata.WorldSchemaVersion,
                metadata.SimulationRulesVersion,
                metadata.ApplicationVersion,
                metadata.WorldConfigurationJson,
                new DeterministicCountersSnapshot(metadata.NextEntityId, metadata.NextHistoricalEventId, metadata.NextScheduledEventSequence),
                scheduledEvents);
            SimulationEngine.ValidatePersistenceSnapshotCompatibility(snapshot);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The legacy M0 checkpoint is not a valid persistence snapshot.", exception);
        }
        catch (NotSupportedException exception)
        {
            throw new InvalidDataException("The legacy M0 checkpoint has unsupported compatibility metadata.", exception);
        }

        return snapshot;
    }

    private static WorldMetaRow ToWorldMetaRow(SimulationPersistenceSnapshot snapshot, WorldMap world, DateTime createdUtc, DateTime checkpointUtc) => new()
    {
        Id = SingletonWorldId, WorldSeedValue = WorldSeedCodec.Encode(snapshot.Seed), WorldMinute = snapshot.WorldMinute.Value,
        WorldSchemaVersion = snapshot.WorldSchemaVersion, SimulationRulesVersion = snapshot.SimulationRulesVersion, ApplicationVersion = snapshot.ApplicationVersion,
        WorldConfigurationJson = world.Configuration.CanonicalJson, GenerationVersion = world.GenerationVersion, GenerationAttempt = world.GenerationAttempt,
        StartingX = world.StartingSite.X, StartingY = world.StartingSite.Y, WorldFingerprint = world.Fingerprint,
        NextEntityId = snapshot.Counters.NextEntityId, NextHistoricalEventId = snapshot.Counters.NextHistoricalEventId, NextScheduledEventSequence = snapshot.Counters.NextScheduledEventSequence,
        CreatedUtc = createdUtc, LastCheckpointUtc = checkpointUtc
    };

    private static WorldTileRow ToWorldTileRow(WorldTile tile, int width) => new()
    {
        TileIndex = tile.Coordinate.ToIndex(width), X = tile.Coordinate.X, Y = tile.Coordinate.Y, Terrain = (int)tile.Terrain,
        Elevation = tile.Elevation, Fertility = tile.Fertility, WaterAccess = tile.WaterAccess, Walkable = tile.Walkable, MovementCost = tile.MovementCost
    };

    private static ResourceNodeRow ToResourceNodeRow(ResourceNode node, int width) => new()
    {
        Id = node.Id.Value, TileIndex = node.Coordinate.ToIndex(width), X = node.Coordinate.X, Y = node.Coordinate.Y, Resource = (int)node.Type,
        InitialQuantity = node.InitialQuantity, MaximumQuantity = node.MaximumQuantity, RegenerationPotential = node.RegenerationPotential
    };

    private static ScheduledEventRow ToScheduledEventRow(ScheduledEventSnapshot scheduledEvent) => new()
    {
        Id = scheduledEvent.Id.Value, DueWorldMinute = scheduledEvent.Order.DueWorldMinute.Value, Priority = scheduledEvent.Order.Priority,
        EntitySortKey = scheduledEvent.Order.EntitySortKey, Sequence = scheduledEvent.Order.Sequence, EventName = scheduledEvent.Name, EventPayloadJson = "{}"
    };

    private static ScheduledEventSnapshot ToScheduledEventSnapshot(ScheduledEventRow row)
    {
        if (row.Id <= 0 || row.Sequence <= 0 || row.Id != row.Sequence) throw new InvalidDataException("A scheduled event must have one positive identity equal to its sequence.");
        if (string.IsNullOrWhiteSpace(row.EventName) || string.IsNullOrWhiteSpace(row.EventPayloadJson)) throw new InvalidDataException("A scheduled event must contain pure data name and payload values.");
        try { using var payload = JsonDocument.Parse(row.EventPayloadJson); }
        catch (JsonException exception) { throw new InvalidDataException("A scheduled event payload must be valid JSON.", exception); }
        return new ScheduledEventSnapshot(new ScheduledEventId(row.Id), new ScheduledEventOrder(new WorldMinute(row.DueWorldMinute), row.Priority, row.EntitySortKey, row.Sequence), row.EventName);
    }
}

/// <summary>Lossless UInt64-to-SQLite-text codec for world seeds.</summary>
public static class WorldSeedCodec
{
    public static string Encode(WorldSeed seed) => seed.Value.ToString(CultureInfo.InvariantCulture);

    public static WorldSeed Decode(string value)
    {
        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) throw new InvalidDataException("The persisted world seed is not a lossless UInt64 decimal value.");
        return new WorldSeed(parsed);
    }
}
