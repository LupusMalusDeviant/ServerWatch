namespace Whiskers.Models;

/// <summary>
/// One minute's reading of how a background loop is doing against one server (Plan-0003 WP3.2).
///
/// <para>Two reasons this is on disk and not only in memory. The obvious one: the numbers survive a restart,
/// so "was this loop already struggling yesterday?" is answerable without an external time-series database.</para>
///
/// <para>The less obvious one matters more. After a restart the in-memory view is empty, and an empty
/// <c>LastSuccess</c> is indistinguishable from a loop that has never succeeded — so the supervisory rule
/// would either alarm on every restart or have to ignore fresh loops entirely, and both are wrong. Restoring
/// the last known success from here means a restart neither invents a problem nor hides one.</para>
/// </summary>
public class SelfMetricSampleEntity
{
    public long Id { get; set; }

    /// <summary>When this reading was taken. UTC, like every timestamp here — a mixed-zone column produces
    /// timelines that suggest relationships that do not exist.</summary>
    public DateTime TakenAtUtc { get; set; }

    /// <summary>The loop's name, e.g. <c>logmonitor</c>.</summary>
    public string Loop { get; set; } = string.Empty;

    public string ServerId { get; set; } = string.Empty;

    /// <summary>When that loop last completed a cycle for that server. Null means it never has — which is a
    /// real state and not the same as zero.</summary>
    public DateTime? LastSuccessUtc { get; set; }

    /// <summary>How long the last cycle took, in milliseconds.</summary>
    public double LastDurationMs { get; set; }

    public long Cycles { get; set; }

    public long Failures { get; set; }

    public long Skips { get; set; }

    /// <summary>Why the loop skipped this server, when it did. Kept so a skipped server can be told apart
    /// from a silent one long after the fact.</summary>
    public string? SkipReason { get; set; }

    /// <summary>The loop's own cadence in seconds, as it reported it. Stored rather than assumed: a
    /// retrospective judgement needs the interval that was in force at the time, not today's setting.</summary>
    public double? ExpectedIntervalSeconds { get; set; }
}
