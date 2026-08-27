using Whiskers.Models;

namespace Whiskers.Services.LogMonitor.Hygiene;

/// <summary>One container's log-file situation (Plan-0007 WP3).</summary>
/// <param name="SizeBytes">Null means <em>unknown</em>, never zero and never a guess. The size lives on the
/// host and is not always readable; a made-up number here would be acted on as if it were measured.</param>
/// <param name="UnknownReason">Why the size could not be read, when it could not. An unexplained blank is
/// indistinguishable from a container with an empty log.</param>
/// <param name="GrowthBytesPerDay">Only present once two readings exist. A single reading says how big the
/// log is; it says nothing about whether it is a problem.</param>
/// <param name="FreeDiskBytes">What the size is judged against. 100 MB is trivial on one host and a quarter
/// of the remaining disk on another, and an absolute threshold would be wrong on both.</param>
public sealed record LogInventoryEntry(
    string ServerId,
    string ContainerId,
    string ContainerName,
    ContainerLogConfiguration? Configuration,
    long? SizeBytes,
    string? UnknownReason,
    double? GrowthBytesPerDay,
    long? FreeDiskBytes,
    DateTime MeasuredAt)
{
    /// <summary>Rotation is missing AND the file is on the host disk AND we actually know its size. All three
    /// have to hold: a driver that ships elsewhere cannot fill this disk, and an unknown size is not evidence
    /// of a large one.</summary>
    public bool IsUnbounded =>
        Configuration is { WritesToHostDisk: true, RotationConfigured: false } && SizeBytes is not null;

    /// <summary>How much of the remaining disk this one log occupies, or null when either number is unknown.</summary>
    public double? ShareOfFreeDisk =>
        SizeBytes is { } size && FreeDiskBytes is { } free && free > 0
            ? (double)size / (size + free)
            : null;
}

/// <summary>
/// The daily log-file inventory (Plan-0007 WP3/WP4).
///
/// <para>Two containers reached 822 MB in a fortnight because nothing was watching for a missing rotation
/// limit. The inventory reports it before the disk does — and reports the size relative to the free space on
/// that host, because an absolute threshold is wrong on every host but one.</para>
///
/// <para><b>It reports; it does not repair.</b> Fixing a missing <c>max-size</c> recreates the container, which
/// is a decision with a downtime attached. The finding comes with the exact command and stops there.</para>
/// </summary>
public interface ILogInventory
{
    /// <summary>Takes a reading for one server. Runs once a day under the load budget: one call per container.</summary>
    Task<IReadOnlyList<LogInventoryEntry>> SurveyAsync(
        Models.ServerConfig server, IReadOnlyList<ContainerInfo> containers, CancellationToken ct = default);

    /// <summary>The latest reading, for the view, the alert and the MCP report.</summary>
    IReadOnlyList<LogInventoryEntry> Current();
}
