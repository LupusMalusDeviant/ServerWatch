using Whiskers.Services.Docker.Budget;

namespace Whiskers.Services.Observability.SelfMetrics;

/// <summary>What a loop's row on the self-status page says.</summary>
public enum LoopVerdict
{
    /// <summary>Running, and recently enough that its silence can be trusted.</summary>
    Healthy,

    /// <summary>Deliberately not covering this server. Not a fault — but it must still be shown, or "not
    /// covered" and "nothing found" look identical.</summary>
    Skipped,

    /// <summary>No cadence declared, so there is no basis for a verdict. Shown, never judged.</summary>
    Unjudged,

    /// <summary>Last success is older than three of the loop's own intervals.</summary>
    Stalled
}

/// <summary>One row of the loop table.</summary>
/// <param name="Age">How long ago the loop last succeeded. Deliberately an age and not a timestamp: "14:02"
/// requires the reader to do the subtraction, and doing it wrong is exactly how a six-day-old failure goes
/// unnoticed. Null means it has never succeeded.</param>
public sealed record LoopRow(
    string Loop,
    string ServerId,
    string ServerName,
    LoopVerdict Verdict,
    TimeSpan? Age,
    TimeSpan LastDuration,
    long Cycles,
    long Failures,
    long Skips,
    string? SkipReason,
    TimeSpan? ExpectedInterval);

/// <summary>One row of the per-server table.</summary>
public sealed record ServerLoadRow(
    string ServerId,
    string ServerName,
    ServerCircuitState Circuit,
    int BackgroundInFlight,
    int BackgroundLimit,
    int InteractiveInFlight,
    int InteractiveLimit,
    bool Paused,
    string? PauseReason);

/// <summary>A container whose logs are not being read, and why.</summary>
/// <param name="IsFault">True when the scan gave up after repeated timeouts (a fault it will retry), false
/// when the container is deliberately excluded. Both look identical from outside — no findings — and mean
/// opposite things, so they must never share a label.</param>
public sealed record UnreadContainerRow(string Container, string ServerName, bool IsFault, string Detail);

/// <summary>
/// Turns the self-metrics into rows a page can render (Plan-0003 WP4).
///
/// <para>This is separate from the page on purpose. Deciding that a loop has stalled is a judgement, not
/// formatting — it uses the same three-interval rule as <see cref="ScanSupervisor"/> and the MCP tool, so the
/// page, the alert and the agent can never disagree about what "fine" means. A view that quietly used a
/// different threshold would be worse than no view: it would look authoritative while contradicting the
/// alert that woke someone up.</para>
/// </summary>
public static class SelfStatusPresenter
{
    /// <summary>Read from the supervisor rather than copied. The supervisor is the one that raises the alert,
    /// so it owns the definition of "too long"; a second constant here would be free to drift, and the day it
    /// did, this page would say "fine" about the loop that had just paged someone.</summary>
    public const int IntervalsBeforeStalled = ScanSupervisor.IntervalsBeforeAlarm;

    public static IReadOnlyList<LoopRow> LoopRows(
        IReadOnlyList<LoopHealth> loops, DateTime now, Func<string, string> serverName)
    {
        var rows = new List<LoopRow>(loops.Count);

        foreach (var loop in loops)
        {
            // The age shown is the age of the last SUCCESS, never of the last attempt. A loop that tries every
            // minute and fails every minute has a fresh attempt and no success at all, and showing the attempt
            // would report it as healthy — the exact failure this page exists to make visible.
            var age = loop.LastSuccess is { } lastSuccess ? now - lastSuccess : (TimeSpan?)null;

            var verdict = Judge(loop, age);

            rows.Add(new LoopRow(
                loop.Loop, loop.ServerId, serverName(loop.ServerId), verdict, age,
                loop.LastDuration, loop.Cycles, loop.Failures, loop.Skips, loop.SkipReason, loop.ExpectedInterval));
        }

        // Worst first: the whole point of the page is answering "is anything wrong?" without reading it all.
        return rows
            .OrderByDescending(r => r.Verdict == LoopVerdict.Stalled)
            .ThenBy(r => r.Loop, StringComparer.Ordinal)
            .ThenBy(r => r.ServerName, StringComparer.Ordinal)
            .ToList();
    }

