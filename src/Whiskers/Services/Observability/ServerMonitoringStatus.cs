using Whiskers.Services.Docker.Budget;

namespace Whiskers.Services.Observability;

/// <summary>
/// What is happening to a server's monitoring — which is not the same question as how the server is
/// (Plan-0005 WP5).
///
/// <para>Three of these four states produce no findings, and only one of them is good news. Collapsing them
/// into "quiet" is the confusion the 2026-08-26 incident ran on for six days, and it is the reason this is an
/// enum and not a boolean.</para>
/// </summary>
public enum ServerMonitoringState
{
    /// <summary>Being checked normally. An absence of findings here means there is nothing to find.</summary>
    Monitored,

    /// <summary>Somebody paused the background checks. Nothing is being looked at; that is a decision, not a
    /// verdict about the server.</summary>
    Paused,

    /// <summary>Whiskers stopped calling this server itself after repeated failures — the circuit is open.
    /// Also nothing being looked at, but nobody chose it.</summary>
    Throttled,

    /// <summary>The server did not answer. Here the silence IS about the server.</summary>
    Unreachable
}

/// <summary>How to say it, and what to say about what it means.</summary>
public sealed record ServerMonitoringStatus(
    ServerMonitoringState State, string Label, string Meaning, string? Detail, TimeSpan? Remaining);

/// <summary>How long a pause should last, offered as a choice (Plan-0005 WP2.1).</summary>
/// <param name="Duration">Null means "until revoked" — which is the one that needs a reminder, because it is
/// the one people forget.</param>
public sealed record PauseOption(string Label, TimeSpan? Duration);

public static class ServerMonitoring
{
    /// <summary>The durations offered in the UI. Short options first: the common case is "I want to look at
    /// something for ten minutes", and putting the open-ended one first would make it the accidental default.</summary>
    public static readonly IReadOnlyList<PauseOption> PauseOptions = new[]
    {
        new PauseOption("15 minutes", TimeSpan.FromMinutes(15)),
        new PauseOption("1 hour", TimeSpan.FromHours(1)),
        new PauseOption("4 hours", TimeSpan.FromHours(4)),
        new PauseOption("Until I revoke it", null)
    };

    /// <summary>
    /// Decides what a server's monitoring state is, in the order that matters.
    ///
    /// <para>A deliberate pause outranks everything else: if somebody switched the checks off, then
    /// "unreachable" is not a finding about the server, it is the absence of a check. Reporting the
    /// consequence of a decision as a fault is how an operator ends up debugging their own switch.</para>
    /// </summary>
    public static ServerMonitoringStatus Describe(
        string serverId,
        ILoopSuspensionService suspension,
        IServerCircuitBreaker circuit,
        bool answeredLastCycle,
        DateTime nowUtc)
    {
        var paused = suspension.Current().FirstOrDefault(p =>
            string.Equals(p.ServerId, serverId, StringComparison.OrdinalIgnoreCase));

        if (paused is not null)
            return new ServerMonitoringStatus(
                ServerMonitoringState.Paused,
                "paused",
                "Nothing is being checked here. That is not the same as nothing being wrong.",
                paused.Automatic ? $"Whiskers paused itself: {paused.Reason}" : paused.Reason,
                paused.Until - nowUtc);

        var state = circuit.Snapshot(serverId).State;
        if (state != ServerCircuitState.Closed)
            return new ServerMonitoringStatus(
                ServerMonitoringState.Throttled,
                "throttled",
                "Whiskers stopped calling this server after repeated failures. It is not being checked, and " +
                "nobody chose that — it happened on its own.",
                circuit.Snapshot(serverId).LastReason,
                null);

        if (!answeredLastCycle)
            return new ServerMonitoringStatus(
                ServerMonitoringState.Unreachable,
                "unreachable",
                "The server did not answer. Here the silence really is about the server.",
                null, null);

        return new ServerMonitoringStatus(
            ServerMonitoringState.Monitored,
            "monitored",
            "Checks are running. An absence of findings here means there is nothing to find.",
            null, null);
    }

    /// <summary>How long a pause still has to run, in the shortest unambiguous form. "until revoked" is stored
    /// as a far-future deadline, so anything beyond a year is reported as exactly that rather than as
    /// "in 3652 days".</summary>
    public static string Remaining(TimeSpan? remaining) => remaining switch
    {
        null => "",
        { TotalDays: > 365 } => "until revoked",
        { TotalMinutes: < 1 } => "less than a minute left",
        { TotalMinutes: < 90 } => $"{remaining.Value.TotalMinutes:F0} min left",
        _ => $"{remaining.Value.TotalHours:F0} h left"
    };
}
