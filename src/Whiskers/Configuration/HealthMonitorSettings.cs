namespace Whiskers.Configuration;

public class HealthMonitorSettings
{
    public const string SectionName = "HealthMonitor";
    public int CheckIntervalSeconds { get; set; } = 30;
    public int HistoryRetentionHours { get; set; } = 24;
    public int RestartLoopThreshold { get; set; } = 5;
    public int RestartLoopWindowMinutes { get; set; } = 10;

    /// <summary>Consecutive failed cycles before a server is reported unreachable. One failure is normal
    /// (a tunnel rebuild, a host slower than the 8s listing bound); two in a row is an outage.</summary>
    public int ServerUnreachableCycles { get; set; } = 2;

    /// <summary>Consecutive failed cycles before a server that has NOT answered once since startup is
    /// reported unreachable. Remote connections take a while to come up after a restart, and alerting on
    /// that turns every deploy into a burst of false alarms — see <see cref="HealthMonitor.ServerReachabilityTracker"/>.
    /// At the default 30s interval this is a five-minute grace, after which a genuinely dead host is still
    /// reported.</summary>
    public int ServerUnreachableColdStartCycles { get; set; } = 10;
}
