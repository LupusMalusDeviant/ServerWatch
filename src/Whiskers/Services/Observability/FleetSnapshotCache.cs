using System.Collections.Concurrent;
using Whiskers.Models;

namespace Whiskers.Services.Observability;

/// <summary>One server's last known state, and when it was learned.</summary>
/// <param name="Age">How old the reading is. Never omit this when showing the value — a cached number
/// presented as a current one is the failure this whole strand exists to remove.</param>
public sealed record CachedServerInfo(ServerSystemInfo Info, DateTime CapturedAtUtc, TimeSpan Age);

/// <summary>
/// The last known reading per server, so a page can paint immediately instead of waiting for the fleet.
///
/// <para><b>Why.</b> The background loops already ask every server every 30 seconds. The dashboard then asked
/// again, from scratch, and rendered nothing usable until those answers came back — two seconds of a page
/// that claimed the whole fleet was unreachable. The data was already in the building; nobody had kept it.</para>
///
/// <para><b>Bounded on purpose.</b> One entry per server, not a history — the size is the fleet, not the
/// uptime. Entries are dropped once they pass <see cref="MaxAge"/>, and
/// <see cref="PruneRemovedServers"/> clears out servers that no longer exist, the same way the CVE store had
/// to learn to (2026-08-27).</para>
///
/// <para><b>Age is not optional.</b> <see cref="Get"/> hands back how old the reading is and refuses nothing;
/// deciding what is too old to show belongs to the caller, which knows whether it is drawing a dashboard or
/// making a decision. What the cache will not do is hand out a value that looks fresh.</para>
/// </summary>
public interface IFleetSnapshotCache
{
    /// <summary>Beyond this a reading is discarded rather than served. Ten minutes is twenty missed metrics
    /// cycles: past that the number says more about the outage than about the server.</summary>
    static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    void Put(string serverId, ServerSystemInfo info);

    /// <summary>The last known reading, or null if there is none or it has aged out.</summary>
    CachedServerInfo? Get(string serverId);

    /// <summary>Every reading still worth showing, keyed by server id.</summary>
    IReadOnlyDictionary<string, CachedServerInfo> GetAll();

    /// <summary>Drops readings for servers that are no longer configured. Returns how many went.</summary>
    int PruneRemovedServers(IReadOnlySet<string> configuredServerIds);
}

public sealed class FleetSnapshotCache(TimeProvider? time = null) : IFleetSnapshotCache
{
    private readonly ConcurrentDictionary<string, (ServerSystemInfo Info, DateTime At)> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public void Put(string serverId, ServerSystemInfo info)
    {
        if (string.IsNullOrWhiteSpace(serverId)) return;
        _entries[serverId] = (info, _time.GetUtcNow().UtcDateTime);

        // Sweep on write rather than on a timer: writes are the only thing that grows this, they happen every
        // cycle anyway, and a cache that needs its own background loop to stay small is one more loop to
        // wonder about at three in the morning.
        DropExpired();
    }

    public CachedServerInfo? Get(string serverId)
    {
        if (string.IsNullOrWhiteSpace(serverId)) return null;
        if (!_entries.TryGetValue(serverId, out var e)) return null;

        var age = _time.GetUtcNow().UtcDateTime - e.At;
        if (age > IFleetSnapshotCache.MaxAge)
        {
            _entries.TryRemove(serverId, out _);
            return null;
        }

        // A clock that jumped backwards would otherwise produce a negative age, which reads as "from the
        // future" and sorts wrong everywhere.
        return new CachedServerInfo(e.Info, e.At, age < TimeSpan.Zero ? TimeSpan.Zero : age);
    }

    public IReadOnlyDictionary<string, CachedServerInfo> GetAll()
    {
        var result = new Dictionary<string, CachedServerInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _entries.Keys)
            if (Get(key) is { } cached) result[key] = cached;   // Get also evicts what has aged out
        return result;
    }

    public int PruneRemovedServers(IReadOnlySet<string> configuredServerIds)
    {
        // An empty set is far more likely to mean "the server list could not be read" than "there are no
        // servers", and clearing everything on that reading is how a cache turns a config hiccup into a blank
        // dashboard. Same rule, same reason, as the CVE store (2026-08-27).
        if (configuredServerIds.Count == 0) return 0;

        var removed = 0;
        foreach (var key in _entries.Keys)
            if (!configuredServerIds.Contains(key) && _entries.TryRemove(key, out _)) removed++;
        return removed;
    }

    private void DropExpired()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var kv in _entries)
            if (now - kv.Value.At > IFleetSnapshotCache.MaxAge)
                _entries.TryRemove(kv.Key, out _);
    }
}
