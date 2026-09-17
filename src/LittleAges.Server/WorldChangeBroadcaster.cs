using Microsoft.AspNetCore.SignalR;

namespace LittleAges.Server;

public sealed record WorldChangedPayload(
    long Revision,
    long WorldMinute,
    SimulationHostState HostState,
    PersistenceState PersistenceState);

/// <summary>Observer-only SignalR endpoint. Canonical simulation state is never exposed here.</summary>
public sealed class WorldHub : Hub
{
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
