namespace Whiskers.Services.Observability.Outcomes;

/// <summary>How much weight a hit rate can carry (Plan-0006 WP5.1).</summary>
public enum RateTrust
{
    /// <summary>Enough judged outcomes, few enough unmeasurable ones.</summary>
    Sound,

    /// <summary>Too few judged outcomes to mean anything yet. Not a bad rate — no rate.</summary>
    TooFew,

    /// <summary>As many outcomes could not be measured as could. The number on screen describes the checking,
    /// not the actions.</summary>
    Unreliable
}

/// <summary>One row of the "actions and their effect" table.</summary>
public sealed record OutcomeRow(
    string ActionKind, int Worked, int DidNotWork, int NotMeasurable, int Pending,
    double? HitRate, RateTrust Trust, string Caveat);

public static class OutcomePresenter
{
    /// <summary>Below this many judged outcomes a percentage is theatre. Three attempts and one failure is
    /// "67%", which reads as a measurement and is a coin toss.</summary>
    public const int MinimumForARate = 5;

    /// <summary>
    /// Turns the tallies into rows, and says for each whether its number can be believed.
    ///
    /// <para>The judgement here is not the percentage — that is arithmetic. It is whether the percentage
    /// deserves to be read at all. A hit rate computed over three attempts, or over a period when most
    /// outcomes could not be measured, looks exactly like one computed over a hundred clean ones, and the
    /// difference is the whole value of the table.</para>
    /// </summary>
    public static IReadOnlyList<OutcomeRow> Rows(IReadOnlyList<OutcomeTally> tallies)
        => tallies.Select(t =>
        {
            var trust = Judge(t);
            return new OutcomeRow(
                t.ActionKind, t.Worked, t.DidNotWork, t.NotMeasurable, t.Pending,
                trust == RateTrust.TooFew ? null : t.HitRate,
                trust,
                Caveat(t, trust));
        })
        // Worst first: an action kind that mostly changes nothing is the one worth looking at, and an
        // unreliable rate is worth looking at before a good one.
        .OrderBy(r => r.Trust == RateTrust.Sound)
        .ThenBy(r => r.HitRate ?? 2)
        .ThenBy(r => r.ActionKind, StringComparer.Ordinal)
        .ToList();

    private static RateTrust Judge(OutcomeTally tally)
    {
        // Unmeasurable outnumbering measured means the checking has broken down, and that has to be said
        // before any percentage is shown — otherwise a rate computed from the handful that did work reads as
        // a fact about all of them.
        if (tally.NotMeasurable > 0 && tally.NotMeasurable >= tally.Judged) return RateTrust.Unreliable;
        if (tally.Judged < MinimumForARate) return RateTrust.TooFew;
        return RateTrust.Sound;
    }

    private static string Caveat(OutcomeTally tally, RateTrust trust) => trust switch
    {
        RateTrust.TooFew =>
            $"Only {tally.Judged} outcome(s) judged so far — too few for a rate to mean anything.",

        RateTrust.Unreliable =>
            $"{tally.NotMeasurable} outcome(s) could not be measured against {tally.Judged} that could. " +
            "This number describes the checking, not the actions.",

        _ when tally.NotMeasurable > 0 =>
            $"{tally.NotMeasurable} outcome(s) could not be measured and are not in the rate.",

        _ => ""
    };

    /// <summary>What to say about windows that came due and were never judged (Plan-0006 WP5.3).
    /// Empty when there are none — a permanent "0 overdue" line is one more thing to stop reading.</summary>
    public static string OverdueWarning(int overdue) => overdue switch
    {
        <= 0 => "",
        < 10 => $"{overdue} check window(s) came due and have not been judged yet.",
        _ => $"{overdue} check windows came due and were never judged. If this keeps climbing the outcome " +
             "sweep has stopped running — which looks exactly like a fleet with nothing to check."
    };
}
