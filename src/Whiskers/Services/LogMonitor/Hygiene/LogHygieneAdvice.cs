using System.Globalization;
using Whiskers.Models;

namespace Whiskers.Services.LogMonitor.Hygiene;

/// <summary>How serious one log-file finding is (Plan-0007 WP4).</summary>
public enum LogHygieneSeverity
{
    /// <summary>Nothing to say about this container.</summary>
    None,

    /// <summary>No rotation limit, but the file is still small. Worth listing, not worth waking anyone —
    /// WP4.1 is explicit that this stays an inventory entry.</summary>
    Note,

    /// <summary>No rotation limit and the file has grown into a share of the disk that matters. This is the
    /// state the 2026-08-26 incident reached unnoticed.</summary>
    Alert
}

/// <summary>
/// Turns a log-file finding into something an operator can act on (Plan-0007 WP4).
///
/// <para>Two rules shape the wording. First, the threshold is <b>relative to the free disk</b>: 100 MB is
/// nothing on one host and a quarter of what is left on another, so an absolute number would be wrong almost
/// everywhere. Second, every message says out loud that this fixes the <b>trigger and not the cause</b> — the
/// cause was a log fetch that was abandoned rather than cancelled. Symptom relief that reads like a cure is
/// how the real fix gets postponed.</para>
///
/// <para><b>Nothing here executes anything.</b> Setting a rotation limit recreates the container, which is a
/// decision with downtime attached. The finding hands over the exact command and stops.</para>
/// </summary>
public static class LogHygieneAdvice
{
    /// <summary>The share of the remaining disk at which a note becomes an alert. From the Plan-0007 risk
    /// table: below this the operator has days of warning, which is the point.</summary>
    public const double AlertShareOfFreeDisk = 0.25;

    /// <summary>A floor for hosts whose free space could not be read. Without a denominator there is no share
    /// to compare, and staying silent about a 1 GB log because <c>df</c> failed would be the worse error.</summary>
    public const long AlertBytesWithoutDiskInfo = 1024L * 1024 * 1024;

    public static LogHygieneSeverity Severity(LogInventoryEntry entry)
    {
        if (!entry.IsUnbounded) return LogHygieneSeverity.None;

        if (entry.ShareOfFreeDisk is { } share)
            return share >= AlertShareOfFreeDisk ? LogHygieneSeverity.Alert : LogHygieneSeverity.Note;

        return entry.SizeBytes >= AlertBytesWithoutDiskInfo ? LogHygieneSeverity.Alert : LogHygieneSeverity.Note;
    }

    /// <summary>The finding in one paragraph: what is wrong, how fast it is getting worse, and how long there
    /// is left. The time-to-full estimate is the number that makes people act.</summary>
    public static string Describe(LogInventoryEntry entry, string serverName)
    {
        var size = Humanise(entry.SizeBytes);
        var text = $"{entry.ContainerName} on {serverName} has no log rotation limit and its log file is {size}.";

        if (entry.GrowthBytesPerDay is { } growth and > 0)
        {
            text += $" It is growing by about {Humanise((long)growth)} per day";

            if (entry.FreeDiskBytes is { } free && growth > 0)
            {
                var days = free / growth;
                text += days < 90
                    ? $", which fills the remaining disk in roughly {days:0} day{(days < 1.5 ? "" : "s")}."
                    : ".";
            }
            else text += ".";
        }
        else
        {
            // One reading is a size, not a trend. Saying so beats implying a trend we have not measured.
            text += " No growth rate yet — that needs a second reading, tomorrow.";
        }

        if (entry.ShareOfFreeDisk is { } share)
            text += $" It currently accounts for {share * 100:0.#}% of the space that would otherwise be free.";

        return text;
    }

    /// <summary>The remediation, verbatim and runnable (WP4.3), plus the fleet-wide default (WP4.5).
    ///
    /// <para>Two commands rather than one, because they do different things: the first stops this container
    /// from growing again, the second stops the next container from ever starting without a limit. Both say
    /// that the container is recreated — an operator who discovers that afterwards will not trust the next
    /// suggestion.</para></summary>
    public static string Remediation(LogInventoryEntry entry, IReadOnlyDictionary<string, string> labels)
    {
        var lines = new List<string>();

        var project = labels.TryGetValue("com.docker.compose.project", out var p) ? p : null;
        var service = labels.TryGetValue("com.docker.compose.service", out var s) ? s : null;
        var workingDir = labels.TryGetValue("com.docker.compose.project.working_dir", out var w) ? w : null;

        if (project is not null && service is not null)
        {
            lines.Add("This container is managed by Docker Compose, so the limit belongs in the compose file:");
            lines.Add("");
            lines.Add($"    services:");
            lines.Add($"      {service}:");
            lines.Add($"        logging:");
            lines.Add($"          driver: json-file");
            lines.Add($"          options:");
            lines.Add($"            max-size: \"50m\"");
            lines.Add($"            max-file: \"3\"");
            lines.Add("");
            lines.Add("Then apply it — this RECREATES the container, so the service restarts:");
            lines.Add("");
            lines.Add(workingDir is not null
                ? $"    cd {Whiskers.Utils.ShellUtils.Quote(workingDir)} && docker compose up -d --force-recreate {service}"
                : $"    docker compose -p {project} up -d --force-recreate {service}");
        }
        else
        {
            lines.Add("This container is not managed by Compose. Docker cannot change a running container's log");
            lines.Add("options, so it has to be recreated with the limit set — check how it was started first,");
            lines.Add("because recreating it by hand loses any option not repeated on the new command line:");
            lines.Add("");
            lines.Add($"    docker inspect {entry.ContainerName}");
            lines.Add($"    # then re-run it with: --log-opt max-size=50m --log-opt max-file=3");
        }

        lines.Add("");
        lines.Add("To stop this happening to the NEXT container on this host, set a default in");
        lines.Add("/etc/docker/daemon.json and restart Docker (running containers keep their current setting):");
        lines.Add("");
        lines.Add("    { \"log-driver\": \"json-file\", \"log-opts\": { \"max-size\": \"50m\", \"max-file\": \"3\" } }");
        lines.Add("");
        lines.Add("    sudo systemctl restart docker");

        return string.Join('\n', lines);
    }

    /// <summary>The sentence that keeps this from being mistaken for a fix (WP4.4).</summary>
    public const string TriggerNotCause =
        "Setting a rotation limit removes the TRIGGER of the 2026-08-26 incident, not its cause. The cause was " +
        "a log fetch that was abandoned instead of cancelled, so requests piled up on the host while the logs " +
        "they were reading grew without bound. That fix lives in the load budget and cancellation work; this " +
        "one only stops the disk filling while it lands.";

    public static string Humanise(long? bytes) => bytes switch
    {
        null => "unknown",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => (bytes.Value / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB",
        < 1024L * 1024 * 1024 => (bytes.Value / (1024.0 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB",
        _ => (bytes.Value / (1024.0 * 1024 * 1024)).ToString("0.##", CultureInfo.InvariantCulture) + " GB"
    };
}
