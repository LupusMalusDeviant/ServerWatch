namespace Whiskers.Services.Observability.Outcomes;

/// <summary>
/// The success criterion for every automatic action (Plan-0006 WP1.2/WP1.3).
///
/// <para><b>An action kind with no criterion must not run automatically.</b> That rule is enforced here
/// rather than written down as a convention: <see cref="For"/> throws, and a test walks every member of
/// <see cref="AutomaticActionKind"/> and fails on the first one this table has forgotten. Adding a new
/// automatic behaviour therefore forces the question "how would we know it worked?" before it ever runs — the
/// question the 2026-08-26 incident was never asked.</para>
///
/// <para>Metric names refer to series that already exist. A criterion that would need new instrumentation is
/// a criterion nobody can evaluate, and an unevaluatable promise is worse than none: it looks like a
/// control.</para>
/// </summary>
public static class ActionCriteria
{
    /// <summary>The metric names, so a typo cannot silently create an unevaluatable criterion.</summary>
    public static class Metrics
    {
        /// <summary>Percent of the whole machine, from <c>ServerMetrics</c>.</summary>
        public const string HostCpuPercent = "host.cpu.percent";

        /// <summary>Docker API response time on that server, in milliseconds (Plan-0004 WP4).</summary>
        public const string ApiLatencyMs = "host.api.latency.ms";

        /// <summary>1 when the container is running and not unhealthy, 0 otherwise.</summary>
        public const string ContainerUp = "container.up";

        /// <summary>Seconds since the loop last completed a cycle for that server (Plan-0003).</summary>
        public const string LoopSuccessAgeSeconds = "loop.last_success.age.seconds";
    }

    private static readonly IReadOnlyDictionary<AutomaticActionKind, ActionOutcomeCriterion> Table =
        new Dictionary<AutomaticActionKind, ActionOutcomeCriterion>
        {
            [AutomaticActionKind.ContainerRestart] = new(
                AutomaticActionKind.ContainerRestart, Metrics.ContainerUp, OutcomeDirection.Above, 1,
                TimeSpan.FromMinutes(5),
                "A restart worked if the container is running and not unhealthy five minutes later. Sooner "
                + "than that and a slow-starting service would be judged dead; much later and a container that "
                + "crash-loops every four minutes would pass."),

            [AutomaticActionKind.AutoUpdate] = new(
                AutomaticActionKind.AutoUpdate, Metrics.ContainerUp, OutcomeDirection.Above, 1,
                TimeSpan.FromMinutes(10),
                "An update worked if the container is still up ten minutes later. Longer than a restart "
                + "because a new image often has migrations or a cold cache to get through."),

            [AutomaticActionKind.SelfThrottle] = new(
                AutomaticActionKind.SelfThrottle, Metrics.ApiLatencyMs, OutcomeDirection.Below, 1000,
                TimeSpan.FromMinutes(15),
                "Whiskers throttled itself because a server stopped answering. It worked if the daemon is "
                + "answering in under a second again. If it is not, the load was never ours and the throttle "
                + "is just a blind spot we imposed on ourselves."),

            [AutomaticActionKind.LogScanLockout] = new(
                AutomaticActionKind.LogScanLockout, Metrics.ApiLatencyMs, OutcomeDirection.Below, 1000,
                TimeSpan.FromMinutes(15),
                "One container's logs were dropped from the scan to take load off the host. It worked if the "
                + "host is responsive again. If not, that container was not the problem and it is now "
                + "unmonitored for nothing."),

            [AutomaticActionKind.EmergencyStop] = new(
                AutomaticActionKind.EmergencyStop, Metrics.HostCpuPercent, OutcomeDirection.Below, 90,
                TimeSpan.FromMinutes(10),
                "Background checks were paused because Whiskers looked like the load. It worked if the host's "
                + "CPU came down. If it did not, Whiskers was not the cause — and the pause has taken the "
                + "monitoring away from a server that has a real problem."),

            [AutomaticActionKind.AgentWriteAction] = new(
                AutomaticActionKind.AgentWriteAction, Metrics.ContainerUp, OutcomeDirection.Above, 1,
                TimeSpan.FromMinutes(5),
                "The agent changed something. The weakest criterion here on purpose: the agent acts for many "
                + "reasons and 'the target is still up' is the only thing true of all of them. Treat a pass as "
                + "'nothing obviously broke', not as 'it achieved its goal'.")
        };

    /// <summary>The criterion for an action kind. Throws if there is none — see the class summary: an
    /// automatic action with no declared way to check it must not run.</summary>
    public static ActionOutcomeCriterion For(AutomaticActionKind kind) =>
        Table.TryGetValue(kind, out var criterion)
            ? criterion
            : throw new InvalidOperationException(
                $"No success criterion is declared for '{kind}'. An automatic action without one cannot be " +
                "checked, and an action nobody checks is a belief rather than a control (Plan-0006 WP1.3). " +
                "Add it to ActionCriteria.");

    public static IReadOnlyCollection<AutomaticActionKind> Declared => Table.Keys.ToList();
}
