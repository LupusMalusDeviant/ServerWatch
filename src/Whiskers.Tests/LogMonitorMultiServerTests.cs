using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Whiskers.Models;
using Whiskers.Services.Docker;
using Whiskers.Services.LogMonitor;
using Whiskers.Services.Notifications;
using Whiskers.Services.Persistence;
using Whiskers.Services.ServerConfig;

namespace Whiskers.Tests;

/// <summary>The log monitor scans the WHOLE fleet, not just the default server: an "all containers" rule
/// used to silently cover one host because the scan called ListContainersAsync() without a server id.
/// These tests pin the fleet-wide scan and everything that had to move with it — per-server log fetches,
/// server-scoped watermarks/cooldowns, and the self-container guard that must only apply to our own host.
/// </summary>
public sealed class LogMonitorMultiServerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"logmon-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _sp;

    public LogMonitorMultiServerTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MetricsDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        _sp = services.BuildServiceProvider();
        using var scope = _sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<MetricsDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _sp.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    // --- helpers ---------------------------------------------------------------------------------------

    private static ContainerInfo Container(string id, string name, string serverId, string serverName) =>
        new() { Id = id, Name = name, Image = "img:1", ServerId = serverId, ServerName = serverName };

    private LogAlertRuleEntity SeedRule(string name = "fatal", string pattern = "FATAL",
        string? containerName = null, int cooldownMinutes = 10)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetricsDbContext>();
        var rule = new LogAlertRuleEntity
        {
            Name = name,
            Pattern = pattern,
            ContainerName = containerName,
            Severity = "error",
            CooldownMinutes = cooldownMinutes
        };
        db.LogAlertRules.Add(rule);
        db.SaveChanges();
        return rule;
    }

    private LogAlertRuleEntity ReloadRule(string ruleId)
    {
        using var scope = _sp.CreateScope();
        return scope.ServiceProvider.GetRequiredService<MetricsDbContext>()
            .LogAlertRules.AsNoTracking().Single(r => r.RuleId == ruleId);
    }

    private LogMonitorService Monitor(FakeDocker docker, FakeNotifications notifications, FakeServerConfig servers) =>
        new(_sp.GetRequiredService<IServiceScopeFactory>(), docker, servers, notifications,
            NullLogger<LogMonitorService>.Instance);

    private static FakeServerConfig TwoServers() => new(
        new Whiskers.Models.ServerConfig { Id = "local", Name = "Badwolf (local)", ConnectionType = ConnectionType.Local, IsDefault = true },
        new Whiskers.Models.ServerConfig { Id = "infomaniak", Name = "LupusMalus", ConnectionType = ConnectionType.TCP });

    // --- the fleet-wide scan ---------------------------------------------------------------------------

    [Fact]
    public async Task Alerts_on_a_container_of_a_REMOTE_server()
    {
        var rule = SeedRule();
        var docker = new FakeDocker(
            Container("c-local", "authentik-worker-1", "local", "Badwolf (local)"),
            Container("c-remote", "burg-web", "infomaniak", "LupusMalus"));
        docker.Logs["infomaniak/c-remote"] = "some noise\nFATAL: Kontrolltest\n";
        var notifications = new FakeNotifications();

        await Monitor(docker, notifications, TwoServers()).RunScanCycleAsync(CancellationToken.None);

        var evt = Assert.Single(notifications.Events);
        Assert.Equal("burg-web", evt.ContainerName);
        Assert.Equal("log_alert:error", evt.EventType);
        Assert.Contains("LupusMalus", evt.ImageInfo); // the host is named — container names repeat
        Assert.Equal(1, ReloadRule(rule.RuleId).TriggerCount);
    }

    [Fact]
    public async Task Fetches_each_container_log_from_its_own_server()
    {
        SeedRule();
        var docker = new FakeDocker(
            Container("c-local", "authentik-worker-1", "local", "Badwolf (local)"),
            Container("c-remote", "burg-web", "infomaniak", "LupusMalus"));

        await Monitor(docker, new FakeNotifications(), TwoServers()).RunScanCycleAsync(CancellationToken.None);

        // Without the server id the call would land on the default host — the bug that hid five servers.
        Assert.Equal(
            new[] { ("c-local", "local"), ("c-remote", "infomaniak") },
            docker.LogCalls.Select(c => (c.ContainerId, c.ServerId)).OrderBy(c => c.ContainerId).ToArray());
    }

    [Fact]
    public async Task Keeps_the_local_behaviour_unchanged()
    {
        var rule = SeedRule();
        var docker = new FakeDocker(Container("c-local", "authentik-worker-1", "local", "Badwolf (local)"));
        docker.Logs["local/c-local"] = "FATAL: Kontrolltest\n";
        var notifications = new FakeNotifications();

        await Monitor(docker, notifications, TwoServers()).RunScanCycleAsync(CancellationToken.None);

        Assert.Equal("authentik-worker-1", Assert.Single(notifications.Events).ContainerName);
        Assert.Equal(1, ReloadRule(rule.RuleId).TriggerCount);
    }

    [Fact]
    public async Task A_failing_server_does_not_stop_the_others()
    {
        SeedRule();
        var docker = new FakeDocker(
            Container("c-local", "authentik-worker-1", "local", "Badwolf (local)"),
            Container("c-remote", "burg-web", "infomaniak", "LupusMalus"));
        docker.FailingServerIds.Add("local");
        docker.Logs["infomaniak/c-remote"] = "FATAL: Kontrolltest\n";
        var notifications = new FakeNotifications();

        await Monitor(docker, notifications, TwoServers()).RunScanCycleAsync(CancellationToken.None);

        Assert.Equal("burg-web", Assert.Single(notifications.Events).ContainerName);
    }

    // --- per-server keys -------------------------------------------------------------------------------

    [Fact]
    public async Task Same_container_id_on_two_servers_gets_its_own_cooldown()
    {
        // Container ids are unique per host only: a shared cooldown key would swallow the second host.
        SeedRule();
        var docker = new FakeDocker(
            Container("deadbeef", "postgres", "local", "Badwolf (local)"),
            Container("deadbeef", "postgres", "infomaniak", "LupusMalus"));
        docker.Logs["local/deadbeef"] = "FATAL: boom\n";
        docker.Logs["infomaniak/deadbeef"] = "FATAL: boom\n";
        var notifications = new FakeNotifications();

        await Monitor(docker, notifications, TwoServers()).RunScanCycleAsync(CancellationToken.None);

        Assert.Equal(
            new[] { "Badwolf (local)", "LupusMalus" },
            notifications.Events.Select(e => e.ImageInfo!.Split('@')[^1].Trim()).OrderBy(s => s).ToArray());
    }

    [Fact]
    public void Composite_key_separates_the_same_container_id_on_two_servers()
    {
        var local = Container("deadbeef", "postgres", "local", "Badwolf (local)");
        var remote = Container("deadbeef", "postgres", "infomaniak", "LupusMalus");

        // The watermark map (_lastLogCheck) is keyed by this: on container.Id alone the two hosts would
        // share one "last checked" timestamp and one of them would skip its window.
        Assert.NotEqual(LogMonitorService.CompositeKey(local), LogMonitorService.CompositeKey(remote));
        Assert.StartsWith("local:", LogMonitorService.CompositeKey(local));
    }

    // --- the self-container guard ----------------------------------------------------------------------

    [Fact]
    public async Task Never_scans_our_own_container_on_the_local_host()
    {
        SeedRule();
        var docker = new FakeDocker(Container("c-self", "serverwatch", "local", "Badwolf (local)"));
        docker.Logs["local/c-self"] = "FATAL: our own alert log line\n";
        var notifications = new FakeNotifications();

        await Monitor(docker, notifications, TwoServers()).RunScanCycleAsync(CancellationToken.None);

        Assert.Empty(docker.LogCalls);
        Assert.Empty(notifications.Events);
    }

    [Fact]
    public async Task Still_scans_a_same_named_container_on_a_remote_host()
    {
        // The guard exists to break OUR OWN feedback loop; a remote namesake is a different process.
        SeedRule();
        var docker = new FakeDocker(Container("c-remote", "serverwatch", "infomaniak", "LupusMalus"));
        docker.Logs["infomaniak/c-remote"] = "FATAL: Kontrolltest\n";
        var notifications = new FakeNotifications();

        await Monitor(docker, notifications, TwoServers()).RunScanCycleAsync(CancellationToken.None);

        Assert.Single(notifications.Events);
    }

    [Fact]
    public void Self_server_ids_are_the_local_connections()
    {
        var ids = LogMonitorService.ResolveSelfServerIds(TwoServers());
        Assert.Equal(new[] { "local" }, ids.ToArray());
    }

    [Fact]
    public void Self_server_ids_fall_back_to_the_default_server_when_no_local_one_exists()
    {
        // All-mTLS / Kubernetes fleets have no ConnectionType.Local entry — keep guarding the host the
        // single-server scan used to look at rather than guarding nothing.
        var servers = new FakeServerConfig(
            new Whiskers.Models.ServerConfig { Id = "hetzner-apps", Name = "AppServer", ConnectionType = ConnectionType.TCP, IsDefault = true },
            new Whiskers.Models.ServerConfig { Id = "rabenhof", Name = "Rabenhof", ConnectionType = ConnectionType.TCP });

        Assert.Equal(new[] { "hetzner-apps" }, LogMonitorService.ResolveSelfServerIds(servers).ToArray());
    }

    // --- rule targeting --------------------------------------------------------------------------------

    [Fact]
    public void A_name_filtered_rule_matches_that_name_on_every_server()
    {
        // The UI picker lists containers of all servers but stores only the name, and MCP-created rules
        // set the name alone — so a name rule means "this workload, wherever it runs".
        var rule = new LogAlertRuleEntity { ContainerName = "postgres" };
        Assert.True(LogMonitorService.RuleApplies(rule, Container("a", "postgres", "local", "Badwolf")));
        Assert.True(LogMonitorService.RuleApplies(rule, Container("b", "postgres", "rabenhof", "Rabenhof")));
        Assert.False(LogMonitorService.RuleApplies(rule, Container("c", "redis", "rabenhof", "Rabenhof")));
    }

    [Fact]
    public async Task A_name_filtered_rule_alerts_only_on_that_container()
    {
        // ContainerId stays null for UI- and MCP-created rules, so "no filter" must test BOTH fields —
        // otherwise a rule for one container fires on every container of every server.
        SeedRule(name: "qr", pattern: "LUPUSLINK-NEU", containerName: "lupusmalus-web-app-1");
        var docker = new FakeDocker(
            Container("c-local", "authentik-worker-1", "local", "Badwolf (local)"),
            Container("c-remote", "lupusmalus-web-app-1", "infomaniak", "LupusMalus"));
        docker.Logs["local/c-local"] = "LUPUSLINK-NEU seen in the wrong container\n";
        docker.Logs["infomaniak/c-remote"] = "LUPUSLINK-NEU\n";
        var notifications = new FakeNotifications();

        await Monitor(docker, notifications, TwoServers()).RunScanCycleAsync(CancellationToken.None);

        Assert.Equal("lupusmalus-web-app-1", Assert.Single(notifications.Events).ContainerName);
        Assert.DoesNotContain(docker.LogCalls, c => c.ContainerId == "c-local"); // not even fetched
    }

    [Fact]
    public void An_unfiltered_rule_matches_everything()
    {
        var rule = new LogAlertRuleEntity();
        Assert.True(LogMonitorService.RuleApplies(rule, Container("a", "anything", "rabenhof", "Rabenhof")));
    }

    // --- fakes -----------------------------------------------------------------------------------------

    private sealed record LogCall(string ContainerId, string? ServerId, DateTime? Since);

    private sealed class FakeNotifications : INotificationService
    {
        public List<NotificationEvent> Events { get; } = new();
        public Task SendAsync(NotificationEvent evt) { Events.Add(evt); return Task.CompletedTask; }
        public Task SendTestAsync() => Task.CompletedTask;
    }

    private sealed class FakeServerConfig : IServerConfigService
    {
        private readonly List<Whiskers.Models.ServerConfig> _servers;
        public FakeServerConfig(params Whiskers.Models.ServerConfig[] servers) => _servers = servers.ToList();

        public bool IsInitialized => true;
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public List<Whiskers.Models.ServerConfig> GetServers() => _servers;
        public List<Whiskers.Models.ServerConfig> GetEnabledServers() => _servers.Where(s => s.Enabled).ToList();
        public Whiskers.Models.ServerConfig? GetServer(string serverId) => _servers.FirstOrDefault(s => s.Id == serverId);
        public Whiskers.Models.ServerConfig? GetDefaultServer() => _servers.FirstOrDefault(s => s.IsDefault) ?? _servers.FirstOrDefault();
        public bool SupportsTerminal(string? serverId) => false;
        public Task AddServerAsync(Whiskers.Models.ServerConfig server) => throw new NotSupportedException();
        public Task UpdateServerAsync(Whiskers.Models.ServerConfig server) => throw new NotSupportedException();
        public Task RemoveServerAsync(string serverId) => throw new NotSupportedException();
        public Task SaveSshKeyAsync(string serverId, string fileName, byte[] keyData) => throw new NotSupportedException();
        public string? GetSshKeyPath(Whiskers.Models.ServerConfig server) => null;
        public Task DeleteSshKeyAsync(string serverId) => throw new NotSupportedException();
    }

    /// <summary>Docker double: serves a fixed fleet, records every log call and answers per
    /// "{serverId}/{containerId}" so a call landing on the wrong host returns nothing.</summary>
    private sealed class FakeDocker : IDockerService
    {
        private readonly List<ContainerInfo> _containers;
        public FakeDocker(params ContainerInfo[] containers) => _containers = containers.ToList();

        public Dictionary<string, string> Logs { get; } = new();
        public HashSet<string> FailingServerIds { get; } = new();
        public ConcurrentBag<LogCall> Calls { get; } = new();
        public List<LogCall> LogCalls => Calls.OrderBy(c => c.ContainerId, StringComparer.Ordinal)
            .ThenBy(c => c.ServerId, StringComparer.Ordinal).ToList();

        public Task<IList<ContainerInfo>> ListAllContainersAsync(bool all = true)
            => Task.FromResult<IList<ContainerInfo>>(_containers.ToList());

        public Task<IList<ContainerInfo>> ListContainersAsync(bool all = true, string? serverId = null)
            => Task.FromResult<IList<ContainerInfo>>(
                _containers.Where(c => c.ServerId == (serverId ?? "local")).ToList());

        public Task<string> GetContainerLogsAsync(string containerId, int tailLines = 100, string? serverId = null, DateTime? since = null)
        {
            Calls.Add(new LogCall(containerId, serverId, since));
            if (FailingServerIds.Contains(serverId ?? "")) throw new InvalidOperationException("host down");
            return Task.FromResult(Logs.TryGetValue($"{serverId}/{containerId}", out var log) ? log : "(no logs available)");
        }

        // --- unused by the monitor ---------------------------------------------------------------------
        public Task<ContainerInfo?> GetContainerAsync(string id, string? serverId = null) => throw new NotSupportedException();
        public Task<ContainerStats?> GetContainerStatsAsync(string containerId, string? serverId = null) => throw new NotSupportedException();
        public Task StartContainerAsync(string containerId, string? serverId = null) => throw new NotSupportedException();
        public Task StopContainerAsync(string containerId, string? serverId = null) => throw new NotSupportedException();
        public Task RestartContainerAsync(string containerId, string? serverId = null) => throw new NotSupportedException();
        public Task RemoveContainerAsync(string containerId, bool force = false, string? serverId = null) => throw new NotSupportedException();
        public Task<string> CreateContainerAsync(DeploymentRequest request, string? serverId = null) => throw new NotSupportedException();
        public Task PullImageAsync(string imageName, IProgress<string>? progress = null, string? serverId = null) => throw new NotSupportedException();
        public Task<(string State, int ExitCode, bool OomKilled)> InspectContainerStateAsync(string containerId, string? serverId = null) => throw new NotSupportedException();
        public Task<ServerSystemInfo> GetServerSystemInfoAsync(string? serverId = null) => throw new NotSupportedException();
        public Task<Dictionary<string, ServerSystemInfo>> GetAllServerSystemInfoAsync() => throw new NotSupportedException();
        public Task<string?> GetImageDigestAsync(string imageRef, string? serverId = null) => throw new NotSupportedException();
        public Task<string> RecreateContainerAsync(string containerId, string? serverId = null, IProgress<string>? progress = null) => throw new NotSupportedException();
        public Task<List<KeyValuePair<string, string>>> GetContainerEnvAsync(string containerId, string? serverId = null) => throw new NotSupportedException();
        public Task<(string ImageId, string ConfigJson)> CaptureRollbackSnapshotAsync(string containerId, string? serverId = null) => throw new NotSupportedException();
        public Task<string> RollbackContainerAsync(string containerName, string imageId, string configJson, string? serverId = null, IProgress<string>? progress = null) => throw new NotSupportedException();
        public Task<IList<NetworkInfo>> ListNetworksAsync(string? serverId = null) => throw new NotSupportedException();
        public Task<string> CreateNetworkAsync(string name, string driver = "bridge", string? serverId = null) => throw new NotSupportedException();
        public Task RemoveNetworkAsync(string networkId, string? serverId = null) => throw new NotSupportedException();
        public Task ConnectContainerToNetworkAsync(string networkId, string containerId, string? serverId = null) => throw new NotSupportedException();
        public Task DisconnectContainerFromNetworkAsync(string networkId, string containerId, string? serverId = null) => throw new NotSupportedException();
        public Task<(string Output, string Error, int ExitCode)> RunHostShellAsync(string command, string? serverId = null, TimeSpan? timeout = null) => throw new NotSupportedException();
    }
}
