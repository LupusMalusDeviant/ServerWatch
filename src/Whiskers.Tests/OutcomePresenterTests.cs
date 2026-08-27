using Whiskers.Services.Observability.Outcomes;

namespace Whiskers.Tests;

/// <summary>
/// Whether a hit rate deserves to be read (Plan-0006 WP5.1).
///
/// <para>The percentage itself is arithmetic. The judgement is whether it means anything — a rate from three
/// attempts, or from a period when most outcomes could not be measured, looks identical on screen to one
/// from a hundred clean ones. That difference is the entire value of the table, and these tests are about
/// it rather than about the number.</para>
/// </summary>
public class OutcomePresenterTests
{
    private static OutcomeTally Tally(
        string kind = "SelfThrottle", int worked = 0, int didNot = 0, int notMeasurable = 0, int pending = 0)
        => new(kind, worked, didNot, notMeasurable, pending);

    [Fact]
    public void A_rate_built_on_too_few_outcomes_is_not_shown_at_all()
    {
        // "67%" from three attempts reads as a measurement and is a coin toss. Showing nothing is the honest
        // answer; showing a number invites a decision it cannot support.
        var row = Assert.Single(OutcomePresenter.Rows([Tally(worked: 2, didNot: 1)]));

        Assert.Equal(RateTrust.TooFew, row.Trust);
        Assert.Null(row.HitRate);
        Assert.Contains("too few for a rate to mean anything", row.Caveat);
    }

    [Fact]
    public void A_rate_is_marked_unreliable_when_most_outcomes_could_not_be_measured()
    {
        // THE case this table exists for. Six of ten unmeasurable means the checking has broken down; a rate
        // computed from the four that worked would read as a fact about all ten.
        var row = Assert.Single(OutcomePresenter.Rows([Tally(worked: 4, didNot: 0, notMeasurable: 6)]));

        Assert.Equal(RateTrust.Unreliable, row.Trust);
        Assert.Contains("describes the checking, not the actions", row.Caveat);
    }

    [Fact]
    public void A_sound_rate_says_so_and_shows_the_number()
    {
        var row = Assert.Single(OutcomePresenter.Rows([Tally(worked: 8, didNot: 2)]));

        Assert.Equal(RateTrust.Sound, row.Trust);
        Assert.Equal(0.8, row.HitRate);
        Assert.Equal("", row.Caveat);
    }

    [Fact]
    public void Unmeasurable_outcomes_are_named_even_when_the_rate_is_sound()
    {
        // A handful of gaps does not invalidate the rate, but leaving them out entirely would make the table
        // claim more coverage than it has.
        var row = Assert.Single(OutcomePresenter.Rows([Tally(worked: 9, didNot: 1, notMeasurable: 2)]));

        Assert.Equal(RateTrust.Sound, row.Trust);
        Assert.Contains("could not be measured and are not in the rate", row.Caveat);
    }

    [Fact]
    public void Unmeasurable_outcomes_never_enter_the_rate_itself()
    {
        // The plan's central rule, at the presentation layer this time. Counting them as either outcome would
        // flatter or damn the action for something that was never observed.
        var row = Assert.Single(OutcomePresenter.Rows([Tally(worked: 5, didNot: 5, notMeasurable: 4)]));

        Assert.Equal(0.5, row.HitRate);   // 5 of 10 judged, not 5 of 14
    }

    [Fact]
    public void The_rows_that_need_attention_come_first()
    {
        // Somebody opening this page is asking "is any of this working?". An unreliable number and a poor
        // rate are both answers to that; a good one is not.
        var rows = OutcomePresenter.Rows([
            Tally("AaaFine", worked: 10),
            Tally("BbbPoor", worked: 2, didNot: 8),
            Tally("CccBlind", worked: 3, notMeasurable: 9)
        ]);

        Assert.Equal("CccBlind", rows[0].ActionKind);   // unreliable outranks everything
        Assert.Equal("BbbPoor", rows[1].ActionKind);
        Assert.Equal("AaaFine", rows[2].ActionKind);
    }

    [Fact]
    public void Nothing_overdue_produces_no_line_at_all()
    {
        // A permanent "0 overdue" is one more thing to stop reading, and the day it matters it will be
        // skipped along with the rest.
        Assert.Equal("", OutcomePresenter.OverdueWarning(0));
    }

    [Fact]
    public void A_large_overdue_backlog_says_what_it_actually_means()
    {
        // The number alone is not the point — a growing backlog means the sweep has stopped, and a checker
        // that has stopped checking looks exactly like a fleet with nothing to check.
        var warning = OutcomePresenter.OverdueWarning(40);

        Assert.Contains("the outcome sweep has stopped running", warning);
        Assert.Contains("nothing to check", warning);
    }
}
