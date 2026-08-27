using Whiskers.Models;

namespace Whiskers.Services.Observability.SelfMetrics;

/// <summary>One thing that happened, placed on the timeline.</summary>
/// <param name="AtUtc">Always UTC. Not "usually", not "unless the caller passed local" — the whole value of a
/// timeline is that two entries an hour apart really are an hour apart, and a single local-time entry mixed
/// into a UTC list invents a causal relationship that was never there.</param>
/// <param name="AuditId">The audit entry this came from, so a reader can open the full record. Null for
/// events that are not audited.</param>
public sealed record TimelineEntry(
    DateTime AtUtc,
    string Kind,
    string Summary,
    string? ServerId,
    string? Actor,
    bool Success,
    long? AuditId);

/// <summary>
/// "What happened at 14:02?" (Plan-0003 WP5).
///
/// <para>Self-inflicted load is the hardest kind to attribute, because the metric curve shows the effect and
/// nothing shows the cause. A deploy, a rule change, a restart, an agent action and a circuit opening are all
/// recorded already — just nowhere near the numbers they explain.</para>
///
/// <para><b>Everything here is UTC, and conversion happens only at the point of display.</b> The plan calls
/// this out and it is not pedantry: a timeline with a one-hour offset in it is worse than no timeline,
/// because it will confidently suggest that the thing which happened after the spike caused it.</para>
/// </summary>
public static class ActionTimeline
{
    /// <summary>Subjects whose actions can change how the fleet behaves. A login or a settings read cannot.</summary>
    private static readonly string[] InterestingPrefixes =
    {
        "container.", "server.", "deploy", "git-deploy", "compose", "agent.", "logalert.", "schedule.", "update."
    };

    /// <summary>Verbs that only look at something.
    ///
    /// <para>Excluding reads rather than listing writes is deliberate. An allow-list of write verbs would
    /// silently drop the next kind of intervention someone adds — and a timeline missing the action that
    /// caused the spike is worse than no timeline, because it looks complete. This way a new verb appears by
    /// default and the worst case is one line of noise.</para></summary>
    private static readonly string[] ReadOnlyVerbs =
    {
        "list", "get", "read", "view", "inspect", "logs", "stats", "search", "export", "download"
    };

    public static IReadOnlyList<TimelineEntry> Build(
        IEnumerable<AuditLogEntity> audit,
        IEnumerable<InAppNotification>? events,
        string? serverId,
        DateTime sinceUtc)
    {
        var entries = new List<TimelineEntry>();

        foreach (var a in audit)
        {
            if (Normalise(a.Timestamp) < sinceUtc) continue;
            if (serverId is not null && !string.Equals(a.ServerId, serverId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsInteresting(a.Action)) continue;

            entries.Add(new TimelineEntry(
                Normalise(a.Timestamp),
                a.Action,
                $"{a.Action} {a.TargetName}".Trim(),
                a.ServerId,
                string.IsNullOrWhiteSpace(a.Actor) ? null : $"{a.Actor} ({a.ActorType})",
                a.Success,
                a.Id));
        }

        // Stored notifications carry no server id — the column does not exist on NotificationEntity. So they
        // can only appear on the fleet-wide timeline; asking for one server's timeline and getting fleet-wide
        // events mixed in would attribute a pause on one host to a spike on another, which is exactly the
        // false relationship this view must not suggest. Narrowing to a server therefore drops them, and
        // says so on the page rather than quietly showing less.
        if (serverId is null)
        {
            foreach (var e in events ?? Enumerable.Empty<InAppNotification>())
            {
                if (Normalise(e.Timestamp) < sinceUtc) continue;
                if (!IsWhiskersOwnDoing(e.EventType)) continue;

                entries.Add(new TimelineEntry(
                    Normalise(e.Timestamp), e.EventType, e.Title, ServerId: null, "Whiskers", Success: true, AuditId: null));
            }
        }

        // Newest first: the question is almost always about something that just happened.
        return entries.OrderByDescending(e => e.AtUtc).ToList();
    }

    /// <summary>The events where Whiskers itself changed how it behaves. These are the ones that explain a
    /// change in the curve without anybody having touched the fleet — the case that is otherwise a mystery.</summary>
    private static bool IsWhiskersOwnDoing(string eventType) => eventType is
        "server_throttled" or "server_recovered_from_throttle" or
        "loops_paused" or "loops_resumed" or "loops_paused_reminder" or
        "log_scan_suspended" or "log_scan_resumed" or
        "monitoring_stalled" or "monitoring_resumed";

    private static bool IsInteresting(string action)
    {
        if (!InterestingPrefixes.Any(p => action.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return false;

        var verb = action.Contains('.') ? action[(action.LastIndexOf('.') + 1)..] : action;
        return !ReadOnlyVerbs.Contains(verb, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Forces a timestamp to UTC (Plan-0003 WP5.2).
    ///
    /// <para>A <see cref="DateTime"/> read back from a database usually comes out as <c>Unspecified</c>, and
    /// <c>Unspecified</c> silently means "local" the moment anything converts it. Assuming UTC here is correct
    /// because every writer in this codebase stores <c>DateTime.UtcNow</c> — but the assumption has to be
    /// explicit, because the failure is silent: entries drift by the host's offset, and the timeline then
    /// suggests that the thing which happened after the spike caused it.</para>
    /// </summary>
    public static DateTime Normalise(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
