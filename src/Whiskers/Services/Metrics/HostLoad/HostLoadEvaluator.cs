namespace Whiskers.Services.Metrics.HostLoad;

/// <summary>What the evaluator decided about one sample.</summary>
/// <param name="AtUtc">The time of the <em>sample</em>, not of the evaluation. That distinction is what makes
/// a replay meaningful: a six-day series pushed through in a second must still report 20 August, 14:17.</param>
public sealed record HostLoadFinding(
    DateTime AtUtc, string ServerId, string ServerName, string Kind, string Summary, double Value, double Threshold);

/// <summary>Thresholds for the host-level rules. Defaults are aimed at the 2-core machines in this fleet.</summary>
public sealed class HostLoadThresholds
{
    /// <summary>Percent of the whole machine.</summary>
    public double CpuPercent { get; init; } = 90;

    public double MemoryPercent { get; init; } = 90;

    /// <summary>How much host CPU may go unexplained by containers before it is worth saying so. Set well
    /// above the noise between two samples taken moments apart, and below the incident's own 86 points.</summary>
    public double UnexplainedCpuPercent { get; init; } = 40;

    /// <summary>How long a breach must last before it is reported. A build peak is not an incident; six days
    /// at 98% is. Ten minutes is short enough to have caught 2026-08-26 by 14:17.</summary>
    public TimeSpan SustainedFor { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How long before the same open finding is repeated. It escalates rather than repeats — the
    /// point of WP5.1 is that a signal people learn to filter protects nothing.</summary>
    public TimeSpan RepeatAfter { get; init; } = TimeSpan.FromHours(6);
}

/// <summary>
/// The rules that would have caught the 2026-08-26 incident (Plan-0004 WP1/WP2).
///
/// <para>Whiskers recorded roughly 8,900 measurements of BurgCloud over six days, practically every one of
/// them above 98%, and evaluated none of them — because <c>EvaluateAlertsAsync</c> judges containers, and
/// <c>dockerd</c> runs in no container. The load fell through the gap between "per container" and "disk".</para>
///
/// <para><b>Driven by sample time, never by the wall clock.</b> Everything here advances on the timestamp in
/// the sample, which is what lets a recorded or reconstructed week be pushed through in under a second and
/// still produce findings dated correctly. A rule that consults <c>DateTime.UtcNow</c> cannot be replayed,
/// and a rule that cannot be replayed cannot be shown to catch the incident it was written for.</para>
/// </summary>
public sealed class HostLoadEvaluator
{
    private sealed class Breach
    {
        public DateTime? Since;
        public DateTime? Reported;
    }

    private readonly Dictionary<string, Breach> _breaches = new(StringComparer.Ordinal);
    private readonly HostLoadThresholds _thresholds;

    public HostLoadEvaluator(HostLoadThresholds? thresholds = null)
        => _thresholds = thresholds ?? new HostLoadThresholds();

    /// <summary>Judges one sample and returns whatever became reportable at that moment. Usually empty.</summary>
    public IReadOnlyList<HostLoadFinding> Evaluate(HostSample sample)
    {
        var findings = new List<HostLoadFinding>();

        Consider(findings, sample, "host_cpu_high", sample.HostCpuPercent, _thresholds.CpuPercent,
            v => $"{sample.ServerName} has been at {v:F0}% CPU for over {_thresholds.SustainedFor.TotalMinutes:F0} " +
                 $"minutes (threshold {_thresholds.CpuPercent:F0}%). This is the whole machine, not one container.");

        Consider(findings, sample, "host_memory_high", sample.MemoryPercent, _thresholds.MemoryPercent,
            v => $"{sample.ServerName} has been at {v:F0}% memory for over {_thresholds.SustainedFor.TotalMinutes:F0} " +
                 $"minutes (threshold {_thresholds.MemoryPercent:F0}%).");

        // The specific one. It names the class of cause rather than only the symptom, which is what turns
        // "the server is busy" into "something outside the containers is busy" — the exact fact that took six
        // days to establish by hand.
        Consider(findings, sample, "host_cpu_unexplained", sample.UnexplainedCpuPercent, _thresholds.UnexplainedCpuPercent,
            v => $"{sample.ServerName} is using {sample.HostCpuPercent:F0}% CPU while its containers together " +
                 $"account for only {sample.ContainerCpuPercentOfMachine:F0}% — {v:F0} points unexplained. " +
                 "A host process is the likely cause; dockerd, a backup job and a runaway service all look like " +
                 "this. Short-lived containers are missing from the sum, so treat this as a strong hint rather " +
                 "than proof.");

        return findings;
    }

    private void Consider(
        List<HostLoadFinding> findings, HostSample sample, string kind, double value, double threshold,
        Func<double, string> describe)
    {
        var key = $"{sample.ServerId}|{kind}";
        var breach = _breaches.TryGetValue(key, out var existing) ? existing : _breaches[key] = new Breach();

        if (value < threshold)
        {
            // Recovered. Clearing the report marker too means the NEXT breach is announced immediately rather
            // than being swallowed by the repeat window — a server that flaps in and out must not go quiet.
            breach.Since = null;
            breach.Reported = null;
            return;
        }

        breach.Since ??= sample.AtUtc;
        if (sample.AtUtc - breach.Since < _thresholds.SustainedFor) return;

        if (breach.Reported is { } last && sample.AtUtc - last < _thresholds.RepeatAfter) return;

        breach.Reported = sample.AtUtc;
        findings.Add(new HostLoadFinding(
            sample.AtUtc, sample.ServerId, sample.ServerName, kind, describe(value), value, threshold));
    }
}
