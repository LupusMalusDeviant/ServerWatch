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
}
