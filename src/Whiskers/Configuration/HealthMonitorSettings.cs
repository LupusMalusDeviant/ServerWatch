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
}
