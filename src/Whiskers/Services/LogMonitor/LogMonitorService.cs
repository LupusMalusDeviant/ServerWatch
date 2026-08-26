using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Whiskers.Models;
using Whiskers.Services.Docker;
using Whiskers.Services.Notifications;
using Whiskers.Services.Persistence;
using Whiskers.Services.ServerConfig;

namespace Whiskers.Services.LogMonitor;

/// <summary>
/// Background service that periodically checks container logs against alert rules.
/// Scans EVERY configured Docker server, not just the default one — a rule without a container filter
/// means "all containers of the fleet", the same scope <see cref="HealthMonitor.ContainerHealthMonitor"/>
/// has always used.
/// </summary>
public class LogMonitorService : BackgroundService, ILogMonitorService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDockerService _docker;
    private readonly IServerConfigService _serverConfig;
    private readonly INotificationService _notifications;
    private readonly ILogger<LogMonitorService> _logger;
    private readonly Docker.Budget.IServerBudget _budget;
    private readonly ConcurrentDictionary<string, DateTime> _cooldowns = new();
    // Per-container timestamp of the last log check, so we fetch only NEW lines and an old ERROR line
    // doesn't re-alert every cycle. Keyed by "{serverId}:{containerId}" (see CompositeKey): container ids
    // are only unique per host, so a fleet-wide scan must not let two hosts share one entry.
    private readonly ConcurrentDictionary<string, DateTime> _lastLogCheck = new();

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);

    // A single wedged Docker connection must not stall the cycle forever: hosts are scanned in parallel,
    // but an unbounded fetch would still hold its own chain — and with it the start of the next cycle.
    private static readonly TimeSpan DefaultLogFetchTimeout = TimeSpan.FromSeconds(15);

    // Instance field only so a test can drive many cycles against a deliberately slow backend without
    // waiting 15 seconds each time. Production behaviour is unchanged: the constructor defaults to
    // DefaultLogFetchTimeout, and nothing outside the tests passes anything else.
    private readonly TimeSpan _logFetchTimeout;

    // Lines fetched per container per cycle. Since the Docker call now caps the transfer even with a
    // `since` filter, this is the real ceiling on how much of a burst we can still match in one cycle.
    private const int TailLines = 200;

    // Our own container must never be scanned for log alerts. Whiskers logs its own
    // "Log alert triggered: … {matchedLine}" and "Trivy scan failed … FATAL" lines; when an
    // "all containers" rule reads those back they re-match the pattern and create a
    // self-amplifying trigger loop (this is what ran the "Echte Fehler" rule up to 133×).
    // Self-monitoring, if ever wanted, must be a deliberate out-of-band mechanism, not this.
    // Override the excluded name(s) via SERVERWATCH_SELF_CONTAINERS (comma-separated).
    private static readonly HashSet<string> SelfContainerNames = new(
        (Environment.GetEnvironmentVariable("SERVERWATCH_SELF_CONTAINERS") ?? "serverwatch")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        StringComparer.OrdinalIgnoreCase);

    public LogMonitorService(
        IServiceScopeFactory scopeFactory,
        IDockerService docker,
        IServerConfigService serverConfig,
        INotificationService notifications,
        ILogger<LogMonitorService> logger,
        Docker.Budget.IServerBudget budget,
        TimeSpan? logFetchTimeout = null)
    {
        _scopeFactory = scopeFactory;
        _docker = docker;
        _serverConfig = serverConfig;
        _notifications = notifications;
        _logger = logger;
        _budget = budget;
        _logFetchTimeout = logFetchTimeout ?? DefaultLogFetchTimeout;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Log monitor service started. Check interval: {Interval}s", CheckInterval.TotalSeconds);
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // initial delay

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // The token now reaches the Docker calls themselves, so a stop ends the in-flight fetches
                // rather than merely abandoning them. WaitAsync stays as the backstop that bounds the cycle
                // even if some future step forgets to honour the token.
                await RunScanCycleAsync(stoppingToken).WaitAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Log monitor check failed");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    /// <summary>Runs one scan cycle over the whole fleet. Public so a test can drive a single cycle
    /// without the hosted-service loop (same test seam idea as ContainerHealthMonitor.IsRestart).</summary>
    public async Task RunScanCycleAsync(CancellationToken ct)
    {
        // Everything this cycle does is background work: it shares the background lane of the per-server
        // budget with the other loops and must never take slots from a waiting human (Plan-0001 WP3).
        using var background = _budget.BackgroundScope();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetricsDbContext>();

        var rules = await db.LogAlertRules.Where(r => r.Enabled).ToListAsync(ct);
        if (rules.Count == 0) return;

        // Compile the regex rules once per cycle (keyed by pattern) instead of re-parsing each pattern
        // for every log line of every container. Invalid patterns are dropped here with a warning.
        var compiledRegexes = new Dictionary<string, Regex>();
        foreach (var r in rules.Where(r => r.IsRegex))
        {
            if (compiledRegexes.ContainsKey(r.Pattern)) continue;
            try { compiledRegexes[r.Pattern] = new Regex(r.Pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException ex) { _logger.LogWarning(ex, "Invalid log-alert regex '{Pattern}' — skipped", r.Pattern); }
        }

        // The fleet-wide list: ListContainersAsync() without a server id only ever returns the DEFAULT
        // server's containers, which silently limited every "all containers" rule to one host. The
        // detailed variant also reports which hosts answered — needed for the pruning below.
        var listing = await _docker.ListAllContainersDetailedAsync(all: false);
        var containers = listing.Containers;
        var selfServerIds = ResolveSelfServerIds();
        var hits = new ConcurrentQueue<LogAlertHit>();

        // One chain per server: hosts in parallel so remote latency doesn't add up over the fleet, the
        // containers of a host sequentially so we don't fan a burst of log requests at one connection.
        await Task.WhenAll(containers
            .GroupBy(c => c.ServerId, StringComparer.OrdinalIgnoreCase)
            .Select(g => ScanServerAsync(g.ToList(), rules, compiledRegexes, selfServerIds, hits, ct)));

        // Rule bookkeeping and notifications happen back here, on the cycle's own thread: MetricsDbContext
        // is not thread-safe, and the notification order shouldn't depend on which host answered first.
        foreach (var hit in hits)
        {
            hit.Rule.LastTriggered = DateTime.UtcNow;
            hit.Rule.TriggerCount++;

            await _notifications.SendAsync(new NotificationEvent
            {
                ContainerId = hit.Container.Id,
                ContainerName = hit.Container.Name,
                Image = hit.Container.Image,
                ServerId = hit.Container.ServerId,
                ServerName = hit.Container.ServerName,
                EventType = $"log_alert:{hit.Rule.Severity}",
                // The detail line every channel renders (NotificationFormatter.Detail): which rule fired,
                // on which host (container names repeat across a fleet) and the line that matched. The
                // matched line is third-party text — the Matrix HTML body escapes it (MatrixNotification-
                // Service.HtmlEscaped), the other channels are plain text.
                ImageInfo = $"{hit.Rule.Name} · {hit.Container.Name} @ {hit.Container.ServerName} — {hit.MatchedLine}",
                // Abuse RestartCount field for trigger count
                RestartCount = hit.Rule.TriggerCount
            });

            _logger.LogWarning("Log alert triggered: {RuleName} on {Container} ({Server}) — {Line}",
                hit.Rule.Name, hit.Container.Name, hit.Container.ServerName, hit.MatchedLine);
        }

        if (!hits.IsEmpty) await db.SaveChangesAsync(ct);

        // Bound the per-container maps: drop entries for containers no longer in the list — but ONLY for
        // servers that actually answered. An unreachable host contributes an empty list; dropping its
        // watermarks would re-baseline it to "now" on recovery, so every line written during the outage
        // would be silently skipped.
        var liveKeys = containers.Select(CompositeKey).ToHashSet();
        foreach (var kv in _cooldowns.ToArray())
        {
            var parts = kv.Key.Split(':', 2); // "ruleId:serverId:containerId"
            if (parts.Length == 2 && !liveKeys.Contains(parts[1]) && listing.MayPruneStateFor(ServerOfKey(parts[1])))
                _cooldowns.TryRemove(kv.Key, out _);
        }
        foreach (var key in _lastLogCheck.Keys)
            if (!liveKeys.Contains(key) && listing.MayPruneStateFor(ServerOfKey(key)))
                _lastLogCheck.TryRemove(key, out _);
    }

    /// <summary>Scans one server's containers sequentially and records the matches; the caller applies
    /// them to the database.</summary>
    private async Task ScanServerAsync(
        IReadOnlyList<ContainerInfo> containers,
        IReadOnlyList<LogAlertRuleEntity> rules,
        IReadOnlyDictionary<string, Regex> compiledRegexes,
        IReadOnlySet<string> selfServerIds,
        ConcurrentQueue<LogAlertHit> hits,
        CancellationToken ct)
    {
        int scanned = 0, noRules = 0, failed = 0;

        foreach (var container in containers)
        {
            if (ct.IsCancellationRequested) break;

            // Never scan our own logs — breaks the self-amplifying alert feedback loop.
            if (IsSelfContainer(container, selfServerIds)) continue;

            var applicableRules = rules.Where(r => RuleApplies(r, container)).ToList();
            if (applicableRules.Count == 0) { noRules++; continue; }
            scanned++;

            var key = CompositeKey(container);

            try
            {
                // Fetch only lines since our last check so an old ERROR line doesn't re-alert every cycle;
                // on first sight, baseline to now so historical logs aren't alerted. The watermark is taken
                // BEFORE the fetch: with remote hosts the round trip is long enough that lines written
                // during it would otherwise fall into neither window. A line seen twice at the window edge
                // is caught by the rule cooldown; a line lost is lost for good.
                var fetchedAt = DateTime.UtcNow;
                var since = _lastLogCheck.TryGetValue(key, out var last) ? last : fetchedAt;
                var logs = await FetchLogsAsync(container, since, ct);
                _lastLogCheck[key] = fetchedAt;
                var lines = logs.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                // Per-container trace of what the scan actually saw. Without it, "no alert" cannot be told
                // apart from "not scanned", "nothing new" or "read failed" — a bad place to be for the
                // component whose whole job is noticing things.
                _logger.LogDebug("Scanned {Container} on {Server}: {Lines} new line(s) since {Since:HH:mm:ss}, {Rules} rule(s)",
                    container.Name, container.ServerName, lines.Length, since, applicableRules.Count);

                foreach (var rule in applicableRules)
                {
                    // Cooldown check — per rule AND per container, and the container is only identified by
                    // server + id: two hosts running a same-named container must not share one cooldown.
                    var cooldownKey = $"{rule.RuleId}:{key}";
                    if (_cooldowns.TryGetValue(cooldownKey, out var lastTriggered) &&
                        DateTime.UtcNow - lastTriggered < TimeSpan.FromMinutes(rule.CooldownMinutes))
                        continue;

                    // Pattern match
                    string? matchedLine = null;

                    foreach (var line in lines)
                    {
                        bool hit = rule.IsRegex
                            ? compiledRegexes.TryGetValue(rule.Pattern, out var rx) && rx.IsMatch(line)
                            : line.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase);

                        if (hit)
                        {
                            matchedLine = line.Length > 200 ? line[..200] : line;
                            break;
                        }
                    }

                    if (matchedLine != null)
                    {
                        _cooldowns[cooldownKey] = DateTime.UtcNow;
                        hits.Enqueue(new LogAlertHit(rule, container, matchedLine));
                    }
                }
            }
            catch (Exception ex)
            {
                // No watermark update on failure — the next cycle retries the same window.
                failed++;
                _logger.LogDebug(ex, "Failed to check logs for {Container} on {Server}", container.Name, container.ServerName);
            }
        }

        _logger.LogDebug("Scan of {Server} done: {Scanned} scanned, {NoRules} without a matching rule, {Failed} failed, of {Total}",
            containers.FirstOrDefault()?.ServerName ?? "?", scanned, noRules, failed, containers.Count);
    }

    /// <summary>Fetches a container's new log lines, bounded by <see cref="DefaultLogFetchTimeout"/>.
    ///
    /// <para>The bound is a linked <see cref="CancellationTokenSource"/>, not a race against a timer. The
    /// earlier <c>Task.WhenAny(fetch, Task.Delay(...))</c> ended the <em>wait</em> and left the <em>request</em>
    /// running: dockerd kept reading the log file until the proxy cut the connection 600 seconds later, while
    /// a new fetch started every cycle. Ten concurrent full-log scans per container was the stable end state —
    /// 184% of 200% CPU for six days (incident 2026-08-26). Cancelling the token instead ends the request on
    /// the server as well, which is the whole point.</para></summary>
    private async Task<string> FetchLogsAsync(ContainerInfo container, DateTime since, CancellationToken ct)
    {
        using var fetchTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        fetchTimeout.CancelAfter(_logFetchTimeout);

        try
        {
            return await _docker.GetContainerLogsAsync(container.Id, TailLines, container.ServerId, since, fetchTimeout.Token);
        }
        // Only OUR timeout is turned into a failure for this container. A cancelled shutdown token belongs to
        // the caller and must keep propagating, or a stop would look like a fleet of broken containers.
        catch (OperationCanceledException) when (fetchTimeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Log fetch for {Container} on {Server} aborted after {Timeout}s — request cancelled on the server, not just abandoned here",
                container.Name, container.ServerName, _logFetchTimeout.TotalSeconds);
            throw new TimeoutException($"Log fetch for {container.Name} on {container.ServerName} timed out.");
        }
    }

    /// <summary>Identifies a container across the fleet. Container ids are unique per host only, so every
    /// per-container map has to be keyed by server + id (same scheme as ContainerHealthMonitor).</summary>
    public static string CompositeKey(ContainerInfo container) => $"{container.ServerId}:{container.Id}";

    /// <summary>The server part of a <see cref="CompositeKey"/>.</summary>
    public static string ServerOfKey(string compositeKey) => compositeKey.Split(':', 2)[0];

    /// <summary>A rule applies to a container when it carries no filter at all ("all containers"), or when
    /// its filter matches by id or by name.
    /// <para>A NAME-only filter is the normal case — both the UI dialog and the MCP tool set ContainerName
    /// and leave ContainerId null — so the "no filter" test has to look at BOTH fields. Testing ContainerId
    /// alone (as this did) short-circuited every name-filtered rule into an all-containers rule; harmless
    /// while the scan saw one host, an alert storm once it sees the whole fleet.</para>
    /// <para>A name filter deliberately matches on EVERY server: the UI picker lists the containers of all
    /// servers but stores only the name, so a name rule means "this workload, wherever it runs". Pinning a
    /// rule to one server would need a new column on LogAlertRuleEntity.</para></summary>
    public static bool RuleApplies(LogAlertRuleEntity rule, ContainerInfo container) =>
        (rule.ContainerId == null && rule.ContainerName == null)
        || rule.ContainerId == container.Id
        || rule.ContainerName == container.Name;

    /// <summary>The self-log guard, restricted to the host we ourselves run on. A container that merely
    /// shares our name on a REMOTE host is a different process and must stay monitored.</summary>
    public static bool IsSelfContainer(ContainerInfo container, IReadOnlySet<string> selfServerIds) =>
        selfServerIds.Contains(container.ServerId) && SelfContainerNames.Contains(container.Name);

    /// <summary>The server(s) whose containers can be our OWN container: Whiskers reaches its own host
    /// through the <see cref="ConnectionType.Local"/> entry. If the fleet has no local server (an all-mTLS
    /// or Kubernetes setup, where our own container may not be visible at all), fall back to the default
    /// server — that is exactly the host the old single-server scan used to look at.</summary>
    public static IReadOnlySet<string> ResolveSelfServerIds(IServerConfigService serverConfig)
    {
        var ids = serverConfig.GetServers()
            .Where(s => s.ConnectionType == ConnectionType.Local)
            .Select(s => s.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ids.Count == 0 && serverConfig.GetDefaultServer() is { } fallback)
            ids.Add(fallback.Id);

        return ids;
    }

    private IReadOnlySet<string> ResolveSelfServerIds() => ResolveSelfServerIds(_serverConfig);

    /// <summary>A pattern match found while scanning; applied to the rule stats and notifications
    /// afterwards, off the parallel scan.</summary>
    private sealed record LogAlertHit(LogAlertRuleEntity Rule, ContainerInfo Container, string? MatchedLine);

    // === Public API ===

    public async Task<List<LogAlertRuleEntity>> GetRulesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetricsDbContext>();
        return await db.LogAlertRules.OrderBy(r => r.Name).ToListAsync();
    }

    public async Task<LogAlertRuleEntity> CreateRuleAsync(LogAlertRuleEntity rule)
    {
        // Validate regex. The timeout is defense-in-depth only — this compiles (it does not match), and the
        // actual match paths (LogSearchService and the monitor loop) already run every pattern under a timeout.
        if (rule.IsRegex)
            _ = new Regex(rule.Pattern, RegexOptions.None, TimeSpan.FromSeconds(1)); // throws on invalid

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetricsDbContext>();
        db.LogAlertRules.Add(rule);
        await db.SaveChangesAsync();
        return rule;
    }

    public async Task DeleteRuleAsync(string ruleId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetricsDbContext>();
        var rule = await db.LogAlertRules.FirstOrDefaultAsync(r => r.RuleId == ruleId);
        if (rule != null)
        {
            db.LogAlertRules.Remove(rule);
            await db.SaveChangesAsync();
        }
    }

    public async Task ToggleRuleAsync(string ruleId, bool enabled)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetricsDbContext>();
        var rule = await db.LogAlertRules.FirstOrDefaultAsync(r => r.RuleId == ruleId);
        if (rule != null)
        {
            rule.Enabled = enabled;
            await db.SaveChangesAsync();
        }
    }
}
