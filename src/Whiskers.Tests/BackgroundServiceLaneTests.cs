using System.Reflection;
using Microsoft.Extensions.Hosting;
using Whiskers.Services;
using Whiskers.Services.Docker.Budget;

namespace Whiskers.Tests;

/// <summary>
/// Every fleet loop must run in the background lane (Plan-0001 WP3, regression 2026-08-27).
///
/// <para>The per-server budget gives each server four concurrent slots for background loops and four for
/// anything a human is waiting on, kept apart so a long scan cannot freeze the UI. Which lane a call takes is
/// decided by an <c>AsyncLocal</c> flag the caller has to set — and for months exactly two of twelve loops set
/// it. The other ten queued in the lane reserved for people. The metrics loop alone sweeps all six servers
/// every 30 seconds and takes 20 to do it, so the interactive lane sat at 4 of 4 across the whole fleet, waits
/// reached five seconds, calls that waited too long timed out, and the circuit breaker opened on server after
/// server. A slow dashboard and a stream of "paused / resumed" notifications: the guard against overload had
/// become the source of it.</para>
///
/// <para>Nothing failed while that was true. No test went red, no log line said it, and the only visible
/// symptom was a UI that felt slow — which reads as "the server is busy", not as "we are queueing behind
/// ourselves". That is why this is a ratchet over the assembly rather than a test per service: the next loop
/// will be written by someone who has never heard of the lanes.</para>
/// </summary>
public class BackgroundServiceLaneTests
{
    private static IReadOnlyList<Type> HostedLoops() =>
        typeof(FleetBackgroundService).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true })
            .Where(t => typeof(BackgroundService).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Every_fleet_loop_runs_in_the_background_lane()
    {
        // THE assertion. A loop that derives from BackgroundService directly gets the interactive lane by
        // default — the one reserved for a person who is waiting — and nothing anywhere says so.
        var offenders = HostedLoops()
            .Where(t => !typeof(FleetBackgroundService).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These loops derive from BackgroundService instead of FleetBackgroundService, so their Docker " +
            "calls take the lane meant for waiting humans and will queue in front of the dashboard: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void The_loops_are_actually_found_so_an_empty_sweep_cannot_pass_for_a_clean_one()
    {
        // Without this the test above passes just as happily when the reflection query matches nothing —
        // which is exactly how a ratchet quietly stops ratcheting.
        var loops = HostedLoops();

        Assert.True(loops.Count >= 10,
            $"expected the fleet's background loops to be discoverable, found {loops.Count}");
        Assert.Contains(loops, t => t.Name == "MetricsCollectorService");
        Assert.Contains(loops, t => t.Name == "LogMonitorService");
    }

    [Fact]
    public void A_loop_cannot_quietly_take_the_interactive_lane_back()
    {
        // FleetBackgroundService.ExecuteAsync is sealed on purpose: overriding it would re-open the hole
        // without touching anything this test looks at.
        var executeAsync = typeof(FleetBackgroundService)
            .GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(executeAsync);
        Assert.True(executeAsync!.IsFinal, "ExecuteAsync must stay sealed, or a loop can opt out of the lane");
    }

    [Fact]
    public void The_scope_actually_flips_the_flag_and_puts_it_back()
    {
        // The mechanism itself, not just who calls it. If EnterBackground stopped working, every test above
        // would still be green while every loop was back in the interactive lane.
        var budget = TestBudget.Create();
        Assert.False(budget.IsBackgroundCall);

        using (ServerBudget.EnterBackground())
            Assert.True(budget.IsBackgroundCall);

        Assert.False(budget.IsBackgroundCall);
    }

    [Fact]
    public async Task The_flag_survives_an_await_because_that_is_the_whole_point()
    {
        // The loops set the scope once and then await for the process lifetime. A flag that did not flow
        // across awaits would be set in ExecuteAsync and gone by the first Docker call.
        var budget = TestBudget.Create();

        using (ServerBudget.EnterBackground())
        {
            await Task.Yield();
            await Task.Delay(1);
            Assert.True(budget.IsBackgroundCall);
        }

        Assert.False(budget.IsBackgroundCall);
    }

    // ---- the lane has to be wide enough for everyone now in it -----------------------------------------

    [Fact]
    public void The_background_lane_carries_every_loop_not_just_the_two_that_used_to_be_in_it()
    {
        // Four slots were sized when two loops used this lane and ten sat in the interactive one — eight
        // slots across two queues in practice. Moving all twelve here without widening it halved the fleet's
        // background capacity, and every server's circuit opened within four seconds of the next restart.
        var settings = new Whiskers.Configuration.ServerBudgetSettings();

        Assert.True(settings.BackgroundConcurrency >= 8,
            $"twelve loops share this lane; {settings.BackgroundConcurrency} slots is less than the fleet " +
            "effectively had before they were consolidated into it");
    }

    // ---- and they must not all start in the same second ------------------------------------------------

    [Fact]
    public void The_loops_do_not_all_start_at_once()
    {
        // THE assertion for the startup burst. Identical offsets are the bug; the work is the same either
        // way, only the pile-up is avoidable.
        var offsets = HostedLoops()
            .Where(t => typeof(FleetBackgroundService).IsAssignableFrom(t))
            .Select(FleetBackgroundService.StartupOffset)
            .ToList();

        Assert.True(offsets.Distinct().Count() >= offsets.Count - 1,
            "the loops must not share a startup offset — that is the pile-up this exists to prevent");
        Assert.True(offsets.Max() - offsets.Min() > TimeSpan.FromSeconds(5),
            $"offsets span only {(offsets.Max() - offsets.Min()).TotalSeconds:F1}s — too tight to spread the burst");
    }

    [Fact]
    public void No_loop_is_delayed_beyond_the_spread()
    {
        // A stagger that pushes a loop minutes out would trade a noisy restart for a monitor that is simply
        // not running yet — the exact confusion the whole self-protection strand exists to remove.
        foreach (var type in HostedLoops().Where(t => typeof(FleetBackgroundService).IsAssignableFrom(t)))
        {
            var offset = FleetBackgroundService.StartupOffset(type);
            Assert.InRange(offset, TimeSpan.Zero, FleetBackgroundService.StartupSpread);
        }
    }

    [Fact]
    public void The_same_loop_always_gets_the_same_offset()
    {
        // Deterministic on purpose: a staggered start that shuffles itself makes every startup problem a
        // different startup problem. string.GetHashCode() would have done exactly that — it is randomised
        // per process.
        var metrics = Assert.Single(HostedLoops(), t => t.Name == "MetricsCollectorService");
        var first = FleetBackgroundService.StartupOffset(metrics);
        var again = FleetBackgroundService.StartupOffset(metrics);

        Assert.Equal(first, again);
    }
}
