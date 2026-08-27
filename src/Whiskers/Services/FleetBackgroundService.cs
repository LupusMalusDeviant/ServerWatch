using Whiskers.Services.Docker.Budget;

namespace Whiskers.Services;

/// <summary>
/// Base class for every loop that touches the fleet on its own schedule. Its whole job is to put that loop in
/// the background lane of the per-server budget — and to make forgetting impossible.
///
/// <para><b>Why this exists.</b> Plan-0001 gave each server two lanes: four concurrent calls for background
/// loops and four for anything a human is waiting on, kept apart so a long scan cannot freeze the UI. The lane
/// is chosen by an <c>AsyncLocal</c> flag that a caller has to set. On 2026-08-27 exactly two of twelve loops
/// set it. The other ten — metrics, health, CVE, image updates, auto-update, scheduler and four newer ones —
/// counted as interactive and queued in front of the dashboard. The metrics loop alone sweeps every server
/// every 30 seconds and takes 20 seconds to do it, so the interactive lane sat at 4 of 4 on all six servers at
/// once, waits reached five seconds, and calls that waited too long timed out and opened the circuit. The
/// result was a slow dashboard and a stream of "paused / resumed" notifications: the guard against overload
/// had become the source of it.</para>
///
/// <para><b>Why a base class and not a constructor parameter.</b> Ten loops would have needed a new dependency
/// each, and the eleventh — written next month — would still be free to skip it. Here the correct behaviour is
/// the default and <see cref="ExecuteAsync"/> is sealed, so a loop cannot quietly opt out.
/// <c>BackgroundServiceLaneTests</c> holds the line for anything that does not derive from this.</para>
///
/// <para><b>What it deliberately does not cover.</b> The scope wraps the hosted-service loop only. A method
/// that a loop shares with a manual trigger — <c>CveMonitorService.RunOneCycleAsync</c> is the live example —
/// stays interactive when a person calls it, which is right: they are waiting for it.</para>
/// </summary>
public abstract class FleetBackgroundService : BackgroundService
{
    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Set before the first await so it flows down the whole loop, including anything the loop starts.
        using var background = ServerBudget.EnterBackground();
        await RunAsync(stoppingToken);
    }

    /// <summary>The loop body, exactly as <c>ExecuteAsync</c> would have been written.</summary>
    protected abstract Task RunAsync(CancellationToken stoppingToken);
}
