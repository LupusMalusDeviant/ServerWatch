using ModelContextProtocol.Server;
using System.ComponentModel;
using Whiskers.Models;
using Whiskers.Services.GitDeploy;
using Whiskers.Services.Mcp;
using Microsoft.AspNetCore.Http;

namespace Whiskers.Mcp.Tools;

/// <summary>Read-only view of the Git-deploy apps (Plan-0013 WP4). Triggering a deploy or a rollback stays out
/// of the agent's reach for now — those are the write tools GAP-3 designs, together with the health check and
/// the automatic rollback that make them safe to hand over.</summary>
[McpServerToolType]
public class GitDeployTools
{
    [McpToolLevel(McpPermissionLevels.Read)]
    [McpServerTool, Description("List the applications Whiskers deploys from Git: repository, branch, compose path, target server, and the outcome, time and commit of the last deploy. Read-only — this cannot start a deploy.")]
    public static async Task<string> ListGitDeployApps(
        IHttpContextAccessor httpContextAccessor,
        IMcpPermissionService permissionService,
        IGitDeployService gitDeploy)
    {
        var denied = McpPermissionCheck.CheckAccess(httpContextAccessor, permissionService, "list_git_deploy_apps");
        if (denied != null) return denied;

        var apps = await gitDeploy.GetAppsAsync();
        if (apps.Count == 0) return "No Git-deploy applications are configured.";

        // Never the token itself — the model only carries whether one is stored, and that is all that is useful.
        var lines = apps.Select(a =>
        {
            var last = a.LastDeployedAt is null
                ? "never deployed"
                : $"last {(a.LastDeploySucceeded == true ? "OK" : a.LastDeploySucceeded == false ? "FAILED" : "unknown")} " +
                  $"at {a.LastDeployedAt:yyyy-MM-dd HH:mm} UTC" +
                  (string.IsNullOrEmpty(a.LastDeployedSha) ? "" : $" ({a.LastDeployedSha[..Math.Min(8, a.LastDeployedSha.Length)]})");

            return $"- {a.Name} [{a.Id}] | {a.RepoUrl} @ {a.Branch} | compose: {a.ComposePath} | " +
                   $"server: {a.ServerId} | credentials: {(a.HasToken ? "stored" : "none")} | {last}";
        });

        return $"Git-deploy applications ({apps.Count}):\n{string.Join('\n', lines)}";
    }
}
