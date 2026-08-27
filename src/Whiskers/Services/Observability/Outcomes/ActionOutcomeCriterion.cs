namespace Whiskers.Services.Observability.Outcomes;

/// <summary>Every kind of thing Whiskers does on its own (Plan-0006 WP1.2).
///
/// <para>An enum rather than a string so the "no action without a criterion" rule can be enforced by walking
/// the type: a new member that nobody gave a criterion to breaks the build's test run, which is the point of
/// WP1.3. A string would have let one slip through silently.</para></summary>
public enum AutomaticActionKind
{
    /// <summary>A container was restarted because it looked unhealthy.</summary>
    ContainerRestart,

    /// <summary>An image was updated automatically.</summary>
    AutoUpdate,

    /// <summary>Whiskers throttled itself against one server — the circuit opened (SP-1).</summary>
    SelfThrottle,

    /// <summary>One container's logs stopped being read after repeated timeouts (SP-2).</summary>
    LogScanLockout,

    /// <summary>Background checks for a server were paused (SP-5).</summary>
    EmergencyStop,

    /// <summary>The agent did something with a write effect.</summary>
    AgentWriteAction
}

/// <summary>Which way a metric has to move for the action to have worked.</summary>
public enum OutcomeDirection
{
    /// <summary>Success means the metric ends up below the threshold.</summary>
    Below,

    /// <summary>Success means the metric ends up at or above it.</summary>
    Above
}

/// <summary>
/// What an action promised to achieve, written down <em>before</em> it runs (Plan-0006 WP1.1).
///
/// <para>The point of declaring it in advance is that afterwards there is nothing left to interpret. Whiskers
/// currently counts an action as successful when the call returned without an error — not when the problem
/// went away. That is the same confusion as the incident itself, one level up: the loop ran, so it must be
/// working.</para>
/// </summary>
/// <param name="Metric">The series to read afterwards. Deliberately one of the series that already exists —
/// a criterion that needs new instrumentation is a criterion nobody will be able to evaluate.</param>
/// <param name="Window">How long to wait before judging. Per action kind, because the honest wait for a
/// container restart is not the honest wait for an image update — and a window that is too long measures the
/// passage of time rather than the effect of the action.</param>
public sealed record ActionOutcomeCriterion(
    AutomaticActionKind Kind,
    string Metric,
    OutcomeDirection Direction,
    double Threshold,
    TimeSpan Window,
    string Explanation)
{
    public bool IsMet(double value) => Direction == OutcomeDirection.Below
        ? value < Threshold
        : value >= Threshold;
}
