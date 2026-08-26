using ModelContextProtocol.Server;
using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using Whiskers.Models;
using Whiskers.Services.LogMonitor.Hygiene;
using Whiskers.Services.Mcp;
using Whiskers.Services.ServerConfig;

namespace Whiskers.Mcp.Tools;

/// <summary>Log hygiene, read-only (Plan-0007 WP-MCP).
///
/// <para>Answers the question an agent cannot otherwise answer: "why is this container producing no log
/// alerts?" Without it, a container excluded from the scan is indistinguishable from a container with nothing
/// to report — and the agent would go looking for a fault in the rules.</para>
///
/// <para>No write counterpart, deliberately. Changing what is scanned means deciding to stop watching
/// something, and the remediation for an oversized log recreates the container. Both belong to a person; the
/// report hands over the finding and, later, the exact command — it does not run it.</para></summary>
[McpServerToolType]
public class LogHygieneTools
{
    [McpToolLevel(McpPermissionLevels.Read)]
    [McpServerTool, Description("Report which containers the log-alert scan steps over and why. Containers on Whiskers' own Docker access path are skipped because every request Whiskers makes is a line in their log — scanning them means scanning the record of the scan. Excluded containers are still covered by health, metric and CVE monitoring; only their log content is ignored. Read-only: this cannot change what is scanned.")]
    public static string GetLogHygieneReport(
        IHttpContextAccessor httpContextAccessor,
        IMcpPermissionService permissionService,
        ILogScanExclusions exclusions,
        IServerConfigService servers,
        [Description("Limit the report to one server (optional)")] string? serverId = null)
    {
        var denied = McpPermissionCheck.CheckAccess(httpContextAccessor, permissionService, "get_log_hygiene_report");
        if (denied != null) return denied;

        var all = exclusions.Current();
        var scoped = string.IsNullOrWhiteSpace(serverId)
            ? all
            : all.Where(e => string.Equals(e.ServerId, serverId, StringComparison.OrdinalIgnoreCase)).ToList();

        if (scoped.Count == 0)
        {
            // "Nothing excluded" is a real answer and a slightly suspicious one on a host reached through a
            // proxy — say so rather than leaving the agent to read silence as confirmation.
            return string.IsNullOrWhiteSpace(serverId)
                ? "No containers are excluded from the log-alert scan. On a host Whiskers reaches through a " +
                  "tunnel or socket proxy, expect at least one — if there is none, the access path may not be " +
                  "detectable from the container list and may need SERVERWATCH_SELF_CONTAINERS."
                : $"No containers are excluded from the log-alert scan on '{serverId}'.";
        }

        var lines = scoped
            .GroupBy(e => e.ServerId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var name = servers.GetServer(group.Key)?.Name ?? group.Key;
                var entries = group.Select(e => $"  - {e.ContainerName} [{e.Reason}] — {e.Detail}");
                return $"{name} ({group.Key}):\n{string.Join('\n', entries)}";
            });

        return $"Containers excluded from the log-alert scan ({scoped.Count}):\n{string.Join("\n\n", lines)}\n\n" +
               "These containers are still monitored for health, metrics and CVEs — only their log content is " +
               "skipped. Excluding the access path removes the trigger of the 2026-08-26 incident, not its " +
               "cause: the cause was a log fetch that was abandoned rather than cancelled.";
    }
}
