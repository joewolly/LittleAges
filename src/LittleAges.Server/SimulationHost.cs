using System.Threading.Channels;
using System.Globalization;
using System.Collections.ObjectModel;
using LittleAges.Domain;
using LittleAges.Persistence;
using LittleAges.Simulation;
using Microsoft.Extensions.Hosting;

namespace LittleAges.Server;

public enum SimulationHostState
{
    Starting,
    Running,
    Stopping,
    Faulted
}

public sealed record ServerStatusSnapshot(
    SimulationHostState State,
    long WorldMinute,
    int PendingEventCount,
    string WorldSeed,
    string? Error,
    WorldSummarySnapshot? World = null);

public sealed record WorldStartingSiteSnapshot(int X, int Y);

public sealed record WorldSummarySnapshot(
    string WorldSeed,
    int Width,
    int Height,
    int TileCount,
    int GenerationVersion,
    int GenerationAttempt,
    WorldStartingSiteSnapshot StartingSite,
    IReadOnlyDictionary<string, int> TerrainCounts,
    IReadOnlyDictionary<string, int> ResourceCounts,
    string Fingerprint);

public sealed record CheckpointCommandResult(bool Succeeded, long WorldMinute);

internal abstract record SimulationCommand
{
    internal abstract void SetException(Exception exception);
}

internal sealed record CheckpointSimulationCommand(TaskCompletionSource<CheckpointCommandResult> Completion) : SimulationCommand
{
    internal override void SetException(Exception exception) => Completion.TrySetException(exception);
}

internal sealed record FailSimulationCommand(TaskCompletionSource<bool> Completion, Exception Failure) : SimulationCommand
{
    internal override void SetException(Exception exception) => Completion.TrySetException(exception);
}

/// <summary>
/// The sole hosted simulation writer. HTTP code reads only the immutable Status value and
/// submits commands through the bounded channel; it never touches the engine or database.
/// </summary>
public sealed partial class SimulationHost : BackgroundService
{
    private readonly ServerOptions _options;
    private readonly ILogger<SimulationHost> _logger;
    private readonly Channel<SimulationCommand> _commands = Channel.CreateBounded<SimulationCommand>(
        new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    private ServerStatusSnapshot _status;
    private SimulationEngine? _engine;
    private WorldDatabase? _database;
    private WorldCheckpointStore? _checkpointStore;
    private int _failNextFinalCheckpointForTesting;
    private Exception? _terminalFailure;

    public SimulationHost(ServerOptions options, ILogger<SimulationHost> logger)
    {
        _options = options;
        _logger = logger;
        _status = new ServerStatusSnapshot(SimulationHostState.Starting, 0, 0, FormatWorldSeed(options.WorldSeed.Value), null);
    }

    public ServerStatusSnapshot Status => Volatile.Read(ref _status);
    public int CommandCapacity => 32;

    public async Task<CheckpointCommandResult> RequestCheckpointAsync(CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<CheckpointCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new CheckpointSimulationCommand(completion);
        try
        {
            await _commands.Writer.WriteAsync(command, cancellationToken);
            return await completion.Task.WaitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
            throw;
        }
    }

    internal async Task TriggerCommandLoopFailureForTestingAsync()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _commands.Writer.WriteAsync(new FailSimulationCommand(
            completion,
            new InvalidOperationException("Controlled simulation command failure.")));
        await completion.Task;
    }

    internal void FailNextFinalCheckpointForTesting() => Interlocked.Exchange(ref _failNextFinalCheckpointForTesting, 1);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        var terminalFailure = Volatile.Read(ref _terminalFailure);
        if (terminalFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(terminalFailure).Throw();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Exception? terminalFailure = null;
        try
        {
            await OpenOrCreateWorldAsync(stoppingToken);
            Publish(SimulationHostState.Running);
            LogHostRunning(_options.ActiveWorld, _engine!.CurrentMinute.Value);
            await ConsumeCommandsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown proceeds through the final checkpoint below.
        }
        catch (Exception exception)
        {
            terminalFailure = exception;
            Publish(SimulationHostState.Faulted, exception.Message);
            LogHostFaulted(exception);
        }
        finally
        {
            var shutdownFailure = await ShutdownAsync(terminalFailure is not null);
            terminalFailure ??= shutdownFailure;
        }

        if (terminalFailure is not null)
        {
            Interlocked.CompareExchange(ref _terminalFailure, terminalFailure, null);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(terminalFailure).Throw();
        }
    }

    private async Task OpenOrCreateWorldAsync(CancellationToken cancellationToken)
    {
        _database = await WorldDatabase.OpenAsync(_options.DatabasePath, cancellationToken);
        _checkpointStore = _database.CreateCheckpointStore();
        var hasCheckpoint = await _database.HasCheckpointAsync(cancellationToken);
        if (hasCheckpoint)
        {
            var snapshot = await _checkpointStore.LoadAsync(cancellationToken);
            _engine = SimulationEngine.FromPersistenceSnapshot(snapshot);
            return;
        }

        _engine = new SimulationEngine(_options.WorldSeed, worldConfiguration: WorldGenerationConfiguration.Default.CanonicalJson);
        await _checkpointStore.CheckpointAsync(_engine.CreatePersistenceSnapshot(), DateTime.UtcNow, cancellationToken: cancellationToken);
        LogWorldCreated(_options.ActiveWorld, _options.DatabasePath);
    }

