using Whiskers.Models;

namespace Whiskers.Services.Observability.SelfMetrics;

/// <summary>Shorthands so a loop can report what it did — and did not — look at, in one line per site.</summary>
public static class SelfMetricsFleetExtensions
{
    /// <summary>Names for the loops, so the metric labels agree across the code base rather than drifting
    /// into "logmonitor", "log-monitor" and "LogMonitor".</summary>
    public static class Loops
    {
        public const string LogMonitor = "logmonitor";
        public const string Health = "health";
        public const string Metrics = "metrics";
        public const string Cve = "cve";
        public const string ImageUpdate = "imageupdate";
    }

    /// <summary>
    /// Records a skip for every Kubernetes server a Docker-only loop steps over.
    ///
    /// <para>Four loops filter these servers out — health, metrics, CVE and the container listing — and until
    /// now they did it silently. A server that produces no metrics at all is indistinguishable from one that
    /// produces nothing to worry about, so a Kubernetes host looked exactly as calm as a healthy Docker host
    /// while nothing whatsoever was checking it. Recording the skip is what turns "invisible" into
    /// "explicitly not covered".</para>
    /// </summary>
    public static void RecordKubernetesSkips(this ISelfMetrics metrics, string loop, IEnumerable<Whiskers.Models.ServerConfig> allServers)
    {
        foreach (var server in allServers.Where(s => s.ConnectionType == ConnectionType.Kubernetes))
            metrics.RecordSkip(loop, server.Id, "Kubernetes server — this loop only speaks to Docker hosts");
    }
}
