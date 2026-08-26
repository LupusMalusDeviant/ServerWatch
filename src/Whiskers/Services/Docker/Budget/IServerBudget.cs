namespace Whiskers.Services.Docker.Budget;

/// <summary>What the budget currently looks like for one server. Read-only view for the self-metrics
/// (SP-3) and the server page; the numbers are the raw material Plan-0003 exports.</summary>
public sealed record ServerBudgetSnapshot(
    string ServerId,
    int BackgroundInFlight,
    int InteractiveInFlight,
    int BackgroundLimit,
    int InteractiveLimit,
    long Started,
    long WaitedMillisecondsTotal,
    long MaxWaitMilliseconds,
    long DiscardedDuplicates);

/// <summary>
/// Caps the load Whiskers puts on ONE server, across every caller.
///
/// <para>Before this existed each background loop had its own timeout and no idea what the others were doing
/// on the same host. The server, of course, sees the sum. On 2026-08-26 that sum was thirteen concurrent
/// full-log scans against a two-core machine. A per-loop fix would have left the next loop free to repeat it,
/// which is why the limit lives here — at the single point every Docker call passes through — instead of in
/// the loops.</para>
///
/// <para>Two separate lanes: background work and anything a human is waiting for. They must not share a
/// queue, or a CVE scan holding the budget makes the UI look frozen and the health checks stop running while
/// everything still reports "fine". Callers mark background work with <see cref="BackgroundScope"/>; anything
/// unmarked counts as interactive, which is the safe default — mistaking a loop for a user costs a slot,
/// mistaking a user for a loop costs responsiveness.</para>
/// </summary>
/// <summary>Thrown when a background caller asks for something an identical background request is already
/// fetching. Not an error condition — the next cycle will ask again in a minute.</summary>
public sealed class DuplicateRequestException(string key)
    : Exception($"An identical background request is already in flight: {key}");

public interface IServerBudget
{
    /// <summary>Runs the operation once a slot on that server is free. Cancellation applies to the wait as
    /// well as the operation — a caller that gave up must not still be holding a place in the queue.
    ///
    /// <para><paramref name="singleFlightKey"/> discards a second <b>background</b> request for something
    /// already in flight (Plan-0001 WP3.2), throwing <see cref="DuplicateRequestException"/> rather than
    /// queueing it. Interactive callers ignore the key: for a background loop a discarded request costs one
    /// skipped cycle, but for a person it would be an error message where an answer belongs — and the load it
    /// would save is already capped by the budget itself.</para></summary>
    Task<T> RunAsync<T>(string serverId, Func<Task<T>> operation, CancellationToken ct = default, string? singleFlightKey = null);

    /// <summary>Marks everything inside as background work for the current async flow. Nested scopes are
    /// harmless; the flag is restored on dispose.</summary>
    IDisposable BackgroundScope();

    /// <summary>Current usage for one server. Never throws for an unknown server — it reports an idle
    /// budget, because "no data" and "nothing running" must not be told apart by an exception.</summary>
    ServerBudgetSnapshot Snapshot(string serverId);

    /// <summary>Every server the budget has seen, for the self-metrics loop.</summary>
    IReadOnlyList<ServerBudgetSnapshot> SnapshotAll();
}
