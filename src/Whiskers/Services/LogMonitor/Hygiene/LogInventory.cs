using System.Collections.Concurrent;
using System.Globalization;
using Whiskers.Models;
using Whiskers.Services.Docker;
using Whiskers.Services.Server;
using Whiskers.Utils;

namespace Whiskers.Services.LogMonitor.Hygiene;

/// <summary>The daily log-file inventory. See <see cref="ILogInventory"/> for why it exists.</summary>
public sealed class LogInventory : ILogInventory
{
    private readonly IDockerService _docker;
    private readonly IHostCommandExecutor _host;
    private readonly ILogger<LogInventory> _logger;

    // Keyed "{serverId}|{containerId}". The previous reading is what turns a size into a growth rate, and the
    // growth rate is what turns "this log is big" into "this log will be a problem on Thursday".
    private readonly ConcurrentDictionary<string, (long Bytes, DateTime At)> _previous = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<LogInventoryEntry>> _latest = new(StringComparer.OrdinalIgnoreCase);

    public LogInventory(IDockerService docker, IHostCommandExecutor host, ILogger<LogInventory> logger)
    {
        _docker = docker;
        _host = host;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LogInventoryEntry>> SurveyAsync(
        Models.ServerConfig server, IReadOnlyList<ContainerInfo> containers, CancellationToken ct = default)
    {
        var freeDisk = await FreeDiskBytesAsync(server.Id, ct);
        var entries = new List<LogInventoryEntry>(containers.Count);

        foreach (var container in containers)
        {
            if (ct.IsCancellationRequested) break;
            entries.Add(await SurveyContainerAsync(server, container, freeDisk, ct));
        }

        _latest[server.Id] = entries;
        return entries;
    }

    public IReadOnlyList<LogInventoryEntry> Current() =>
        _latest.Values.SelectMany(v => v)
            .OrderBy(e => e.ServerId, StringComparer.Ordinal)
            .ThenBy(e => e.ContainerName, StringComparer.Ordinal)
            .ToList();

    private async Task<LogInventoryEntry> SurveyContainerAsync(
        Models.ServerConfig server, ContainerInfo container, long? freeDisk, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        ContainerLogConfiguration? config = null;
        string? unknown = null;

        try
        {
            config = await _docker.GetLogConfigurationAsync(container.Id, server.Id);
        }
        catch (Exception ex)
        {
            // A container that vanished between listing and inspection is ordinary, not a fault worth an
            // alert. What is NOT ordinary is pretending we know something about it.
            unknown = "the container could not be inspected";
            _logger.LogDebug(ex, "Log configuration for {Container} on {Server} unreadable", container.Name, server.Name);
        }

        long? size = null;
        if (unknown is null)
        {
            if (config is null)
                unknown = "Docker reported no log configuration";
            else if (!config.WritesToHostDisk)
                unknown = $"the {config.Driver} driver does not write a file on this host";
            else if (string.IsNullOrWhiteSpace(config.LogPath))
                unknown = "Docker reported no log path";
            else
                (size, unknown) = await ReadSizeAsync(server.Id, config.LogPath!, ct);
        }

        var key = $"{server.Id}|{container.Id}";
        double? growth = null;

        if (size is { } bytes)
        {
            if (_previous.TryGetValue(key, out var before))
            {
                var elapsed = (now - before.At).TotalDays;
                // Two readings a few seconds apart produce an absurd per-day figure. Below an hour the answer
                // is "not yet", which is honest; a wild number would be acted on as if it meant something.
                if (elapsed >= 1.0 / 24)
                    growth = Math.Max(0, bytes - before.Bytes) / elapsed;
            }

            _previous[key] = (bytes, now);
        }

        return new LogInventoryEntry(
            server.Id, container.Id, container.Name, config, size, unknown, growth, freeDisk, now);
    }

    /// <summary>Reads the size of the log file on the host. Returns (null, reason) whenever it cannot —
    /// WP3.2: report <em>unknown</em>, never estimate.</summary>
    private async Task<(long? Size, string? Unknown)> ReadSizeAsync(string serverId, string logPath, CancellationToken ct)
    {
        try
        {
            // stat, not du: one syscall, no directory walk, and no chance of an expensive traversal on a host
            // that is already under pressure. The path comes from Docker, but it is still quoted — a path is
            // data, and data that reaches a shell unquoted is an injection waiting for an odd filename.
            var quoted = ShellUtils.Quote(logPath);
            var result = await _host.ExecuteAsync(serverId, $"stat -c %s {quoted}", TimeSpan.FromSeconds(15), ct);

            if (!result.Success)
                return (null, "the log file size is not readable from here (host access refused or unavailable)");

            var text = result.Output.Trim();
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes)
                ? (bytes, null)
                : (null, "the host returned no readable size");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the log size for {Path} on {Server}", logPath, serverId);
            return (null, "the log file size could not be read");
        }
    }

    /// <summary>Free bytes on the filesystem holding the Docker logs. Null when unreadable — which downgrades
    /// the finding to "no rotation limit" rather than inventing a denominator.</summary>
    private async Task<long?> FreeDiskBytesAsync(string serverId, CancellationToken ct)
    {
        try
        {
            var result = await _host.ExecuteAsync(
                serverId, "df -B1 --output=avail /var/lib/docker | tail -1", TimeSpan.FromSeconds(15), ct);

            if (!result.Success) return null;
            return long.TryParse(result.Output.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var free)
                ? free
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the free disk space on {Server}", serverId);
            return null;
        }
    }
}
