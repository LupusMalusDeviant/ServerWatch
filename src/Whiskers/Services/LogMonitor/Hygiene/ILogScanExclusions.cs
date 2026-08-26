using Whiskers.Models;

namespace Whiskers.Services.LogMonitor.Hygiene;

/// <summary>Why one container is not scanned for log alerts.</summary>
/// <param name="Reason">Machine-readable: <c>self</c>, <c>access-path</c> or <c>manual</c>.</param>
/// <param name="Detail">Human-readable, and specific enough to argue with — an exclusion nobody can check is
/// a blind spot with paperwork.</param>
public sealed record LogScanExclusion(
    string ServerId, string ContainerId, string ContainerName, string Reason, string Detail);

/// <summary>
/// Which containers the log scan steps over, and why (Plan-0007 WP1/WP2).
///
/// <para>Two containers caused the 2026-08-26 incident: the socket proxy and the tunnel through which Whiskers
/// reaches Docker. Every request Whiskers makes is a line in their logs, so scanning them means scanning the
/// record of the scan. Left alone for two weeks they grew to 822 MB between them.</para>
///
/// <para><b>Detected by path, not by name.</b> A container is on the access path because Whiskers actually
/// connects to it — its published port is the port in this server's configuration. A container that merely
/// happens to be called <c>socket-proxy</c> keeps being scanned, because it is somebody else's proxy and its
/// logs are somebody else's evidence.</para>
///
/// <para><b>Log scan only.</b> These containers stay under health, metric and CVE monitoring. Their log
/// content is worthless to us; their state is not.</para>
/// </summary>
public interface ILogScanExclusions
{
    /// <summary>The containers of one server that the log scan should skip.</summary>
    IReadOnlyList<LogScanExclusion> Evaluate(Models.ServerConfig server, IReadOnlyList<ContainerInfo> containers);

    /// <summary>Everything excluded as of the last evaluation, for the server view, the metric and the MCP
    /// tool. An exclusion that is not visible somewhere is indistinguishable from a container that is quietly
    /// broken.</summary>
    IReadOnlyList<LogScanExclusion> Current();
}
