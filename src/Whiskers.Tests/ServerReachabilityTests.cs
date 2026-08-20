using Whiskers.Models;
using Whiskers.Services.HealthMonitor;

namespace Whiskers.Tests;

/// <summary>A host that stops answering used to be silent everywhere: the dashboard said "unreachable",
/// but nothing was sent — and every container alert and log-alert rule covering that host quietly stopped
/// producing anything, which looks exactly like "all quiet". These tests pin the outage signal and the
/// state-keeping that depends on it.</summary>
public class ServerReachabilityTests
{
    private static ContainerInfo Container(string id, string name, string serverId, string serverName) =>
        new() { Id = id, Name = name, ServerId = serverId, ServerName = serverName };

    private static FleetContainerListing Listing(
        IEnumerable<ContainerInfo>? containers = null,
        IEnumerable<string>? responded = null,
        IEnumerable<FleetServerFailure>? failed = null) => new()
        {
            Containers = (containers ?? Array.Empty<ContainerInfo>()).ToList(),
            RespondedServerIds = (responded ?? Array.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase),
            FailedServers = (failed ?? Array.Empty<FleetServerFailure>()).ToList()
        };

    private static FleetServerFailure Down(string id, string name = "Rabenhof") =>
        new(id, name, "Connection failed");

    /// <summary>A tracker whose cold-start grace is already over: the server answered once, which is what
    /// the "warm" tests below are about. Cold start has its own section.</summary>
    private static ServerReachabilityTracker Tracker(int threshold, params string[] warmServers)
    {
        var tracker = new ServerReachabilityTracker(threshold, coldStartThreshold: 10);
        var known = warmServers.Length > 0 ? warmServers : new[] { "rabenhof", "burgcloud" };
        tracker.Evaluate(Listing(responded: known));   // one successful cycle = connections are up
        return tracker;
    }

    // --- the outage signal ------------------------------------------------------------------------------

    [Fact]
    public void One_failed_cycle_is_not_an_outage()
    {
        // A tunnel rebuild or a host slower than the 8s listing bound trips a single cycle. Alerting on
        // that would train everyone to ignore the alert.
        var tracker = Tracker(threshold: 2);
        Assert.Empty(tracker.Evaluate(Listing(failed: new[] { Down("rabenhof") })));
    }

    [Fact]
    public void Two_failed_cycles_raise_the_alarm_once()
    {
        var tracker = Tracker(threshold: 2);
        tracker.Evaluate(Listing(failed: new[] { Down("rabenhof") }));

        var evt = Assert.Single(tracker.Evaluate(Listing(failed: new[] { Down("rabenhof") })));
        Assert.Equal("server_unreachable", evt.EventType);
        Assert.Equal("rabenhof", evt.ServerId);
        Assert.Contains("Connection failed", evt.ImageInfo);
        Assert.Empty(evt.ContainerName); // server-level: never suppressed by a container mute

        // Still down on later cycles — one alert per outage, not one per minute.
        Assert.Empty(tracker.Evaluate(Listing(failed: new[] { Down("rabenhof") })));
    }

    [Fact]
    public void Recovery_is_reported_and_names_the_host()
    {
        var tracker = Tracker(threshold: 1);
        tracker.Evaluate(Listing(failed: new[] { Down("rabenhof", "Rabenhof (Hetzner)") }));

        var evt = Assert.Single(tracker.Evaluate(Listing(
            containers: new[] { Container("a", "web", "rabenhof", "Rabenhof (Hetzner)") },
            responded: new[] { "rabenhof" })));
        Assert.Equal("server_recovered", evt.EventType);
        Assert.Equal("Rabenhof (Hetzner)", evt.ServerName);
    }

    [Fact]
    public void A_blip_that_never_alerted_does_not_produce_a_recovery()
    {
        var tracker = Tracker(threshold: 2);
        tracker.Evaluate(Listing(failed: new[] { Down("rabenhof") }));       // 1 of 2 — no alert
        Assert.Empty(tracker.Evaluate(Listing(responded: new[] { "rabenhof" })));
    }

