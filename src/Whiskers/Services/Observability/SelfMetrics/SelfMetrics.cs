using System.Collections.Concurrent;

namespace Whiskers.Services.Observability.SelfMetrics;

/// <summary>
/// In-process counters about Whiskers itself (Plan-0003 WP1/WP2).
///
/// <para>Everything here is a lock-free read of process memory. Collecting these numbers must never cost a
/// Docker call, a database round trip or a lock held across an await — a self-measurement that adds load is
/// the same mistake as the one it exists to reveal, one level up. <c>SelfMetricsTests</c> pins that.</para>
///
/// <para>Labels are fixed to loop, server and counter name. Container ids are deliberately impossible: with
/// 200 containers across a fleet, a container label would multiply the series count into the thousands and
/// fill the time-series database — a monitoring outage caused by monitoring.</para>
/// </summary>
public sealed class SelfMetrics : ISelfMetrics
{
    private sealed class LoopState
    {
        public DateTime? LastSuccess;
        public DateTime? LastAttempt;
        public long DurationTicks;
        public long Cycles;
        public long Failures;
        public long Skips;
        public string? SkipReason;
        public TimeSpan? ExpectedInterval;
    }

    // Key: "{loop}|{serverId}". Loops and servers are both bounded and small, so this never grows unbounded.
    private readonly ConcurrentDictionary<string, LoopState> _loops = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, long>> _counters = new(StringComparer.Ordinal);

    private static string Key(string loop, string serverId) => $"{loop}|{serverId}";

    public void RecordCycle(string loop, string serverId, TimeSpan duration, bool success, TimeSpan? interval = null)
    {
        var s = _loops.GetOrAdd(Key(loop, serverId), _ => new LoopState());
        Interlocked.Increment(ref s.Cycles);
        Interlocked.Exchange(ref s.DurationTicks, duration.Ticks);
        s.LastAttempt = DateTime.UtcNow;
        s.SkipReason = null;
        if (interval is not null) s.ExpectedInterval = interval;

        if (success) s.LastSuccess = DateTime.UtcNow;
        else Interlocked.Increment(ref s.Failures);
    }

    public void RecordSkip(string loop, string serverId, string reason)
    {
        var s = _loops.GetOrAdd(Key(loop, serverId), _ => new LoopState());
        Interlocked.Increment(ref s.Skips);
        s.LastAttempt = DateTime.UtcNow;
        s.SkipReason = reason;
    }

    public void Count(string name, string serverId)
    {
        var perServer = _counters.GetOrAdd(name, _ => new ConcurrentDictionary<string, long>(StringComparer.Ordinal));
        perServer.AddOrUpdate(serverId, 1, (_, v) => v + 1);
    }

    public IReadOnlyList<LoopHealth> Loops() =>
        _loops
            .Select(kv =>
            {
                var parts = kv.Key.Split('|', 2);
                var s = kv.Value;
                return new LoopHealth(
                    parts[0], parts[1],
                    s.LastSuccess, s.LastAttempt,
                    TimeSpan.FromTicks(Interlocked.Read(ref s.DurationTicks)),
                    Interlocked.Read(ref s.Cycles),
                    Interlocked.Read(ref s.Failures),
                    Interlocked.Read(ref s.Skips),
                    s.SkipReason,
                    s.ExpectedInterval);
            })
            .OrderBy(l => l.Loop, StringComparer.Ordinal)
            .ThenBy(l => l.ServerId, StringComparer.Ordinal)
            .ToList();

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> Counters() =>
        _counters.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyDictionary<string, long>)kv.Value.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);
}
