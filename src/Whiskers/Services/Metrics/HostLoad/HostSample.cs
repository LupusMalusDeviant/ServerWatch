namespace Whiskers.Services.Metrics.HostLoad;

/// <summary>
/// One reading of a host's load, with everything needed to judge it (Plan-0004 WP1/WP2).
/// </summary>
/// <param name="HostCpuPercent">Host CPU as a percentage of the <b>whole machine</b> — 100 means every core
/// is busy. This is the scale <c>ServerMetrics.CpuPercent</c> already uses.</param>
/// <param name="ContainerCpuPercentSum">The sum of the container CPU readings, in <b>Docker's</b> scale,
/// where one fully busy core is 100 and a 2-core machine can reach 200. The two numbers are on different
/// scales and comparing them directly is the single biggest way to get this wrong — see
/// <see cref="HostSample.ContainerCpuPercentOfMachine"/>.</param>
/// <param name="CoreCount">Cores on the host. Without it the two CPU numbers cannot be reconciled at all.</param>
public sealed record HostSample(
    DateTime AtUtc,
    string ServerId,
    string ServerName,
    double HostCpuPercent,
    double ContainerCpuPercentSum,
    double MemoryUsedBytes,
    double MemoryTotalBytes,
    int CoreCount)
{
    /// <summary>
    /// The container sum, converted to the same scale as <see cref="HostCpuPercent"/> (Plan-0004 WP2.1).
    ///
    /// <para>This one line is the work of WP2 and its main source of error. During the 2026-08-26 incident
    /// the host sat at 98.3% of the machine while the outside measurement read 195.8 of 200 — the same load,
    /// two conventions. Subtracting one from the other without converting produces a negative "unexplained"
    /// figure on a busy machine and a wildly positive one on an idle single-core host, and either way the
    /// alert would be nonsense in exactly the situation it exists for.</para>
    /// </summary>
    public double ContainerCpuPercentOfMachine =>
        CoreCount > 0 ? ContainerCpuPercentSum / CoreCount : ContainerCpuPercentSum;

    /// <summary>Host load that no container accounts for, in percent of the machine. Negative values are
    /// clamped to zero: the container sum can briefly exceed the host reading because the two are sampled
    /// moments apart, and a negative "unexplained load" is noise, not information.</summary>
    public double UnexplainedCpuPercent => Math.Max(0, HostCpuPercent - ContainerCpuPercentOfMachine);

    public double MemoryPercent =>
        MemoryTotalBytes > 0 ? MemoryUsedBytes * 100.0 / MemoryTotalBytes : 0;
}
