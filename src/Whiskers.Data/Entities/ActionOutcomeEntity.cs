namespace Whiskers.Models;

/// <summary>How an automatic action turned out (Plan-0006 WP2.3).</summary>
public enum ActionVerdict
{
    /// <summary>The window has not elapsed yet.</summary>
    Pending,

    /// <summary>The criterion was met.</summary>
    Worked,

    /// <summary>The criterion was not met. For Whiskers' own throttles this means something specific and
    /// uncomfortable: monitoring was taken away from a server for nothing.</summary>
    DidNotWork,

    /// <summary>
    /// There was no usable measurement — missing data, or Whiskers restarted inside the window.
    ///
    /// <para><b>Never to be folded into "worked".</b> That collapse is the exact shape of the 2026-08-26
    /// incident one level up: the absence of a signal read as the absence of a problem. It is a separate
    /// verdict, it is counted, and a rising share of it means the checking itself has stopped working.</para>
    /// </summary>
    NotMeasurable
}

/// <summary>
/// One automatic action and what came of it (Plan-0006 WP2.1).
///
/// <para>Persisted rather than held in memory because the window outlives the process: an action taken two
/// minutes before a restart would otherwise never be judged, and the actions most worth checking are exactly
/// the ones taken while something is going wrong.</para>
/// </summary>
public class ActionOutcomeEntity
{
    public long Id { get; set; }

    /// <summary>Ties trigger → action → outcome together (Plan-0006 WP2.3). Same identifier the governance
    /// chain already uses, so the whole story is one query.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>The <c>AutomaticActionKind</c> as a string — an enum stored as an int would silently retype
    /// every historical row the day somebody inserts a member in the middle.</summary>
    public string ActionKind { get; set; } = string.Empty;

    public string ServerId { get; set; } = string.Empty;

    /// <summary>What the action was aimed at: a container id, or the server id when it was server-wide.</summary>
    public string TargetId { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;

    public DateTime ExecutedAtUtc { get; set; }

    /// <summary>When the outcome may be judged — executed plus the criterion's window.</summary>
    public DateTime DueAtUtc { get; set; }

    public ActionVerdict Verdict { get; set; } = ActionVerdict.Pending;

    public DateTime? EvaluatedAtUtc { get; set; }

    /// <summary>What was measured and against what, in prose. The number alone is unreadable six months on.</summary>
    public string? Detail { get; set; }

    /// <summary>Why the action was taken. Kept alongside the verdict so a run of failures can be read without
    /// joining back to the alert that caused each one.</summary>
    public string? Reason { get; set; }
}
