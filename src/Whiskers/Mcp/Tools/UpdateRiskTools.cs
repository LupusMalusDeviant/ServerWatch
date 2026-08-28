using System.ComponentModel;
using System.Text;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using Whiskers.Services.ImageUpdate;
using Whiskers.Models;
using Whiskers.Services.Mcp;

namespace Whiskers.Mcp.Tools;

[McpServerToolType]
public class UpdateRiskTools
{
    [McpToolLevel(McpPermissionLevels.Read)]
    [McpServerTool, Description(
        "Assess what updating one container's image would change, BEFORE recreating anything. Compares what " +
        "the running image and the candidate image declare — entrypoint, user, exposed ports, expected " +
        "volumes, healthcheck, base OS — and counts which CVEs the update actually closes. Pulls the " +
        "candidate image (starts and restarts nothing). Read-only with respect to running containers.")]
    public static async Task<string> AssessUpdateRisk(
        IHttpContextAccessor httpContextAccessor,
        IMcpPermissionService permissionService,
        IUpdateRiskService assessor,
        [Description("Server id, e.g. 'local' or 'burgcloud'.")] string serverId,
        [Description("Container name or id prefix.")] string container,
        [Description("Scan the candidate image for CVEs so the benefit has a number. Costs tens of seconds; " +
                     "without it the benefit is reported as unknown, never as zero.")] bool scanCandidate = true)
    {
        var denied = McpPermissionCheck.CheckAccess(httpContextAccessor, permissionService, "assess_update_risk");
        if (denied != null) return denied;

        var report = await assessor.AssessAsync(serverId, container, scanCandidate);
        return Render(report);
    }

    internal static string Render(UpdateRiskReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Update risk for {r.ContainerName} on {r.ServerId} ({r.ImageRef})");

        if (r.Error is not null)
        {
            // An assessment that could not be made must say so. "No findings" here would read as "safe".
            sb.AppendLine($"  ASSESSMENT FAILED: {r.Error}");
            sb.AppendLine("  This is not a verdict. Nothing was checked.");
            return sb.ToString();
        }

        if (!r.UpdateAvailable)
        {
            sb.AppendLine("  Already on the newest image for this tag — nothing to decide.");
            return sb.ToString();
        }

        var risk = r.Risk!;
        sb.AppendLine($"  Risk: {risk.Level.ToString().ToUpperInvariant()}   " +
                      (risk.CvesClosed is { } n
                          ? $"Closes {n} vulnerabilit{(n == 1 ? "y" : "ies")}."
                          : "Benefit unknown — the candidate image was not scanned."));
        sb.AppendLine($"  {r.CurrentDigest?[..Math.Min(19, r.CurrentDigest.Length)]} → " +
                      $"{r.CandidateDigest?[..Math.Min(19, r.CandidateDigest?.Length ?? 0)]}");

        if (risk.Findings.Count == 0)
            sb.AppendLine("  Nothing detectable changed in what the image declares.");
        else
        {
            sb.AppendLine();
            sb.AppendLine("  What changes:");
            foreach (var f in risk.Findings.OrderByDescending(f => f.Level))
                sb.AppendLine($"    [{f.Level.ToString().ToUpperInvariant()}] {f.What} — {f.WhyItMatters}");
        }

        // Always. A verdict that hid its own limits would be worse than no verdict — this compares
        // declarations, not behaviour.
        sb.AppendLine();
        sb.AppendLine("  NOT covered by this assessment:");
        foreach (var b in risk.BlindSpots)
            sb.AppendLine($"    - {b}");
        sb.AppendLine();
        sb.AppendLine("  \"Low risk\" here means nothing detectable changed. It does not mean safe.");

        return sb.ToString();
    }
}
