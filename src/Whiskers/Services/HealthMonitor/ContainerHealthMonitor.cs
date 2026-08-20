using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Whiskers.Configuration;
using Whiskers.Hubs;
using Whiskers.Models;
using Whiskers.Services.Docker;
using Whiskers.Services.Notifications;

namespace Whiskers.Services.HealthMonitor;

public class ContainerHealthMonitor : BackgroundService
{
    private readonly IDockerService _docker;
    private readonly IHealthStore _healthStore;
    private readonly INotificationService _notifications;
    private readonly IHubContext<ContainerHub> _hubContext;
    private readonly HealthMonitorSettings _settings;
    private readonly ILogger<ContainerHealthMonitor> _logger;

    private readonly ConcurrentDictionary<string, string> _previousStates = new();
    private readonly ConcurrentDictionary<string, string> _previousHealth = new();
    private readonly ConcurrentDictionary<string, List<DateTime>> _restartTimestamps = new();
    // A host dropping off the fleet used to be silent everywhere; the tracker turns "did not answer"
    // into server_unreachable / server_recovered events (see ServerReachabilityTracker).
    private readonly ServerReachabilityTracker _reachability;

    public ContainerHealthMonitor(
        IDockerService docker,
        IHealthStore healthStore,
        INotificationService notifications,
        IHubContext<ContainerHub> hubContext,
        IOptions<HealthMonitorSettings> settings,
        ILogger<ContainerHealthMonitor> logger)
    {
        _docker = docker;
        _healthStore = healthStore;
        _notifications = notifications;
        _hubContext = hubContext;
        _settings = settings.Value;
        _logger = logger;
        _reachability = new ServerReachabilityTracker(_settings.ServerUnreachableCycles);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Container health monitor started (interval: {Interval}s)",
            _settings.CheckIntervalSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // WaitAsync bounds the whole cycle to shutdown: an in-flight Docker call (which carries
                // no cancellation token) is abandoned so the service still stops within the host window.
                await RunHealthCycleAsync(ct).WaitAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Health check cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.CheckIntervalSeconds), ct);
        }
    }

    private async Task RunHealthCycleAsync(CancellationToken ct)
    {
        var listing = await _docker.ListAllContainersDetailedAsync(all: true);
        var containers = listing.Containers;

        await ProcessServerReachabilityAsync(listing);

        foreach (var container in containers)
        {
            await ProcessContainer(container);
        }

        // Bound the per-container maps: drop entries for containers that no longer exist — but keep the
        // state of servers that did NOT answer this cycle. Their containers are missing from the list
        // because the host is silent, not because they are gone; dropping the state here means a real
        // stop during the outage is never reported and every container looks "new" on recovery.
        var liveKeys = containers.Select(CompositeKey).ToHashSet();
        PruneToLive(_previousStates, liveKeys, listing);
        PruneToLive(_restartTimestamps, liveKeys, listing);
        PruneToLive(_previousHealth, liveKeys, listing);

        await _hubContext.Clients.All.SendAsync("ContainerListUpdated", containers, ct);
    }

    private async Task ProcessServerReachabilityAsync(FleetContainerListing listing)
    {
        foreach (var evt in _reachability.Evaluate(listing))
        {
            if (evt.EventType == "server_unreachable")
                _logger.LogWarning("Server {ServerName} unreachable: {Detail}", evt.ServerName, evt.ImageInfo);
            else
                _logger.LogInformation("Server {ServerName} is reachable again", evt.ServerName);

            await _notifications.SendAsync(evt);
        }
    }

    private static string CompositeKey(ContainerInfo container)
        => $"{container.ServerId}:{container.Id}";

    private static void PruneToLive<TValue>(
        ConcurrentDictionary<string, TValue> map, IReadOnlySet<string> liveKeys, FleetContainerListing listing)
    {
        foreach (var key in map.Keys)
            if (!liveKeys.Contains(key) && listing.MayPruneStateFor(key.Split(':', 2)[0]))
                map.TryRemove(key, out _);
    }

    private async Task ProcessContainer(ContainerInfo container)
    {
        var key = CompositeKey(container);
        var (state, exitCode, oomKilled) = await SafeInspect(container.Id, container.ServerId);

        var record = new HealthRecord
        {
            ContainerId = container.Id,
            ContainerName = container.Name,
            Timestamp = DateTime.UtcNow,
            State = state,
            HealthStatus = container.HealthStatus,
            ExitCode = exitCode,
            OomKilled = oomKilled
        };

        _healthStore.AddRecord(record);

        // Detect unhealthy transition
        if (_previousHealth.TryGetValue(key, out var prevHealth))
        {
            if (prevHealth != "unhealthy" && container.HealthStatus == "unhealthy")
            {
                await SendNotificationIfAllowed(new NotificationEvent
                {
                    ContainerId = container.Id,
                    ContainerName = container.Name,
                    Image = container.Image,
                    ServerId = container.ServerId,
                    ServerName = container.ServerName,
                    EventType = "unhealthy"
                });
            }
        }
        _previousHealth[key] = container.HealthStatus;

        // Detect unexpected stop
        if (_previousStates.TryGetValue(key, out var prevState))
        {
            if (prevState == "running" && state == "exited")
            {
                if (oomKilled)
                {
                    await SendNotificationIfAllowed(new NotificationEvent
                    {
                        ContainerId = container.Id,
                        ContainerName = container.Name,
                        Image = container.Image,
                        ServerId = container.ServerId,
                        ServerName = container.ServerName,
                        EventType = "oom_killed"
                    });
                }
                else if (exitCode != 0)
                {
                    await SendNotificationIfAllowed(new NotificationEvent
                    {
                        ContainerId = container.Id,
                        ContainerName = container.Name,
                        Image = container.Image,
                        ServerId = container.ServerId,
                        ServerName = container.ServerName,
                        EventType = "stopped",
                        ExitCode = exitCode
                    });
                }
            }

            // Detect restart loops
            if (IsRestart(prevState, state))
            {
                var timestamps = _restartTimestamps.GetOrAdd(key, _ => new List<DateTime>());
                timestamps.Add(DateTime.UtcNow);

                var windowStart = DateTime.UtcNow.AddMinutes(-_settings.RestartLoopWindowMinutes);
                timestamps.RemoveAll(t => t < windowStart);

                if (timestamps.Count >= _settings.RestartLoopThreshold)
                {
                    await SendNotificationIfAllowed(new NotificationEvent
                    {
                        ContainerId = container.Id,
                        ContainerName = container.Name,
                        Image = container.Image,
                        ServerId = container.ServerId,
                        ServerName = container.ServerName,
                        EventType = "restart_loop",
                        RestartCount = timestamps.Count,
                        WindowMinutes = _settings.RestartLoopWindowMinutes
                    });
                    timestamps.Clear();
                }
            }
        }
        // Don't overwrite the last known state with "unknown" (a transient inspect failure, e.g. a
        // flapping SSH tunnel) — otherwise the next real "running" reads as a restart and a real stop
        // is missed.
        if (state != "unknown")
            _previousStates[key] = state;
    }

    /// <summary>Send a container event. The per-container mute/prefs check that used to live here now runs
    /// centrally in <see cref="CompositeNotificationService"/>, so every producer honours it — not just this
    /// monitor.</summary>
    private Task SendNotificationIfAllowed(NotificationEvent evt) => _notifications.SendAsync(evt);

    private async Task<(string State, int ExitCode, bool OomKilled)> SafeInspect(string containerId, string serverId)
    {
        try
        {
            return await _docker.InspectContainerStateAsync(containerId, serverId);
        }
        catch
        {
            return ("unknown", 0, false);
        }
    }

    /// <summary>A restart is a container now <c>running</c> that was previously in a real stopped state.
    /// A prior <c>unknown</c> → <c>running</c> (e.g. a flapping SSH-tunnel inspect) is NOT a restart — that
    /// was the source of phantom restart-loop alerts.</summary>
    public static bool IsRestart(string? prevState, string state)
        => state == "running" && prevState is "exited" or "restarting" or "created" or "dead";
}
