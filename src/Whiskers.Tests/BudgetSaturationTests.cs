using Whiskers.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Whiskers.Services.Docker.Budget;

namespace Whiskers.Tests;

/// <summary>
/// Who gets blamed when a call runs out of time (regression 2026-08-27).
///
/// <para>The budget makes callers queue for a per-server slot, and the caller's deadline covers the wait —
/// deliberately, so a caller who gave up leaves the queue instead of holding a slot for a ghost. The
/// consequence went unexamined: a call that waited four seconds of its five-second budget gets one second to
/// talk to the server, times out, and is recorded as a <em>server</em> failure. Five of those in a row open
/// the circuit. When Whiskers itself is busy this happens on every server at once, so the fleet appears to
/// fail together and the operator gets a stream of "paused / resumed" notifications about servers that were
/// never unhealthy.</para>
///
/// <para>Same exception, opposite meaning, and the difference is where the time went. These tests hold that
/// line in both directions: the queue must not be blamed on the host, and a genuine host timeout must keep
/// counting — the signal the 2026-08-26 incident produced for six days and nobody tallied.</para>
/// </summary>
public class BudgetSaturationTests
{
    private static ServerBudget Budget(int background = 1, int interactive = 1) =>
        new(new StaticOptionsMonitor<ServerBudgetSettings>(new ServerBudgetSettings
            {
                BackgroundConcurrency = background,
                InteractiveConcurrency = interactive
            }),
            NullLogger<ServerBudget>.Instance);

    [Fact]
    public async Task A_deadline_spent_in_the_queue_is_reported_as_saturation()
    {
        // THE case. One slot, held by a slow call; the second caller spends its whole budget waiting and then
        // fails immediately. Nothing about that says anything about the server.
        var budget = Budget(background: 1);
        using var _ = ServerBudget.EnterBackground();

        var release = new TaskCompletionSource();
        var hog = budget.RunAsync("s1", async () => { await release.Task; return 1; });
        await Task.Delay(50);    // the hog now holds the only slot

        // The queued caller must NOT be the one that frees the slot — that is a deadlock, and the first
        // version of this test was exactly that: the second call could only run after the first finished,
        // and the first could only finish once the second ran.
        var queued = Assert.ThrowsAsync<BudgetSaturatedException>(() =>
            budget.RunAsync<int>("s1", () => throw new TaskCanceledException("deadline")));
        await Task.Delay(250);   // it is waiting in the queue for this long
        release.SetResult();     // let it through — it then fails at once, having run for almost no time

        var saturated = await queued;

        Assert.Equal("s1", saturated.ServerId);
        Assert.True(saturated.Waited > saturated.Ran,
            $"waited {saturated.Waited.TotalMilliseconds:F0}ms, ran {saturated.Ran.TotalMilliseconds:F0}ms");
        Assert.Contains("queue ran out the clock", saturated.Message);
        await hog;
    }

    [Fact]
    public async Task A_timeout_at_the_server_is_still_a_plain_timeout()
    {
        // The counter-direction, and the one that must not be weakened: an uncontended call that runs and
        // then times out is exactly the signal the incident report is about. If this ever starts arriving as
        // saturation, the circuit breaker goes deaf.
        var budget = Budget(background: 4);
        using var _ = ServerBudget.EnterBackground();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            budget.RunAsync<int>("s1", async () =>
            {
                await Task.Delay(60);                     // ran longer than it waited (it never waited)
                throw new TaskCanceledException("host did not answer");
            }));
    }

    [Fact]
    public async Task A_failure_that_is_not_a_timeout_is_never_reclassified()
    {
        // Saturation is about deadlines. A server that answers with an error answered — that stays a failure
        // whatever the queue was doing.
        var budget = Budget(background: 1);
        using var _ = ServerBudget.EnterBackground();

        var release = new TaskCompletionSource();
        var hog = budget.RunAsync("s1", async () => { await release.Task; return 1; });
        await Task.Delay(50);

        var queued = Assert.ThrowsAsync<InvalidOperationException>(() =>
            budget.RunAsync<int>("s1", () => throw new InvalidOperationException("host said no")));
        await Task.Delay(150);
        release.SetResult();

        await queued;
        await hog;
    }

    [Fact]
    public async Task Saturation_is_counted_so_the_bottleneck_can_be_seen()
    {
        // Without a number, "Whiskers is the bottleneck" is a thing somebody has to guess from a slow UI —
        // which is how this went unnoticed until a person complained about loading times.
        var budget = Budget(background: 1);
        using var _ = ServerBudget.EnterBackground();

        var before = budget.Snapshot("s1");

        var release = new TaskCompletionSource();
        var hog = budget.RunAsync("s1", async () => { await release.Task; return 1; });
        await Task.Delay(50);
        var queued = Assert.ThrowsAsync<BudgetSaturatedException>(() =>
            budget.RunAsync<int>("s1", () => throw new TaskCanceledException()));
        await Task.Delay(150);
        release.SetResult();
        await queued;
        await hog;

        var after = budget.Snapshot("s1");
        Assert.True(after.SaturationFailures > before.SaturationFailures,
            "a saturation failure must show up in the snapshot, or nobody can tell why the UI is slow");
    }
}
