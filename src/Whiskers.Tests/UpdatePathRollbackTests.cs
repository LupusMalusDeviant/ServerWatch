using System.Reflection;
using Whiskers.Mcp.Tools;
using Whiskers.Services.AutoUpdate;

namespace Whiskers.Tests;

/// <summary>
/// Every update path has to leave a way back (2026-08-28).
///
/// <para>The auto-updater and the Dashboard's update button both take a rollback snapshot before recreating a
/// container. The MCP path did not — so every update driven by an agent or a script left no rollback at all.
/// That is the caller least able to notice the loss and least able to repair it by hand, and the update
/// reported success either way.</para>
///
/// <para>Found by checking <c>UpdateRollbacks</c> after an update that had just reported success: still zero
/// rows. The tool said it worked, and it had, in the only sense it was measuring.</para>
///
/// <para><b>What this test does and does not catch.</b> It holds the dependency in place — the likely
/// regression is somebody tidying an "unused-looking" parameter away during a refactor. It cannot see whether
/// the call is still made; that would need the whole MCP stack in a harness. Said out loud because a test
/// whose limits are unstated gets trusted for more than it does.</para>
/// </summary>
public class UpdatePathRollbackTests
{
    private static MethodInfo UpdateContainer() =>
        typeof(ContainerTools).GetMethod(nameof(ContainerTools.UpdateContainer))
        ?? throw new InvalidOperationException("ContainerTools.UpdateContainer is gone — renamed or removed.");

    [Fact]
    public void The_mcp_update_path_can_take_a_rollback_snapshot()
    {
        // Without this dependency the tool cannot capture anything, and an agent-driven update becomes a
        // one-way door.
        var parameters = UpdateContainer().GetParameters().Select(p => p.ParameterType).ToList();

        Assert.Contains(typeof(IAutoUpdateService), parameters);
    }

    [Fact]
    public void The_snapshot_service_still_offers_what_the_update_paths_need()
    {
        // The Dashboard, the auto-updater and now the MCP tool all go through this one method. If its shape
        // changes, all three lose their rollback at once — and each of them would still report success.
        var capture = typeof(IAutoUpdateService).GetMethod(nameof(IAutoUpdateService.CaptureSnapshotAsync));

        Assert.NotNull(capture);
        Assert.Equal(typeof(Task), capture!.ReturnType);
    }

    [Fact]
    public void Updating_is_still_a_write_level_operation()
    {
        // Unrelated to the rollback, and worth pinning next to it: the tool that recreates containers must
        // never quietly become readable-by-anyone.
        var level = UpdateContainer().GetCustomAttribute<Whiskers.Mcp.McpToolLevelAttribute>();

        Assert.NotNull(level);
        Assert.Equal(Whiskers.Models.McpPermissionLevels.Write, level!.Level);
    }
}
