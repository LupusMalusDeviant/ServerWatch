using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Whiskers.Models;
using Whiskers.Services.LogMonitor;
using Whiskers.Services.Persistence;

namespace Whiskers.Tests;

/// <summary>
/// The watermark ratchet from the 2026-08-26 incident (Plan-0002 WP1/WP2).
///
/// <para>The watermark was only written after a <em>successful</em> fetch. So on failure <c>since</c> stayed
/// put while <c>now</c> moved on, and the requested window grew by one cycle interval every time. The comment
/// in the code claimed "the next cycle retries the same window"; it was not the same window, and that
/// difference is the whole mechanism: failure → wider window → more expensive fetch → certain failure. Six
/// days without a single recovery.</para>
///
/// <para>Applying <c>since</c> costs dockerd the whole file — it decodes the JSON log from the start to find
/// the cut-off — so a wider window is genuinely more work even when it yields nothing.</para>
/// </summary>
public sealed class LogMonitorWatermarkTests : IDisposable
{
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromMilliseconds(50);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"logmon-wm-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _sp;

    public LogMonitorWatermarkTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MetricsDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        _sp = services.BuildServiceProvider();
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetricsDbContext>();
        db.Database.EnsureCreated();
        db.LogAlertRules.Add(new LogAlertRuleEntity { Name = "fatal", Pattern = "FATAL", Severity = "error" });
        db.SaveChanges();
    }

    public void Dispose()
    {
        _sp.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best-effort temp cleanup */ }
    }

    private LogMonitorService Monitor(FakeDocker docker, FakeNotifications notifications) =>
        new(_sp.GetRequiredService<IServiceScopeFactory>(),
            docker,
            new FakeServerConfig(new ServerConfig { Id = "local", Name = "Badwolf", ConnectionType = ConnectionType.Local, IsDefault = true }),
            notifications,
            NullLogger<LogMonitorService>.Instance,
            TestBudget.Create(),
            FetchTimeout);

    private static FakeDocker WedgedContainer() =>
        new(new ContainerInfo { Id = "c-tunnel", Name = "ghostunnel", Image = "img:1", ServerId = "local", ServerName = "Badwolf" })
        {
            FetchDelay = TimeSpan.FromMilliseconds(400)   // eight times the timeout: this fetch never succeeds
        };

    [Fact]
    public async Task A_failed_fetch_does_not_make_the_next_one_more_expensive()
    {
        // The container must SUCCEED once before it goes slow. That is the shape of the incident — a healthy
        // container that degraded — and it is also the only shape in which the ratchet exists at all: without
        // a successful fetch there is no watermark to leave behind, so `since` falls back to "now" every cycle
        // and the window stays flat whether the bug is present or not. An earlier version of this test skipped
        // the healthy cycle and therefore passed against the unfixed code, proving nothing.
        var docker = new FakeDocker(
            new ContainerInfo { Id = "c-tunnel", Name = "ghostunnel", Image = "img:1", ServerId = "local", ServerName = "Badwolf" });
        var monitor = Monitor(docker, new FakeNotifications());

        await monitor.RunScanCycleAsync(CancellationToken.None);   // healthy: sets the watermark
        docker.FetchDelay = TimeSpan.FromMilliseconds(400);        // and now it wedges

        // The gap between cycles has to be large enough that the ratchet would actually show. With a short
        // gap the window widens by that gap per cycle and any generous tolerance swallows it — an earlier
        // version used 120 ms and could not tell the two apart either. The real cycle interval is 60 s.
        const int gapMs = 400;
        var windows = new List<TimeSpan>();

        var seenCalls = docker.Calls.Count;
        for (var i = 0; i < 4; i++)
        {
            await Task.Delay(gapMs);
            var at = DateTime.UtcNow;
            await monitor.RunScanCycleAsync(CancellationToken.None);

            // Only measure cycles that actually issued a fetch. After the suspension kicks in there is no new
            // call, and reusing the previous one against a later clock would invent a growing window that the
            // code never asked for — the first version of this test did exactly that.
            if (docker.Calls.Count > seenCalls)
            {
                seenCalls = docker.Calls.Count;
                if (docker.CallsInOrder[^1].Since is { } since) windows.Add(at - since);
            }
        }

        Assert.True(windows.Count >= 3, $"expected at least 3 failing fetches, got {windows.Count}");

        // Every one of these follows a successful cycle, so each should ask for roughly one gap's worth of
        // logs. Growing means the watermark is being left behind on failure — the ratchet that turned a slow
        // container into a permanently failing one.
        var spread = windows.Max() - windows.Min();

        Assert.True(spread <= TimeSpan.FromMilliseconds(gapMs / 2),
            $"the requested window keeps growing across failures — windows were " +
            $"[{string.Join(", ", windows.Select(w => $"{w.TotalMilliseconds:F0}ms"))}]. " +
            "The ratchet is still there.");
    }

    [Fact]
    public async Task The_requested_window_never_exceeds_the_cap()
    {
        var docker = WedgedContainer();
        var monitor = Monitor(docker, new FakeNotifications());

        for (var i = 0; i < 5; i++)
        {
            await monitor.RunScanCycleAsync(CancellationToken.None);
            await Task.Delay(120);
        }

        // Whatever happens, a single fetch may never ask for more than the cap. Losing the lines in a longer
        // outage is the deliberate side of the trade: today they are lost anyway, just permanently.
        var widest = docker.CallsInOrder
            .Where(c => c.Since is not null)
            .Select(c => DateTime.UtcNow - c.Since!.Value)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max();

        Assert.True(widest <= LogMonitorService.MaxLookback + TimeSpan.FromSeconds(2),
            $"widest requested window was {widest.TotalMinutes:F1} min, cap is {LogMonitorService.MaxLookback.TotalMinutes:F0} min");
    }

    [Fact]
    public async Task A_container_whose_logs_stay_unreadable_is_reported_within_three_cycles()
    {
        // The earliest and most precise signal in the whole incident: the fetch timeouts were already being
        // logged, and nobody counted them. Counted, this fires after three minutes instead of six days.
        var docker = WedgedContainer();
        var notifications = new FakeNotifications();
        var monitor = Monitor(docker, notifications);

        for (var i = 0; i < 3; i++)
            await monitor.RunScanCycleAsync(CancellationToken.None);

        var alert = Assert.Single(notifications.Events);
        Assert.Equal("log_scan_suspended", alert.EventType);
        Assert.Equal("ghostunnel", alert.ContainerName);
    }

    [Fact]
    public async Task A_healthy_container_is_never_suspended()
    {
        var docker = new FakeDocker(
            new ContainerInfo { Id = "c-ok", Name = "burg-web", Image = "img:1", ServerId = "local", ServerName = "Badwolf" });
        var notifications = new FakeNotifications();
        var monitor = Monitor(docker, notifications);

        for (var i = 0; i < 5; i++)
            await monitor.RunScanCycleAsync(CancellationToken.None);

        Assert.DoesNotContain(notifications.Events, e => e.EventType == "log_scan_suspended");
    }

    [Fact]
    public async Task A_removed_container_is_not_reported_as_a_scan_problem()
    {
        // "Container is gone" and "container's logs are unreadable" are different facts. Counting the first
        // as the second would page someone every time a container is legitimately removed.
        var docker = new FakeDocker(
            new ContainerInfo { Id = "c-gone", Name = "old-job", Image = "img:1", ServerId = "local", ServerName = "Badwolf" });
        docker.FailingServerIds.Add("local");   // fetch throws immediately, no timeout involved
        var notifications = new FakeNotifications();
        var monitor = Monitor(docker, notifications);

        for (var i = 0; i < 5; i++)
            await monitor.RunScanCycleAsync(CancellationToken.None);

        Assert.DoesNotContain(notifications.Events, e => e.EventType == "log_scan_suspended");
    }
}
