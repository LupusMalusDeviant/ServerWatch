namespace Whiskers.Services.Observability.SelfMetrics;

/// <summary>How one background loop is doing against one server.</summary>
/// <param name="LastSuccess">When the loop last completed a cycle for this server. <b>The single most
/// important number in this type.</b> Failures are only counted when something happens; a loop that has
/// stopped entirely produces nothing at all, and the only thing that reveals it is the age of this
/// timestamp.</param>
/// <param name="SkipReason">Why the loop did not look at this server, when it deliberately skipped it —
/// "Kubernetes server, Docker loop", "suspended", "paused". A skipped server must still appear, or "the loop
/// does not run for this server" is indistinguishable from "the loop found nothing".</param>
public sealed record LoopHealth(
    string Loop,
    string ServerId,
    DateTime? LastSuccess,
    DateTime? LastAttempt,
    TimeSpan LastDuration,
    long Cycles,
    long Failures,
    long Skips,
    string? SkipReason);

/// <summary>
/// What Whiskers knows about itself (Plan-0003 WP1/WP2).
///
/// <para>Whiskers exports a Prometheus endpoint carrying the container inventory of the whole fleet and not
/// one number about its own behaviour. On 2026-08-26 the log monitor wrote "timed out after 15s" every cycle
/// for six days; nothing counted it, so nothing could act on it. The incident report calls that the earliest
/// and most precise signal of the whole event.</para>
///
/// <para>This type deliberately does NOT re-measure what the load budget and the circuit breaker already
/// count. It covers the loops: did each one run, when did it last succeed, and how long did it take.</para>
/// </summary>
public interface ISelfMetrics
{
    /// <summary>A cycle finished for this server.</summary>
    void RecordCycle(string loop, string serverId, TimeSpan duration, bool success);

    /// <summary>The loop deliberately did not look at this server this cycle, and why. Recording the skip is
    /// the point: a server that simply vanishes from the metrics looks exactly like a quiet one.</summary>
    void RecordSkip(string loop, string serverId, string reason);

    /// <summary>Counts something worth counting that would otherwise only be a log line — a log-fetch timeout,
    /// a discarded duplicate. Keyed by a short, low-cardinality name.</summary>
    void Count(string name, string serverId);

    IReadOnlyList<LoopHealth> Loops();

    /// <summary>Named counters, as name → server → value.</summary>
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> Counters();
}
