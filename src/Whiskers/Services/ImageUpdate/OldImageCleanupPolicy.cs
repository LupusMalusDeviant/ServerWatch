namespace Whiskers.Services.ImageUpdate;

/// <param name="MayDelete">Whether the old image may go.</param>
/// <param name="Reason">Why — in the words somebody would want to read in an audit log six weeks later.</param>
public sealed record ImageCleanupDecision(bool MayDelete, string Reason);

/// <summary>
/// Whether the image a container just moved off may be deleted (2026-08-28, user rule).
///
/// <para>The rule as given: after an update that reported success, the old image may go — except for
/// databases, for backup reasons. What follows adds the two things that rule does not say out loud.</para>
///
/// <para><b>An old image is the way back.</b> C12 records the previous image id so an update can be rolled
/// back; delete the image and the rollback entry points at nothing. That is not an argument against deleting,
/// it is an argument for waiting.</para>
///
/// <para><b>"Success" means started, not survived.</b> A container can come up cleanly and die twenty minutes
/// later on a newly required environment variable — one of the blind spots
/// <see cref="UpdateRiskAssessor"/> names in every assessment it produces. So the old image is kept for a
/// grace period, and only then dropped. A few dozen megabytes buy the way back for a day.</para>
/// </summary>
public static class OldImageCleanupPolicy
{
    /// <summary>How long an update has to have held before its predecessor is discarded. A day covers the
    /// failure modes that do not show up in the first minute — a nightly job, a scheduled task, the first
    /// request that takes an uncommon code path.</summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromHours(24);

    /// <param name="isDatabase">The user's exception, verbatim: databases keep their old image.</param>
    /// <param name="sinceUpdate">How long the new image has been running.</param>
    /// <param name="containerHealthy">Whether the container is up and, if it has a healthcheck, healthy.</param>
    /// <param name="imageStillInUse">Whether another container on the same host still runs this image.</param>
    public static ImageCleanupDecision Evaluate(
        bool isDatabase, TimeSpan sinceUpdate, bool containerHealthy, bool imageStillInUse)
    {
        // Docker would refuse anyway, but a policy that has to be rescued by the thing it is instructing is
        // not a policy.
        if (imageStillInUse)
            return new ImageCleanupDecision(false,
                "Another container on this host still runs this image. Deleting it would break something " +
                "that has nothing to do with this update.");

        if (isDatabase)
            return new ImageCleanupDecision(false,
                "This is a database. Its old image stays — restoring a backup written by the previous " +
                "version can mean needing the previous version to read it.");

        if (!containerHealthy)
            return new ImageCleanupDecision(false,
                "The container is not healthy on the new image. This is exactly when the old one is needed.");

        if (sinceUpdate < GracePeriod)
            return new ImageCleanupDecision(false,
                $"Only {Describe(sinceUpdate)} since the update — the grace period is " +
                $"{GracePeriod.TotalHours:0} hours. \"Started\" is not \"survived\": a missing environment " +
                "variable or a nightly job can still bring it down, and the way back would already be gone.");

        return new ImageCleanupDecision(true,
            $"Running healthily on the new image for {Describe(sinceUpdate)}, not a database, and no other " +
            "container uses the old image.");
    }

    private static string Describe(TimeSpan t) => t.TotalHours >= 1
        ? $"{t.TotalHours:0} hour(s)"
        : $"{t.TotalMinutes:0} minute(s)";
}