    [Fact]
    public void The_streak_resets_between_outages()
    {
        var tracker = Tracker(threshold: 2);
        tracker.Evaluate(Listing(failed: new[] { Down("rabenhof") }));
        tracker.Evaluate(Listing(responded: new[] { "rabenhof" }));
        // Second outage starts counting from scratch, so one failure is still not enough.
        Assert.Empty(tracker.Evaluate(Listing(failed: new[] { Down("rabenhof") })));
        Assert.Single(tracker.Evaluate(Listing(failed: new[] { Down("rabenhof") })));
    }

    [Fact]
    public void Each_server_is_tracked_on_its_own()
    {
        var tracker = Tracker(threshold: 1);
        var events = tracker.Evaluate(Listing(failed: new[] { Down("rabenhof"), Down("burgcloud", "BurgCloud") }));
        Assert.Equal(new[] { "burgcloud", "rabenhof" }, events.Select(e => e.ServerId).OrderBy(s => s).ToArray());
    }

    // --- state keeping ----------------------------------------------------------------------------------

    [Fact]
    public void State_of_a_silent_server_is_never_pruned()
    {
        // The whole point: an unreachable host returns an empty container list. Treating that as "these
        // containers are gone" throws away the watermarks and previous states we need on recovery.
        var listing = Listing(responded: new[] { "local" }, failed: new[] { Down("infomaniak") });

        Assert.True(listing.MayPruneStateFor("local"));
        Assert.False(listing.MayPruneStateFor("infomaniak"));
        Assert.False(listing.IsComplete);
    }

    [Fact]
    public void State_of_a_server_that_is_gone_from_the_fleet_may_be_pruned()
    {
        var listing = Listing(responded: new[] { "local" });
        Assert.True(listing.MayPruneStateFor("removed-server"));
        Assert.True(listing.IsComplete);
    }

    // --- cold start -------------------------------------------------------------------------------------

    [Fact]
    public void A_server_that_has_never_answered_is_not_reported_at_the_normal_threshold()
    {
        // Right after a restart the remote connections are not up yet. Measured on a six-server fleet, the
        // plain threshold produced ten pointless notifications per restart (5 down + 5 back up).
        var tracker = new ServerReachabilityTracker(threshold: 2, coldStartThreshold: 10);

        for (var cycle = 0; cycle < 9; cycle++)
            Assert.Empty(tracker.Evaluate(Listing(failed: new[] { Down("rabenhof") })));
    }

    [Fact]
    public void A_host_that_is_really_dead_at_startup_is_still_reported_eventually()
    {
        var tracker = new ServerReachabilityTracker(threshold: 2, coldStartThreshold: 3);
        tracker.Evaluate(Listing(failed: new[] { Down("rabenhof") }));
        tracker.Evaluate(Listing(failed: new[] { Down("rabenhof") }));

        Assert.Single(tracker.Evaluate(Listing(failed: new[] { Down("rabenhof") })));
    }

    [Fact]
    public void Once_a_server_has_answered_the_normal_threshold_applies()
    {
        var tracker = new ServerReachabilityTracker(threshold: 2, coldStartThreshold: 10);
        tracker.Evaluate(Listing(responded: new[] { "rabenhof" }));   // connections are up

        tracker.Evaluate(Listing(failed: new[] { Down("rabenhof") }));
        Assert.Single(tracker.Evaluate(Listing(failed: new[] { Down("rabenhof") })));
    }

    [Fact]
    public void The_grace_is_per_server_not_global()
    {
        var tracker = new ServerReachabilityTracker(threshold: 2, coldStartThreshold: 10);
        tracker.Evaluate(Listing(responded: new[] { "rabenhof" }));   // only this one is warm

        tracker.Evaluate(Listing(failed: new[] { Down("rabenhof"), Down("burgcloud", "BurgCloud") }));
        var events = tracker.Evaluate(Listing(failed: new[] { Down("rabenhof"), Down("burgcloud", "BurgCloud") }));

        Assert.Equal("rabenhof", Assert.Single(events).ServerId);
    }
}
