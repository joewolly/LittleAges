using Microsoft.AspNetCore.SignalR;
using System.Runtime.CompilerServices;

namespace LittleAges.Server;

public sealed record WorldChangedPayload(
    long Revision,
    long WorldMinute,
    SimulationHostState HostState,
    PersistenceState PersistenceState);

/// <summary>A compact immutable live frame. It contains only scene-critical state.</summary>
public sealed record WorldStreamFrame(
    long Sequence,
    long SentAtUnixMilliseconds,
    long Revision,
    ServerStatusSnapshot Status,
    IReadOnlyList<ServerCitizenSnapshot> Citizens,
    IReadOnlyList<ServerStructureSnapshot> Structures,
    System.Text.Json.JsonElement? Living = null)
{
    internal static WorldStreamFrame Create(long sequence, ServerObservationSnapshot observation) =>
        new(sequence, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), observation.Revision, observation.Status, observation.Citizens, observation.Structures, observation.Living);
}

/// <summary>Observer-only SignalR endpoint. Canonical simulation state is never exposed here.</summary>
public sealed class WorldHub(SimulationHost simulationHost, ServerOptions options) : Hub
{
    /// <summary>
    /// Streams immutable observer frames for the lifetime of one connected client. The stream
    /// is presentation-only and cannot mutate or delay the single-writer simulation host.
    /// </summary>
    public async IAsyncEnumerable<WorldStreamFrame> StreamWorld([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sequence = 0L;
        yield return WorldStreamFrame.Create(checked(++sequence), simulationHost.Observation);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.ObserverStreamIntervalMilliseconds));
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken)) yield break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            yield return WorldStreamFrame.Create(checked(++sequence), simulationHost.Observation);
        }
    }
}

/// <summary>
/// Keeps only the newest immutable world notification. Publishing is synchronous and never
/// waits for a browser or network operation.
/// </summary>
public sealed class WorldChangeBroadcaster
{
    private readonly WorldChangeCoalescer _coalescer = new();

    public void Publish(WorldChangedPayload payload) => _coalescer.Publish(payload);

    internal bool TryFlush(out WorldChangedPayload? payload) => _coalescer.TryFlush(out payload);
}

internal sealed class WorldChangeCoalescer
{
    private readonly object _gate = new();
    private WorldChangedPayload? _latest;
    private long _lastEmittedRevision;

    public void Publish(WorldChangedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        lock (_gate)
        {
            if (_latest is null || payload.Revision > _latest.Revision) _latest = payload;
        }
    }

    public bool TryFlush(out WorldChangedPayload? payload)
    {
        lock (_gate)
        {
            if (_latest is null || _latest.Revision <= _lastEmittedRevision)
            {
                payload = null;
                return false;
            }

            payload = _latest;
            _lastEmittedRevision = payload.Revision;
            return true;
        }
    }
}

/// <summary>Coalesces world notifications to the configured browser update interval.</summary>
public sealed partial class WorldChangeBroadcasterService(
    WorldChangeBroadcaster broadcaster,
    IHubContext<WorldHub> hubContext,
    ServerOptions options,
    ILogger<WorldChangeBroadcasterService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.BrowserUpdateIntervalMilliseconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await FlushAsync(stoppingToken);
        }
    }

    internal async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!broadcaster.TryFlush(out var payload) || payload is null) return;
        try
        {
            await hubContext.Clients.All.SendAsync("worldChanged", payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogNotificationFailed(exception, payload.Revision);
        }
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Warning, Message = "SignalR worldChanged notification failed for revision {Revision}")]
    private partial void LogNotificationFailed(Exception exception, long revision);
}
