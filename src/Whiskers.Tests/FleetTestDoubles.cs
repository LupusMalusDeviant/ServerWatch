using System.Collections.Concurrent;
using Whiskers.Models;
using Whiskers.Services.Docker;
using Whiskers.Services.Notifications;
using Whiskers.Services.ServerConfig;

namespace Whiskers.Tests;

/// <summary>One recorded <c>GetContainerLogsAsync</c> call.</summary>
internal sealed record LogCall(string ContainerId, string? ServerId, DateTime? Since);

/// <summary>Collects the events a producer sent.</summary>
internal sealed class FakeNotifications : INotificationService
{
    public List<NotificationEvent> Events { get; } = new();
    public Task SendAsync(NotificationEvent evt) { Events.Add(evt); return Task.CompletedTask; }
    public Task SendTestAsync() => Task.CompletedTask;
}

/// <summary>A fixed server registry.</summary>
internal sealed class FakeServerConfig : IServerConfigService
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

/// <summary>
/// Docker double for the fleet-wide monitors: serves a fixed set of containers, can make individual
/// servers silent (listing) or their log fetches fail, records every log call, and — for the entries
/// added via <see cref="AddTimedLine"/> — honours the <c>since</c> watermark like the real daemon does.
/// </summary>
internal sealed class FakeDocker : IDockerService
{
    private readonly List<ContainerInfo> _containers;
    private readonly Dictionary<string, List<(DateTime At, string Text)>> _timedLines = new();

    public FakeDocker(params ContainerInfo[] containers) => _containers = containers.ToList();

    /// <summary>Lines returned regardless of the <c>since</c> watermark, keyed "{serverId}/{containerId}".</summary>
    public Dictionary<string, string> Logs { get; } = new();

    /// <summary>Servers whose container LISTING fails — the host is silent, as in a tailnet outage.</summary>
    public HashSet<string> UnreachableServerIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Servers that list fine but whose log fetch throws.</summary>
    public HashSet<string> FailingServerIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentBag<LogCall> Calls { get; } = new();

    public List<LogCall> LogCalls => Calls.OrderBy(c => c.ContainerId, StringComparer.Ordinal)
        .ThenBy(c => c.ServerId, StringComparer.Ordinal).ToList();

    /// <summary>Adds a log line with a timestamp, so <c>since</c> filtering is exercised.</summary>
    public void AddTimedLine(string serverId, string containerId, DateTime at, string text)
    {
        var key = $"{serverId}/{containerId}";
        if (!_timedLines.TryGetValue(key, out var list)) _timedLines[key] = list = new();
        list.Add((at, text));
    }

    public Task<FleetContainerListing> ListAllContainersDetailedAsync(bool all = true)
    {
        var reachable = _containers.Where(c => !UnreachableServerIds.Contains(c.ServerId)).ToList();
        var serverNames = _containers.GroupBy(c => c.ServerId)
            .ToDictionary(g => g.Key, g => g.First().ServerName, StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(new FleetContainerListing
        {
            Containers = reachable,
            RespondedServerIds = serverNames.Keys.Where(id => !UnreachableServerIds.Contains(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            FailedServers = UnreachableServerIds
                .Select(id => new FleetServerFailure(id, serverNames.GetValueOrDefault(id, id), "Connection failed"))
                .ToList()
        });
    }

    public async Task<IList<ContainerInfo>> ListAllContainersAsync(bool all = true)
        => (await ListAllContainersDetailedAsync(all)).Containers.ToList();

    public Task<IList<ContainerInfo>> ListContainersAsync(bool all = true, string? serverId = null)
        => Task.FromResult<IList<ContainerInfo>>(
            _containers.Where(c => c.ServerId == (serverId ?? "local")).ToList());

    public Task<string> GetContainerLogsAsync(string containerId, int tailLines = 100, string? serverId = null, DateTime? since = null)
    {
        Calls.Add(new LogCall(containerId, serverId, since));
        if (FailingServerIds.Contains(serverId ?? "")) throw new InvalidOperationException("host down");

        var key = $"{serverId}/{containerId}";
        var lines = new List<string>();
        if (Logs.TryGetValue(key, out var always)) lines.Add(always);
        if (_timedLines.TryGetValue(key, out var timed))
            lines.AddRange(timed.Where(l => since == null || l.At >= since.Value).Select(l => l.Text));

        return Task.FromResult(lines.Count == 0 ? "(no logs available)" : string.Join('\n', lines));
    }

    // --- not used by the monitors ----------------------------------------------------------------------
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
