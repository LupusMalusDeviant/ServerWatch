namespace Whiskers.Services.Metrics.HostLoad;

/// <summary>
/// "This value is over the line" → at most one open finding, escalated when it worsens, closed when it
/// really recovers (Plan-0004 WP5).
///
/// <para>Extracted rather than copied. The rules here are small but easy to get subtly wrong — the
/// confirmation window, the clear margin, measuring the outage to its end rather than to its confirmation —
/// and two of those were already wrong once. A second implementation for the latency rule would have been a
/// second chance to get them wrong differently, in a place nobody would think to compare.</para>
/// </summary>
public sealed class BreachTracker
{
    private sealed class Breach
    {
        public DateTime? Since;              // when the value first went over
        public DateTime? Reported;           // when it was last said out loud (null = never)
        public double ReportedValue;         // what was said, so escalation can be measured against it
        public DateTime? BelowSince;         // when it came back under the clear line
        public double Peak;
    }

    private readonly Dictionary<string, Breach> _breaches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _serverNames = new(StringComparer.Ordinal);
    private readonly HostLoadThresholds _thresholds;

    public BreachTracker(HostLoadThresholds thresholds) => _thresholds = thresholds;

    /// <summary>Judges one reading. Returns a finding only at the moments something changed: first report,
    /// escalation, all-clear. Every other sample returns null, which is the normal case.</summary>
    public HostLoadFinding? Consider(
        DateTime atUtc, string serverId, string serverName, string kind, double value, double threshold,
        Func<double, TimeSpan, string> describeBreach,
        Func<double, string> describeRecovery)
    {
        _serverNames[serverId] = serverName;
        var key = $"{serverId}|{kind}";
        var breach = _breaches.TryGetValue(key, out var existing) ? existing : _breaches[key] = new Breach();

        if (value < threshold)
            return BelowThreshold(atUtc, serverId, serverName, kind, value, threshold, breach, describeRecovery);

        // Over the line again — any recovery in progress is cancelled.
        breach.BelowSince = null;
        breach.Since ??= atUtc;
        breach.Peak = Math.Max(breach.Peak, value);

        var openFor = atUtc - breach.Since.Value;
        if (openFor < _thresholds.SustainedFor) return null;

        if (breach.Reported is null)
        {
            breach.Reported = atUtc;
            breach.ReportedValue = value;
            return new HostLoadFinding(
                atUtc, serverId, serverName, kind, FindingKind.Raised,
                describeBreach(value, openFor), value, threshold, openFor);
        }

        // Already open. It is only worth saying again if it got materially worse — otherwise the operator
        // learns nothing they do not already know, and learns to skip the next one.
        if (value - breach.ReportedValue < _thresholds.EscalationStep) return null;

        breach.Reported = atUtc;
        breach.ReportedValue = value;
        return new HostLoadFinding(
            atUtc, serverId, serverName, kind, FindingKind.Escalated,
            "Getting worse — " + describeBreach(value, openFor), value, threshold, openFor);
    }

    private HostLoadFinding? BelowThreshold(
        DateTime atUtc, string serverId, string serverName, string kind, double value, double threshold,
        Breach breach, Func<double, string> describeRecovery)
    {
        // Not yet clearly below. Between the threshold and the clear line nothing is decided: the breach
        // stays open and no all-clear is given, which is what stops a value hovering at the threshold from
        // flapping between alert and all-clear until somebody mutes the channel.
        if (value > threshold - _thresholds.ClearMargin)
        {
            breach.BelowSince = null;
            return null;
        }

        breach.BelowSince ??= atUtc;
        if (atUtc - breach.BelowSince < _thresholds.ClearedFor) return null;

        var wasReported = breach.Reported;
        var openedAt = breach.Since;
        // The breach ended when the value dropped, not when we finished confirming it. Reporting the
        // confirmation window as part of the outage would overstate every incident by five minutes — small,
        // but it is the kind of drift that makes people stop trusting the numbers in a post-mortem.
        var endedAt = breach.BelowSince ?? atUtc;

        breach.Since = null;
        breach.Reported = null;
        breach.BelowSince = null;
        breach.Peak = 0;

        // An all-clear only for something that was actually announced. A breach that resolved before it was
        // ever reported needs no closing message — sending one would mean the first thing the operator hears
        // about a problem is that it is over.
        if (wasReported is null) return null;

        var openFor = openedAt is { } start ? endedAt - start : TimeSpan.Zero;
        return new HostLoadFinding(
            atUtc, serverId, serverName, kind, FindingKind.Cleared,
            describeRecovery(value) + $" It had been over the threshold for {Describe(openFor)}.",
            value, threshold, openFor);
    }

    /// <summary>Findings that were raised and never closed, oldest first (Plan-0004 WP5.4).</summary>
    public IReadOnlyList<OpenFinding> OpenFindings()
        => _breaches
            .Where(kv => kv.Value.Reported is not null)
            .Select(kv =>
            {
                var parts = kv.Key.Split('|', 2);
                return new OpenFinding(
                    parts[0], _serverNames.GetValueOrDefault(parts[0], parts[0]),
                    parts.Length > 1 ? parts[1] : string.Empty,
                    kv.Value.Since ?? kv.Value.Reported!.Value,
                    kv.Value.Peak);
            })
            .OrderBy(f => f.SinceUtc)
            .ToList();

    public static string Describe(TimeSpan span) => span switch
    {
        { TotalMinutes: < 90 } => $"{span.TotalMinutes:F0} minutes",
        { TotalHours: < 48 } => $"{span.TotalHours:F0} hours",
        _ => $"{span.TotalDays:F0} days"
    };
}
