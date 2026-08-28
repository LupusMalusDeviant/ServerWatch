using Whiskers.Services.ImageUpdate;

namespace Whiskers.Tests;

/// <summary>
/// When the image a container moved off may be deleted (2026-08-28, user rule).
///
/// <para>The rule as stated: after an update that reported success, the old image may go — except for
/// databases, for backup reasons. These tests pin that, and the two conditions the rule leaves implicit.</para>
///
/// <para>An old image is the way back: C12 records the previous image id so an update can be rolled back, and
/// deleting the image makes that entry point at nothing. And "success" means <em>started</em>, not
/// <em>survived</em> — a container can come up cleanly and die twenty minutes later on a newly required
/// environment variable, which is one of the blind spots every risk assessment already warns about. A day of
/// grace costs a few dozen megabytes and keeps the way back.</para>
/// </summary>
public class OldImageCleanupPolicyTests
{
    private static readonly TimeSpan WellPastGrace = OldImageCleanupPolicy.GracePeriod + TimeSpan.FromHours(1);

    [Fact]
    public void After_the_grace_period_a_healthy_non_database_may_drop_its_old_image()
    {
        var d = OldImageCleanupPolicy.Evaluate(
            isDatabase: false, WellPastGrace, containerHealthy: true, imageStillInUse: false);

        Assert.True(d.MayDelete);
        Assert.Contains("not a database", d.Reason);
    }

    [Fact]
    public void A_database_keeps_its_old_image_forever()
    {
        // The user's exception, and the reason is worth keeping in the text: restoring a backup written by
        // the previous version can mean needing the previous version to read it.
        var d = OldImageCleanupPolicy.Evaluate(
            isDatabase: true, WellPastGrace, containerHealthy: true, imageStillInUse: false);

        Assert.False(d.MayDelete);
        Assert.Contains("database", d.Reason);
        Assert.Contains("backup", d.Reason);
    }

    [Fact]
    public void Fresh_success_is_not_enough_because_started_is_not_survived()
    {
        // THE addition to the rule as given. Five minutes of uptime says the entrypoint works. It says
        // nothing about the nightly job, the first uncommon request, or a variable the image now requires.
        var d = OldImageCleanupPolicy.Evaluate(
            isDatabase: false, TimeSpan.FromMinutes(5), containerHealthy: true, imageStillInUse: false);

        Assert.False(d.MayDelete);
        Assert.Contains("\"Started\" is not \"survived\"", d.Reason);
    }

    [Fact]
    public void An_unhealthy_container_never_loses_its_way_back()
    {
        // Deleting the old image while the new one is failing removes the only thing that would fix it.
        var d = OldImageCleanupPolicy.Evaluate(
            isDatabase: false, WellPastGrace, containerHealthy: false, imageStillInUse: false);

        Assert.False(d.MayDelete);
        Assert.Contains("exactly when the old one is needed", d.Reason);
    }

    [Fact]
    public void An_image_another_container_still_runs_is_never_touched()
    {
        // Docker would refuse anyway. A policy that has to be rescued by the thing it instructs is not a
        // policy — and the failure would land on a container that had nothing to do with this update.
        var d = OldImageCleanupPolicy.Evaluate(
            isDatabase: false, WellPastGrace, containerHealthy: true, imageStillInUse: true);

        Assert.False(d.MayDelete);
        Assert.Contains("still runs this image", d.Reason);
    }

    [Fact]
    public void In_use_outranks_everything_including_a_perfect_case()
    {
        // Order matters: the checks are not independent opinions, they are a precedence.
        var d = OldImageCleanupPolicy.Evaluate(
            isDatabase: true, TimeSpan.FromSeconds(1), containerHealthy: false, imageStillInUse: true);

        Assert.False(d.MayDelete);
        Assert.Contains("still runs this image", d.Reason);
    }

    [Fact]
    public void Every_refusal_says_why_in_words_worth_reading_later()
    {
        // These reasons end up in an audit log. "false" is not an explanation six weeks after the fact.
        foreach (var d in new[]
                 {
                     OldImageCleanupPolicy.Evaluate(true, WellPastGrace, true, false),
                     OldImageCleanupPolicy.Evaluate(false, TimeSpan.Zero, true, false),
                     OldImageCleanupPolicy.Evaluate(false, WellPastGrace, false, false),
                     OldImageCleanupPolicy.Evaluate(false, WellPastGrace, true, true)
                 })
        {
            Assert.False(d.MayDelete);
            Assert.True(d.Reason.Length > 40, $"reason too thin to be useful: '{d.Reason}'");
        }
    }
}
