using Whiskers.Services.Observability.Outcomes;

namespace Whiskers.Tests;

/// <summary>
/// The rule that an automatic action must declare how it can be checked (Plan-0006 WP1.3).
///
/// <para>Whiskers counts an action as successful when the call returned without an error — not when the
/// problem went away. That is the same confusion as the incident itself: the loop ran, so it must be working.
/// These tests make the question "how would we know it worked?" unavoidable at the moment a new automatic
/// behaviour is added, rather than after it has misfired.</para>
/// </summary>
public class ActionCriteriaTests
{
    [Fact]
    public void Every_automatic_action_has_a_declared_success_criterion()
    {
        // WP1.3, enforced rather than documented. A new member of AutomaticActionKind with no entry in the
        // table fails here — which is the whole mechanism: adding an automatic behaviour forces the question
        // before it can ever run.
        var missing = Enum.GetValues<AutomaticActionKind>()
            .Where(k => !ActionCriteria.Declared.Contains(k))
            .ToList();

        Assert.True(missing.Count == 0,
            "These automatic actions have no way to check whether they worked: " + string.Join(", ", missing) +
            ". An action nobody checks is a belief, not a control — add it to ActionCriteria.");
    }

    [Fact]
    public void Asking_for_an_undeclared_criterion_fails_loudly()
    {
        // The runtime half of the same rule. If the enum is ever bypassed — a cast, a deserialised value —
        // the answer must be an exception, never a default that quietly passes everything.
        var undeclared = (AutomaticActionKind)9999;

        var error = Assert.Throws<InvalidOperationException>(() => ActionCriteria.For(undeclared));

        Assert.Contains("belief rather than a control", error.Message);
    }

    [Fact]
    public void Every_criterion_names_a_metric_that_actually_exists()
    {
        // A criterion needing instrumentation nobody built is worse than none: it looks like a control while
        // being unevaluatable. The metric names are constants for exactly this reason.
        var known = typeof(ActionCriteria.Metrics)
            .GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var kind in ActionCriteria.Declared)
            Assert.Contains(ActionCriteria.For(kind).Metric, known);
    }

    [Fact]
    public void Every_criterion_explains_itself_in_prose()
    {
        // The threshold and the window are judgement calls. Six months from now the number alone will look
        // arbitrary and somebody will "clean it up" — the explanation is what makes that a decision rather
        // than a tidy-up.
        foreach (var kind in ActionCriteria.Declared)
        {
            var criterion = ActionCriteria.For(kind);
            Assert.True(criterion.Explanation.Length > 80,
                $"{kind} has no real explanation of its threshold and window.");
        }
    }

    [Fact]
    public void No_window_is_so_long_that_it_measures_time_instead_of_effect()
    {
        // From the plan's risk table. A window of hours does not check whether the action worked; it checks
        // whether anything at all changed in the meantime, which is nearly always true.
        foreach (var kind in ActionCriteria.Declared)
        {
            var criterion = ActionCriteria.For(kind);
            Assert.InRange(criterion.Window, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30));
        }
    }

    [Theory]
    [InlineData(OutcomeDirection.Below, 90, 85, true)]
    [InlineData(OutcomeDirection.Below, 90, 95, false)]
    [InlineData(OutcomeDirection.Above, 1, 1, true)]
    [InlineData(OutcomeDirection.Above, 1, 0, false)]
    public void The_direction_decides_which_way_counts_as_success(
        OutcomeDirection direction, double threshold, double value, bool expected)
    {
        var criterion = new ActionOutcomeCriterion(
            AutomaticActionKind.ContainerRestart, "x", direction, threshold, TimeSpan.FromMinutes(5), "test");

        Assert.Equal(expected, criterion.IsMet(value));
    }

    [Fact]
    public void The_self_throttle_criteria_admit_that_the_cause_may_be_elsewhere()
    {
        // The three self-throttles are the ones where a failed check means something specific and
        // uncomfortable: Whiskers took monitoring away from a server for nothing. The wording has to say so,
        // because the natural reading of "did not work" is "try harder".
        foreach (var kind in new[]
                 {
                     AutomaticActionKind.SelfThrottle,
                     AutomaticActionKind.LogScanLockout,
                     AutomaticActionKind.EmergencyStop
                 })
        {
            var explanation = ActionCriteria.For(kind).Explanation;
            Assert.True(
                explanation.Contains("was never ours") || explanation.Contains("not the problem")
                                                       || explanation.Contains("not the cause"),
                $"{kind} does not say what a failed check means: that the throttle was a blind spot for nothing.");
        }
    }
}