    private static LoopVerdict Judge(LoopHealth loop, TimeSpan? age)
    {
        // A skipped server is not a stalled one. It is explicitly not covered, which is a different fact and
        // needs a different colour.
        if (loop.SkipReason is not null) return LoopVerdict.Skipped;

        // No declared cadence, no verdict. Inventing a threshold would make this view a source of noise, and
        // a noisy status page is one people stop opening.
        if (loop.ExpectedInterval is not { } interval) return LoopVerdict.Unjudged;

        var allowed = interval * IntervalsBeforeStalled;

        // Never succeeded: judged by how many chances it has had, not by how recently it tried. Same rule as
        // ScanSupervisor. A freshly started process gets a few cycles before being called broken.
        if (age is null) return loop.Cycles >= IntervalsBeforeStalled ? LoopVerdict.Stalled : LoopVerdict.Healthy;

        return age > allowed ? LoopVerdict.Stalled : LoopVerdict.Healthy;
    }

    public static IReadOnlyList<ServerLoadRow> ServerRows(
        IReadOnlyList<Models.ServerConfig> servers,
        IServerBudget budget,
        IServerCircuitBreaker circuit,
        ILoopSuspensionService suspension)
    {
        var paused = suspension.Current().ToDictionary(p => p.ServerId, p => p.Reason, StringComparer.OrdinalIgnoreCase);

        return servers.Select(server =>
        {
            var lanes = budget.Snapshot(server.Id);
            return new ServerLoadRow(
                server.Id, server.Name,
                circuit.Snapshot(server.Id).State,
                lanes.BackgroundInFlight, lanes.BackgroundLimit,
                lanes.InteractiveInFlight, lanes.InteractiveLimit,
                paused.ContainsKey(server.Id),
                paused.GetValueOrDefault(server.Id));
        }).ToList();
    }

    /// <summary>The two ways a container can be missing from the log scan, merged into one list
    /// (Plan-0002 WP5 / Plan-0007 WP2.1).
    ///
    /// <para>They belong together because from outside they are indistinguishable — no findings — and they
    /// mean opposite things: one is a fault the scan is backing off from, the other is a deliberate
    /// exclusion. Shown apart, a reader has to know both pages exist to be sure a container is covered.
    /// Shown together under one honest label, "not being read" is answerable at a glance. Faults come first:
    /// an exclusion is a decision, a fault is a symptom.</para></summary>
    public static IReadOnlyList<UnreadContainerRow> UnreadContainers(
        IReadOnlyList<LogMonitor.SuspendedContainer> suspended,
        IReadOnlyList<LogMonitor.Hygiene.LogScanExclusion> exclusions,
        Func<string, string> serverName,
        DateTime now)
    {
        var rows = suspended.Select(s => new UnreadContainerRow(
            s.ContainerName, serverName(s.ServerId), IsFault: true,
            $"{s.ConsecutiveTimeouts} log fetches in a row timed out; retrying in {Age(s.Until - now).Replace(" ago", "")}."))
            .ToList();

        rows.AddRange(exclusions.Select(e => new UnreadContainerRow(
            e.ContainerName, serverName(e.ServerId), IsFault: false, e.Detail)));

        return rows
            .OrderByDescending(r => r.IsFault)
            .ThenBy(r => r.ServerName, StringComparer.Ordinal)
            .ThenBy(r => r.Container, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>An age in the shortest form that is still unambiguous. Rounded down deliberately: "3h" for
    /// something 3h59m old understates it, so the boundaries move up rather than down.</summary>
    public static string Age(TimeSpan? age)
    {
        // Invariant, not the request culture. This string also reaches the MCP tool and the alert text, where
        // a German decimal comma would be read by an agent parsing "2,5h" as something else entirely.
        var c = System.Globalization.CultureInfo.InvariantCulture;
        return age switch
        {
            null => "never",
            { TotalSeconds: < 60 } => age.Value.TotalSeconds.ToString("0", c) + "s ago",
            { TotalMinutes: < 60 } => age.Value.TotalMinutes.ToString("0", c) + "m ago",
            { TotalHours: < 48 } => age.Value.TotalHours.ToString("0.#", c) + "h ago",
            _ => age.Value.TotalDays.ToString("0.#", c) + "d ago"
        };
    }
}
