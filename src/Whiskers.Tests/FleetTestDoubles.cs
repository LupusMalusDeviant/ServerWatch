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

/// <summary>Builds a REAL <see cref="Whiskers.Services.Docker.Budget.ServerBudget"/> for tests. Deliberately
/// not a fake: the point of the budget is the limiting, and a double that always says yes would let a broken
/// limit pass every test.</summary>
internal static class TestBudget
{
    public static Whiskers.Services.Docker.Budget.IServerBudget Create(int background = 4, int interactive = 4) =>
        new Whiskers.Services.Docker.Budget.ServerBudget(
            new StaticOptionsMonitor<Whiskers.Configuration.ServerBudgetSettings>(
                new Whiskers.Configuration.ServerBudgetSettings
                {
                    BackgroundConcurrency = background,
                    InteractiveConcurrency = interactive
                }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Whiskers.Services.Docker.Budget.ServerBudget>.Instance);
}

internal sealed class StaticOptionsMonitor<T>(T value) : Microsoft.Extensions.Options.IOptionsMonitor<T>
{
    public T CurrentValue { get; } = value;
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
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

    // A queue, not a bag: ConcurrentBag does not preserve insertion order, and a test that needs "the most
    // recent fetch" silently got the oldest one. Ordering matters here, so the type has to provide it.
    public ConcurrentQueue<LogCall> Calls { get; } = new();

    /// <summary>Every log call in the order it was made.</summary>
    public IReadOnlyList<LogCall> CallsInOrder => Calls.ToList();

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

    public Task<IList<ContainerInfo>> ListContainersAsync(bool all = true, string? serverId = null, CancellationToken ct = default)
        => Task.FromResult<IList<ContainerInfo>>(
            _containers.Where(c => c.ServerId == (serverId ?? "local")).ToList());

    /// <summary>How long a log fetch takes. Zero (the default) keeps every existing test synchronous; a value
    /// longer than the monitor's fetch timeout reproduces the wedged host from the 2026-08-26 incident.</summary>
    public TimeSpan FetchDelay { get; set; } = TimeSpan.Zero;

    private readonly ConcurrentDictionary<string, int> _inFlight = new();
    private int _totalInFlight;
    private int _peakTotalInFlight;

    /// <summary>Highest number of log fetches that were in flight at the same time for one container.
    /// Above 1 means an abandoned request kept running while the next cycle started another — the exact
    /// steady state that put dockerd at 184% CPU for six days.</summary>
    public ConcurrentDictionary<string, int> PeakConcurrentPerContainer { get; } = new();

    /// <summary>Highest number of log fetches in flight across the whole fleet at any instant.</summary>
    public int PeakTotalInFlight => Volatile.Read(ref _peakTotalInFlight);

    public async Task<string> GetContainerLogsAsync(string containerId, int tailLines = 100, string? serverId = null, DateTime? since = null, CancellationToken ct = default)
    {
        Calls.Enqueue(new LogCall(containerId, serverId, since));
        if (FailingServerIds.Contains(serverId ?? "")) throw new InvalidOperationException("host down");

        var key = $"{serverId}/{containerId}";
        var perContainer = _inFlight.AddOrUpdate(key, 1, (_, v) => v + 1);
        PeakConcurrentPerContainer.AddOrUpdate(key, perContainer, (_, peak) => Math.Max(peak, perContainer));

        var total = Interlocked.Increment(ref _totalInFlight);
        int seen;
        while (total > (seen = Volatile.Read(ref _peakTotalInFlight)))
            Interlocked.CompareExchange(ref _peakTotalInFlight, total, seen);

        try
        {
            // Stands in for dockerd: an abandoned request only stops if the cancellation actually reaches the
            // backend. Honouring the token here is what makes the load invariants provable either way.
            if (FetchDelay > TimeSpan.Zero) await Task.Delay(FetchDelay, ct);

            var lines = new List<string>();
            if (Logs.TryGetValue(key, out var always)) lines.Add(always);
            if (_timedLines.TryGetValue(key, out var timed))
                lines.AddRange(timed.Where(l => since == null || l.At >= since.Value).Select(l => l.Text));

            return lines.Count == 0 ? "(no logs available)" : string.Join('\n', lines);
        }
        finally
        {
            _inFlight.AddOrUpdate(key, 0, (_, v) => v - 1);
            Interlocked.Decrement(ref _totalInFlight);
        }
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
    public Task<ServerSystemInfo> GetServerSystemInfoAsync(string? serverId = null, CancellationToken ct = default) => throw new NotSupportedException();
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

/// <summary>A detector that excludes nothing — for tests about other things. The real one is exercised in
/// <c>LogScanExclusionTests</c>; wiring it into every log-monitor test would only add noise.</summary>
internal sealed class NoExclusions : Whiskers.Services.LogMonitor.Hygiene.ILogScanExclusions
{
    public IReadOnlyList<Whiskers.Services.LogMonitor.Hygiene.LogScanExclusion> Evaluate(
        Whiskers.Models.ServerConfig server, IReadOnlyList<Whiskers.Models.ContainerInfo> containers) => [];

    public IReadOnlyList<Whiskers.Services.LogMonitor.Hygiene.LogScanExclusion> Current() => [];
}