    private async Task ConsumeCommandsAsync(CancellationToken stoppingToken)
    {
        await foreach (var command in _commands.Reader.ReadAllAsync(stoppingToken))
        {
            switch (command)
            {
                case CheckpointSimulationCommand checkpoint:
                    await ProcessCheckpointAsync(checkpoint, stoppingToken);
                    break;
                case FailSimulationCommand failure:
                    failure.Completion.TrySetException(failure.Failure);
                    throw failure.Failure;
                default:
                    command.SetException(new InvalidOperationException("Unsupported simulation command."));
                    break;
            }
        }
    }

    private async Task ProcessCheckpointAsync(CheckpointSimulationCommand command, CancellationToken cancellationToken)
    {
        try
        {
            await _checkpointStore!.CheckpointAsync(_engine!.CreatePersistenceSnapshot(), DateTime.UtcNow, cancellationToken: cancellationToken);
            command.Completion.TrySetResult(new CheckpointCommandResult(true, _engine.CurrentMinute.Value));
        }
        catch (Exception exception)
        {
            command.SetException(exception);
            throw;
        }
    }

    private async Task<Exception?> ShutdownAsync(bool preserveFaulted)
    {
        if (!preserveFaulted)
        {
            Publish(SimulationHostState.Stopping);
        }

        _commands.Writer.TryComplete();
        while (_commands.Reader.TryRead(out var command))
        {
            command.SetException(new OperationCanceledException("The simulation host is stopping."));
        }

        Exception? shutdownFailure = null;
        if (!preserveFaulted && _engine is not null && _checkpointStore is not null)
        {
            try
            {
                if (Interlocked.Exchange(ref _failNextFinalCheckpointForTesting, 0) != 0)
                {
                    throw new InvalidOperationException("Controlled final checkpoint failure.");
                }

                await _checkpointStore.CheckpointAsync(_engine.CreatePersistenceSnapshot(), DateTime.UtcNow);
                LogFinalCheckpointCompleted(_engine.CurrentMinute.Value);
            }
            catch (Exception exception)
            {
                shutdownFailure = exception;
                Publish(SimulationHostState.Faulted, exception.Message);
                LogFinalCheckpointFailed(exception);
            }
        }

        if (_database is not null)
        {
            try
            {
                await _database.DisposeAsync();
            }
            catch (Exception exception)
            {
                var hadEarlierFailure = shutdownFailure is not null;
                shutdownFailure ??= exception;
                if (!preserveFaulted && !hadEarlierFailure)
                {
                    Publish(SimulationHostState.Faulted, exception.Message);
                }
                LogDatabaseCleanupFailed(exception);
            }
        }

        return shutdownFailure;
    }

    private void Publish(SimulationHostState state, string? error = null)
    {
        var engine = _engine;
        var status = new ServerStatusSnapshot(
            state,
            engine?.CurrentMinute.Value ?? 0,
            engine?.PendingEventCount ?? 0,
            FormatWorldSeed(engine?.Seed.Value ?? _options.WorldSeed.Value),
            error,
            engine is null ? null : CreateWorldSummary(engine.World));
        Interlocked.Exchange(ref _status, status);
    }

    private static WorldSummarySnapshot CreateWorldSummary(WorldMap world)
    {
        var terrainCounts = Enum.GetValues<TerrainType>().ToDictionary(type => type.ToString(), type => world.Tiles.Count(tile => tile.Terrain == type), StringComparer.Ordinal);
        var resourceCounts = Enum.GetValues<ResourceType>().ToDictionary(type => type.ToString(), type => world.Resources.Count(resource => resource.Type == type), StringComparer.Ordinal);
        return new WorldSummarySnapshot(
            FormatWorldSeed(world.OriginalSeed.Value),
            world.Width,
            world.Height,
            world.Tiles.Count,
            world.GenerationVersion,
            world.GenerationAttempt,
            new WorldStartingSiteSnapshot(world.StartingSite.X, world.StartingSite.Y),
            new ReadOnlyDictionary<string, int>(terrainCounts),
            new ReadOnlyDictionary<string, int>(resourceCounts),
            world.Fingerprint);
    }

    private static string FormatWorldSeed(ulong seed) => seed.ToString(CultureInfo.InvariantCulture);

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Simulation host running for world {ActiveWorld} at minute {WorldMinute}")]
    private partial void LogHostRunning(string activeWorld, long worldMinute);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Critical, Message = "Simulation host faulted")]
    private partial void LogHostFaulted(Exception exception);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Created foundation world {ActiveWorld} at {DatabasePath}")]
    private partial void LogWorldCreated(string activeWorld, string databasePath);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Final simulation checkpoint completed at minute {WorldMinute}")]
    private partial void LogFinalCheckpointCompleted(long worldMinute);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "Final simulation checkpoint failed")]
    private partial void LogFinalCheckpointFailed(Exception exception);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error, Message = "Simulation database cleanup failed")]
    private partial void LogDatabaseCleanupFailed(Exception exception);
}
