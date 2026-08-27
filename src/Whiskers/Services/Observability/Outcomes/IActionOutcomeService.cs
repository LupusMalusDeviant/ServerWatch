using Whiskers.Models;

namespace Whiskers.Services.Observability.Outcomes;

/// <summary>How often each action kind actually achieved anything (Plan-0006 WP5.1).</summary>
/// <param name="NotMeasurable">Counted separately and never folded into the others. A share that climbs
/// means the checking has stopped working, which looks identical to everything being fine.</param>
public sealed record OutcomeTally(
    string ActionKind, int Worked, int DidNotWork, int NotMeasurable, int Pending)
{
    public int Judged => Worked + DidNotWork;

    /// <summary>Null when nothing has been judged yet — deliberately not zero, which would read as "never
    /// works" for an action that has simply not been tried.</summary>
    public double? HitRate => Judged > 0 ? (double)Worked / Judged : null;
}

/// <summary>
/// Records what Whiskers did automatically, and later whether it helped (Plan-0006).
///
/// <para>Today an action counts as successful when the call returned without an error — not when the problem
/// went away. That is the incident's own confusion one level up: the loop ran, so it must be working.</para>
/// </summary>
public interface IActionOutcomeService
{
    /// <summary>Records an automatic action and schedules its check. Returns the correlation id that ties
    /// trigger, action and outcome together.
    ///
    /// <para>Throws if the action kind has no declared criterion — see <see cref="ActionCriteria"/>. That is
    /// deliberate and it is the enforcement point for WP1.3: an automatic action nobody can check must not
    /// reach the point of being recorded as done.</para></summary>
    Task<string> RecordAsync(
        AutomaticActionKind kind, string serverId, string targetId, string targetName,
        string? reason = null, string? correlationId = null, CancellationToken ct = default);

    /// <summary>Judges every window that has come due. Returns what it decided.</summary>
    Task<IReadOnlyList<ActionOutcomeEntity>> EvaluateDueAsync(DateTime nowUtc, CancellationToken ct = default);

    Task<IReadOnlyList<OutcomeTally>> TalliesAsync(DateTime sinceUtc, CancellationToken ct = default);

    /// <summary>Windows that are due but still unjudged. A list that only grows means the sweep has stopped
    /// (Plan-0006 WP5.3).</summary>
    Task<int> OverdueCountAsync(DateTime nowUtc, CancellationToken ct = default);
}
