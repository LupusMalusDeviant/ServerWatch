using ModelContextProtocol.Server;
using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using Whiskers.Models;
using Whiskers.Services.Docker.Budget;
using Whiskers.Services.Mcp;
using Whiskers.Services.Observability;
using Whiskers.Services.Observability.SelfMetrics;
using Whiskers.Services.ServerConfig;

namespace Whiskers.Mcp.Tools;

/// <summary>What Whiskers knows about itself, for the agent (Plan-0003 WP-MCP).
///
/// <para>Whiskers exported the container inventory of the whole fleet and not one number about its own
/// behaviour. During the 2026-08-26 incident the log monitor wrote "timed out after 15s" every cycle for six
/// days — the earliest and most precise signal of the whole event — and an agent asked "is everything being
/// monitored?" had no way to find out.</para>
///
/// <para>Read-only, and there is no write counterpart by design: an agent that could reset these counters
/// could also erase the evidence that something has been broken for a week.</para></summary>
[McpServerToolType]
public class SelfStatusTools
{
    /// <summary>A loop is called out once its last success is older than this many of its own intervals.
    /// Read from <see cref="ScanSupervisor"/>, not copied, so tool and alert cannot drift apart.</summary>
    private const int IntervalsBeforeConcern = ScanSupervisor.IntervalsBeforeAlarm;

    [McpToolLevel(McpPermissionLevels.Read)]
    [McpServerTool, Description("Report whether Whiskers itself is still working: for every background loop and server, how long ago it last completed a cycle, how long a cycle takes, how many failed, and which servers it deliberately skips and why — plus the per-server load budget and circuit-breaker state. Use this before trusting an absence of findings: a loop that has stopped produces no alerts at all, which looks exactly like a fleet with nothing wrong. Read-only.")]
    public static string GetWhiskersSelfStatus(
        IHttpContextAccessor httpContextAccessor,
        IMcpPermissionService permissionService,
        ISelfMetrics metrics,
        IServerBudget budget,
        IServerCircuitBreaker circuit,
        ILoopSuspensionService suspension,
        IServerConfigService servers)
    {
        var denied = McpPermissionCheck.CheckAccess(httpContextAccessor, permissionService, "get_whiskers_self_status");
        if (denied != null) return denied;

        var loops = metrics.Loops();
        if (loops.Count == 0)
            return "No loop has recorded a cycle yet. Either Whiskers has just started, or every background " +
                   "loop is failing before it can record anything — the second is a serious fault, so check " +
                   "again in a minute before concluding the first.";

        var now = DateTime.UtcNow;
        var lines = new List<string>();
        var concerns = new List<string>();

        foreach (var loop in loops)
        {
            var name = servers.GetServer(loop.ServerId)?.Name ?? loop.ServerId;

            if (loop.SkipReason is { } reason)
            {
                // A skipped server is not a stalled one, but it must still appear: "the loop does not cover
                // this server" and "the loop found nothing" look identical if it is left out.
                lines.Add($"- {loop.Loop} / {name}: SKIPPED ({reason}) — not covered, no findings expected.");
                continue;
            }

            var reference = loop.LastSuccess ?? loop.LastAttempt;
            var age = reference is null ? (TimeSpan?)null : now - reference.Value;
            var ageText = age is null ? "never" : Describe(age.Value) + " ago";

            var concerning = false;
            if (loop.ExpectedInterval is { } interval)
            {
                var allowed = interval * IntervalsBeforeConcern;
                concerning = age is null || age > allowed;
            }

            var status = concerning ? "STALLED" : "ok";
            lines.Add(
                $"- {loop.Loop} / {name}: {status}; last success {ageText}; " +
                $"cycle {loop.LastDuration.TotalMilliseconds:0} ms; {loop.Cycles} cycles, {loop.Failures} failed" +
                (loop.ExpectedInterval is { } i ? $"; runs every {Describe(i)}" : "; no declared cadence"));

            if (concerning)
                concerns.Add($"{loop.Loop} on {name} has not completed a cycle in {ageText}");
        }

        var counters = metrics.Counters();
        if (counters.Count > 0)
        {
            lines.Add("");
            foreach (var (counterName, perServer) in counters.OrderBy(c => c.Key, StringComparer.Ordinal))
                lines.Add($"- counter {counterName}: " +
                          string.Join(", ", perServer.Select(kv => $"{servers.GetServer(kv.Key)?.Name ?? kv.Key}={kv.Value}")));
        }

        var pressure = new List<string>();
        foreach (var server in servers.GetEnabledServers())
        {
            var state = circuit.Snapshot(server.Id).State;
            var inFlight = budget.Snapshot(server.Id);
            var paused = suspension.IsSuspended(server.Id) ? "; background checks PAUSED" : "";

            if (state != ServerCircuitState.Closed || inFlight.BackgroundInFlight > 0 || paused.Length > 0)
                pressure.Add($"- {server.Name}: circuit {state}; " +
                             $"{inFlight.BackgroundInFlight}/{inFlight.BackgroundLimit} background, " +
                             $"{inFlight.InteractiveInFlight}/{inFlight.InteractiveLimit} interactive{paused}");
        }

        var report = $"Whiskers self-status ({loops.Count} loop/server pair(s)):\n{string.Join('\n', lines)}";

        if (pressure.Count > 0)
            report += $"\n\nLoad and circuit state:\n{string.Join('\n', pressure)}";

        report += concerns.Count > 0
            ? $"\n\nNEEDS ATTENTION: {string.Join("; ", concerns)}. While a loop is stalled its silence means " +
              "nothing — do not read an absence of findings from those servers as good news."
            : "\n\nEvery loop with a declared cadence has completed a cycle recently, so an absence of findings " +
              "can be taken at face value.";

        return report;
    }

    private static string Describe(TimeSpan span) => span switch
    {
        { TotalSeconds: < 90 } => $"{span.TotalSeconds:0}s",
        { TotalMinutes: < 90 } => $"{span.TotalMinutes:0}m",
        { TotalHours: < 48 } => $"{span.TotalHours:0.#}h",
        _ => $"{span.TotalDays:0.#}d"
    };
}
