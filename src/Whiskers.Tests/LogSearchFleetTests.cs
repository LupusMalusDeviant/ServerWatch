using Microsoft.Extensions.Logging.Abstractions;
using Whiskers.Models;
using Whiskers.Services.LogMonitor;

namespace Whiskers.Tests;

/// <summary>The log search had the same blind spot as the monitor: without an explicit server id it asked
/// only the default host, while the page's container picker lists containers from every server — so picking
/// a remote container returned "no matches" instead of its log lines.</summary>
public class LogSearchFleetTests
{
    private static ContainerInfo Container(string id, string name, string serverId, string serverName) =>
        new() { Id = id, Name = name, ServerId = serverId, ServerName = serverName };

    private static LogSearchService Search(FakeDocker docker) =>
        new(docker, NullLogger<LogSearchService>.Instance);

    private static FakeDocker TwoHosts()
    {
        var docker = new FakeDocker(
            Container("c-local", "authentik-worker-1", "local", "Badwolf (local)"),
            Container("c-remote", "burg-web", "infomaniak", "LupusMalus"));
        docker.Logs["local/c-local"] = "nothing to see";
        docker.Logs["infomaniak/c-remote"] = "ERROR: remote boom";
        return docker;
    }

    [Fact]
    public async Task Without_a_server_id_the_whole_fleet_is_searched()
    {
        var result = await Search(TwoHosts()).SearchAsync("boom");

        var hit = Assert.Single(result);
        Assert.Equal("burg-web", hit.ContainerName);
        Assert.Equal("LupusMalus", hit.ServerName);   // results must say which host they came from
    }

    [Fact]
    public async Task Each_container_is_read_from_its_own_server()
    {
        var docker = TwoHosts();
        await Search(docker).SearchAsync("nothing-matches-this");

        Assert.Equal(
            new[] { ("c-local", "local"), ("c-remote", "infomaniak") },
            docker.LogCalls.Select(c => (c.ContainerId, c.ServerId)).ToArray());
    }

    [Fact]
    public async Task An_explicit_server_id_still_narrows_the_search()
    {
        var docker = TwoHosts();
        var result = await Search(docker).SearchAsync("boom", serverId: "local");

        Assert.Empty(result);
        Assert.Equal(new[] { "c-local" }, docker.LogCalls.Select(c => c.ContainerId).ToArray());
    }

    [Fact]
    public async Task A_container_filter_matches_by_name_across_servers()
    {
        var docker = TwoHosts();
        var result = await Search(docker).SearchAsync("boom", containerId: "burg-web");

        Assert.Equal("infomaniak", Assert.Single(result).ServerId);
    }
}
