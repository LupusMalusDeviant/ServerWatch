using Whiskers.Services.Metrics.HostLoad;

namespace Whiskers.Tests.TestData;

/// <summary>
/// The 2026-08-26 incident as a replayable series (Plan-0004 WP0).
///
/// <para><b>This is reconstructed, not recorded.</b> The real series lives in the database of the running
/// instance on Badwolf and was deliberately not exported: reading a production database and putting a week of
/// real infrastructure telemetry into a public repository are both decisions that were not mine to make. The
/// numbers below are the ones the incident report states, and nothing else — every constant carries the line
/// it came from.</para>
///
/// <para>What that buys and what it does not: the shape is right (the step, the plateau, the duration, the
/// two conventions for CPU), so a rule that misses this would certainly have missed the real thing. But real
/// data has texture this does not — daily cycles, backup peaks, the noise that produces false alarms. So
/// this proves a rule <b>fires</b>; it does not prove it stays quiet on a normal week. That second question
/// needs the real series, and it is still open.</para>
///
/// <para>Source: <c>docs/reviews/2026-08-26-logmonitor-dockerd-cpu-incident.md</c>.</para>
/// </summary>
public static class BurgCloudIncidentSeries
{
    // --- documented facts, one constant per stated value -------------------------------------------------

    /// <summary>"BurgCloud (Hetzner, 2 Kerne, Debian 12) — Docker 29.5.2, 10 Container" (report line 3).</summary>
    public const int CoreCount = 2;

    public const string ServerId = "burgcloud";
    public const string ServerName = "BurgCloud";

    /// <summary>"20.08.2026 14:02 UTC bis 26.08.2026 15:07 UTC (6 Tage)" (report line 4).</summary>
    public static readonly DateTime IncidentStart = new(2026, 8, 20, 14, 2, 0, DateTimeKind.Utc);
    public static readonly DateTime IncidentEnd = new(2026, 8, 26, 15, 7, 0, DateTimeKind.Utc);

    /// <summary>"die Last am 20.08. innerhalb von zwei Minuten von 12 % auf 98 % sprang" (report line 87).</summary>
    public const double CpuBeforePercent = 12.0;
    public static readonly TimeSpan RampDuration = TimeSpan.FromMinutes(2);

    /// <summary>"Server-CPU (Whiskers) | 98,3 % | 9,0 %" — during and after (report line 31).</summary>
    public const double CpuDuringPercent = 98.3;
    public const double CpuAfterPercent = 9.0;

    /// <summary>The containers were not the cause: dockerd burned 184% of 200% (report line 28), so what the
    /// containers themselves accounted for stayed at its ordinary level throughout. Expressed in Docker's
    /// scale, where one busy core is 100 — on this 2-core host that is 12% of the machine.</summary>
    public const double ContainerCpuSumDockerScale = 24.0;

    /// <summary>"rund 1.600-mal am Tag nach ServerMetrics geschrieben" (report line 20) — one sample roughly
    /// every 54 seconds. Rounded to a minute here; the difference cannot change any verdict at these scales,
    /// and a round number is easier to reason about when a test fails.</summary>
    public static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(1);

    /// <summary>Memory was never the problem in this incident and is held at an unremarkable level, so a
    /// memory alert firing during a replay would be a bug in the rule and not a property of the data.</summary>
    private const double MemoryPercent = 41.0;

    private const double MemoryTotalBytes = 4L * 1024 * 1024 * 1024;

    // --- the series ---------------------------------------------------------------------------------------

    /// <summary>The window the plan asks for: 19–27 August, so the series contains a full day of ordinary
    /// operation before the step and a day after the remediation.</summary>
    public static IReadOnlyList<HostSample> Build()
        => Build(new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc),
                 new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc));

    public static IReadOnlyList<HostSample> Build(DateTime fromUtc, DateTime toUtc)
    {
        var samples = new List<HostSample>();

        for (var at = fromUtc; at < toUtc; at += SampleInterval)
            samples.Add(new HostSample(
                at, ServerId, ServerName,
                HostCpuPercent: CpuAt(at),
                ContainerCpuPercentSum: ContainerCpuSumDockerScale,
                MemoryUsedBytes: MemoryTotalBytes * MemoryPercent / 100.0,
                MemoryTotalBytes: MemoryTotalBytes,
                CoreCount: CoreCount));

        return samples;
    }

    /// <summary>The documented shape: ordinary, then a two-minute step, then six days of plateau, then
    /// straight back down. No noise is added — invented noise would look like evidence about false-alarm
    /// behaviour, and this series cannot say anything about that.</summary>
    private static double CpuAt(DateTime at)
    {
        if (at < IncidentStart) return CpuBeforePercent;
        if (at >= IncidentEnd) return CpuAfterPercent;

        var intoRamp = at - IncidentStart;
        if (intoRamp >= RampDuration) return CpuDuringPercent;

        var progress = intoRamp / RampDuration;
        return CpuBeforePercent + (CpuDuringPercent - CpuBeforePercent) * progress;
    }
}
