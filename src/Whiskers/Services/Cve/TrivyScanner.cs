using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Whiskers.Configuration;
using Whiskers.Models.Cve;
using Whiskers.Services.Server;
using Whiskers.Utils;

namespace Whiskers.Services.Cve;

/// <summary>
/// Scans a single container image for known CVEs by running Trivy as a one-shot
/// container on the target server. Uses a named Docker volume to persist the
/// Trivy vulnerability DB between scans (only the first scan per server pays the
/// full DB download cost).
/// </summary>
public class TrivyScanner : ITrivyScanner
{
    private readonly IHostCommandExecutor _executor;
    private readonly IOptionsMonitor<CveMonitorSettings> _settings;
    private readonly ILogger<TrivyScanner> _logger;

    private const string CacheVolume = "serverwatch-trivy-cache";

    /// <summary>
    /// Output cap for a Trivy run, raised well above the executor's default 1 MB.
    ///
    /// <para>Trivy's JSON for a large image runs to several megabytes — the Authentik server image measures
    /// 3.4 MB — and the default cap cut it mid-document. The scan then failed with
    /// "'0xE2' is an invalid start of a value", which is the first byte of the executor's truncation marker
    /// and reads as a parser bug. The image quietly stopped being scanned for months.</para>
    ///
    /// <para>16 MB is roughly five times the largest output observed, and still a bound: an image that
    /// exceeds even this is reported as truncated rather than parsed into a partial verdict.</para>
    /// </summary>
    private const int MaxTrivyOutputChars = 16 * 1024 * 1024;

    public TrivyScanner(
        IHostCommandExecutor executor,
        IOptionsMonitor<CveMonitorSettings> settings,
        ILogger<TrivyScanner> logger)
    {
        _executor = executor;
        _settings = settings;
        _logger = logger;
    }

    public async Task<CveScanResult> ScanContainerImageAsync(
        string serverId,
        string containerId,
        string containerName,
        string image,
        CancellationToken ct = default)
    {
        var result = new CveScanResult
        {
            ServerId = serverId,
            Source = CveSource.Container,
            ContainerId = containerId,
            ContainerName = containerName,
            Image = image
        };

        if (string.IsNullOrWhiteSpace(image))
        {
            result.Error = "image is empty";
            return result;
        }

        var trivyImage = _settings.CurrentValue.TrivyImage;
        // We scan ALL severities and filter at notify/display time. Trivy DB persists in
        // a named volume so subsequent scans are fast.
        // Mount the host Docker socket so Trivy's "docker" image source can scan
        // locally-built images that exist only in the daemon with no registry to pull from
        // (e.g. compose-built app images like `rabenhof-web`, `serverwatch-web`). Without it
        // Trivy fails those with a FATAL "unable to find the specified image". Registry
        // images continue to resolve exactly as before.
        var cmd =
            $"docker run --rm -v {CacheVolume}:/root/.cache/trivy " +
            $"-v /var/run/docker.sock:/var/run/docker.sock {trivyImage} " +
            $"image --format json --quiet --no-progress --timeout 8m " +
            ShellUtils.Quote(image);

        // First scan on a server downloads the Trivy DB (~600 MB compressed) so allow
        // up to 10 minutes. Subsequent scans usually finish in seconds.
        var exec = await _executor.ExecuteAsync(serverId, cmd, TimeSpan.FromMinutes(10), ct,
            maxOutputChars: MaxTrivyOutputChars);
        if (!exec.Success || string.IsNullOrWhiteSpace(exec.Output))
        {
            result.Error = $"trivy exit {exec.ExitCode}: " +
                Truncate(string.IsNullOrEmpty(exec.Error) ? exec.Output : exec.Error, 400);
            _logger.LogWarning("Trivy scan failed for {Image} on {Server}: {Error}",
                image, serverId, result.Error);
            return result;
        }

        // A cut-off document is a limit that needs raising, not a malformed one that needs a better parser —
        // and the two are indistinguishable once the JSON reader has spoken. Saying so before parsing is the
        // whole difference between a fixable report and six months of a silently unscanned image.
        if (exec.OutputTruncated)
        {
            result.Error = $"trivy output exceeded {MaxTrivyOutputChars / (1024 * 1024)} MB and was cut off — " +
                "the scan is incomplete and was discarded, not parsed";
            _logger.LogWarning(
                "Trivy output for {Image} on {Server} exceeded the {Cap} MB cap and was cut off. No findings " +
                "were taken from this run; raise MaxTrivyOutputChars.",
                image, serverId, MaxTrivyOutputChars / (1024 * 1024));
            return result;
        }

        try
        {
            var root = JsonNode.Parse(exec.Output);
            var results = root?["Results"]?.AsArray();
            if (results == null)
                return result;

            // The image's OS family+name (e.g. "debian 12"), so we can show what the CVE actually applies to.
            var osMeta = root?["Metadata"]?["OS"];
            var osContext = osMeta is null
                ? null
                : string.Join(' ', new[] { TryString(osMeta, "Family"), TryString(osMeta, "Name") }
                    .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            if (string.IsNullOrWhiteSpace(osContext)) osContext = null;

            foreach (var r in results)
            {
                var vulns = r?["Vulnerabilities"]?.AsArray();
                if (vulns == null) continue;
                foreach (var v in vulns)
                {
                    if (v == null) continue;
                    var cveId = TryString(v, "VulnerabilityID");
                    if (string.IsNullOrEmpty(cveId)) continue;

                    result.Findings.Add(new CveFinding
                    {
                        ServerId = serverId,
                        Source = CveSource.Container,
                        ContainerId = containerId,
                        ContainerName = containerName,
                        Image = image,
                        OsContext = osContext,
                        CveId = cveId,
                        Package = TryString(v, "PkgName") ?? "",
                        InstalledVersion = TryString(v, "InstalledVersion"),
                        FixedVersion = TryString(v, "FixedVersion"),
                        Title = TryString(v, "Title"),
                        Reference = TryString(v, "PrimaryURL"),
                        PublishedDate = TryDate(v, "PublishedDate"),
                        Severity = ParseSeverity(TryString(v, "Severity"))
                    });
                }
            }

            _logger.LogInformation("Trivy scan {Image} on {Server}: {Count} findings",
                image, serverId, result.Findings.Count);
        }
        catch (Exception ex)
        {
            result.Error = $"parse failed: {ex.Message}";
            _logger.LogWarning(ex, "Failed to parse Trivy JSON for {Image} on {Server}", image, serverId);
        }

        return result;
    }

    private static string? TryString(JsonNode? node, string prop)
    {
        var val = node?[prop];
        if (val is null) return null;
        try { return val.GetValue<string>(); }
        catch { return val.ToString(); }
    }

    private static DateTime? TryDate(JsonNode? node, string prop)
    {
        var s = TryString(node, prop);
        return DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var dt) ? dt : null;
    }

    private static CveSeverity ParseSeverity(string? s) => s?.ToUpperInvariant() switch
    {
        "CRITICAL" => CveSeverity.Critical,
        "HIGH" => CveSeverity.High,
        "MEDIUM" => CveSeverity.Medium,
        "LOW" => CveSeverity.Low,
        _ => CveSeverity.Unknown
    };

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + "…";
    }
}
