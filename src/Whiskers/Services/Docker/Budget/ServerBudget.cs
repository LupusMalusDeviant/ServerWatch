using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using Whiskers.Configuration;

namespace Whiskers.Services.Docker.Budget;

/// <summary>
/// The per-server concurrency cap (Plan-0001 WP3). One pair of semaphores per server — background and
/// interactive — created on first sight of that server and kept for the process lifetime.
///
/// <para>Defaults are chosen for the smallest machine in the fleet (two cores), not the development box:
/// the incident happened on a two-core Hetzner host, and a limit tuned on an eight-core laptop would have
/// permitted exactly the load that caused it.</para>
/// </summary>
public sealed class ServerBudget : IServerBudget
{
    private static readonly AsyncLocal<bool> IsBackground = new();

    private sealed class Lanes(int background, int interactive)
    {
        public readonly SemaphoreSlim Background = new(background, background);
        public readonly SemaphoreSlim Interactive = new(interactive, interactive);
        public readonly int BackgroundLimit = background;
        public readonly int InteractiveLimit = interactive;
        public int BackgroundInFlight;
        public int InteractiveInFlight;
        public long Started;
        public long WaitedMillisecondsTotal;
        public long MaxWaitMilliseconds;
        public long DiscardedDuplicates;
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> InFlightKeys = new(StringComparer.Ordinal);
    }

    private readonly ConcurrentDictionary<string, Lanes> _lanes = new(StringComparer.OrdinalIgnoreCase);
    private readonly IOptionsMonitor<ServerBudgetSettings> _settings;
    private readonly ILogger<ServerBudget> _logger;

    public ServerBudget(IOptionsMonitor<ServerBudgetSettings> settings, ILogger<ServerBudget> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public IDisposable BackgroundScope() => new Scope();

    private sealed class Scope : IDisposable
    {
        private readonly bool _previous = IsBackground.Value;
        public Scope() => IsBackground.Value = true;
        public void Dispose() => IsBackground.Value = _previous;
    }

    public async Task<T> RunAsync<T>(string serverId, Func<Task<T>> operation, CancellationToken ct = default, string? singleFlightKey = null)
    {
        var lanes = LanesFor(serverId);
        var background = IsBackground.Value;
        var gate = background ? lanes.Background : lanes.Interactive;

        // Single-flight applies to background work only. A loop that skips one cycle loses a minute; a
        // person who gets "already running" instead of their logs loses trust in the tool.
        var dedupe = background && singleFlightKey is not null ? $"{serverId}|{singleFlightKey}" : null;
        if (dedupe is not null && !lanes.InFlightKeys.TryAdd(dedupe, 0))
        {
            Interlocked.Increment(ref lanes.DiscardedDuplicates);
            throw new DuplicateRequestException(dedupe);
        }

        var waited = Stopwatch.GetTimestamp();
        // The token covers the WAIT, not just the operation: a caller that timed out while queued has to
        // leave the queue, otherwise the backlog outlives the callers and the next slot goes to a ghost.
        try { await gate.WaitAsync(ct); }
        catch { if (dedupe is not null) lanes.InFlightKeys.TryRemove(dedupe, out _); throw; }
        var waitMs = (long)Stopwatch.GetElapsedTime(waited).TotalMilliseconds;

        Interlocked.Add(ref lanes.WaitedMillisecondsTotal, waitMs);
        Interlocked.Increment(ref lanes.Started);
        long seenMax;
        while (waitMs > (seenMax = Interlocked.Read(ref lanes.MaxWaitMilliseconds)))
            Interlocked.CompareExchange(ref lanes.MaxWaitMilliseconds, waitMs, seenMax);

        if (background) Interlocked.Increment(ref lanes.BackgroundInFlight);
        else Interlocked.Increment(ref lanes.InteractiveInFlight);

        try
        {
            return await operation();
        }
        finally
        {
            if (dedupe is not null) lanes.InFlightKeys.TryRemove(dedupe, out _);
            if (background) Interlocked.Decrement(ref lanes.BackgroundInFlight);
            else Interlocked.Decrement(ref lanes.InteractiveInFlight);
            gate.Release();
        }
    }

    public ServerBudgetSnapshot Snapshot(string serverId) => Describe(serverId, LanesFor(serverId));

    public IReadOnlyList<ServerBudgetSnapshot> SnapshotAll() =>
        _lanes.Select(kv => Describe(kv.Key, kv.Value)).OrderBy(s => s.ServerId, StringComparer.Ordinal).ToList();

    private static ServerBudgetSnapshot Describe(string serverId, Lanes l) => new(
        serverId,
        Volatile.Read(ref l.BackgroundInFlight),
        Volatile.Read(ref l.InteractiveInFlight),
        l.BackgroundLimit,
        l.InteractiveLimit,
        Interlocked.Read(ref l.Started),
        Interlocked.Read(ref l.WaitedMillisecondsTotal),
        Interlocked.Read(ref l.MaxWaitMilliseconds),
        Interlocked.Read(ref l.DiscardedDuplicates));

    private Lanes LanesFor(string serverId) =>
        _lanes.GetOrAdd(serverId, id =>
        {
            var s = _settings.CurrentValue;
            var (background, interactive) = s.LimitsFor(id);
            _logger.LogInformation(
                "Load budget for server {ServerId}: {Background} concurrent background calls, {Interactive} interactive",
                id, background, interactive);
            return new Lanes(background, interactive);
        });
}
