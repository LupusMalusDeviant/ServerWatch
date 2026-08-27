using Whiskers.Models.Cve;

namespace Whiskers.Services.Cve;

/// <summary>In-memory store of the latest CVE scan results per server/container.</summary>
public interface ICveFindingsStore
{
    bool IsScanning { get; set; }
    DateTime? LastScanAt { get; set; }
    void Set(CveScanResult result);
    CveScanResult? Get(string serverId, string? containerId);
    IReadOnlyList<CveScanResult> GetForServer(string serverId);
    IReadOnlyList<CveScanResult> GetAll();
    void Remove(string serverId, string? containerId);

    /// <summary>Removes stored container results of a server whose key is absent from <paramref name="liveKeys"/>
    /// (phantom entries left by recreated/deleted containers). The OS target key is never pruned. Only call
    /// with an authoritative live set (a successful container listing). Returns the count removed.</summary>
    int PruneServer(string serverId, IReadOnlySet<string> liveKeys);

    /// <summary>
    /// Removes every stored result belonging to a server that is no longer configured at all.
    ///
    /// <para><see cref="PruneServer"/> only ever runs for servers that still exist, so results for a REMOVED
    /// server were kept forever: they were reported as current vulnerabilities of a machine that is gone, and
    /// because their identity keys still counted as live, the first-seen ages behind them could never be
    /// pruned either. Found on 2026-08-27, six weeks after the server went away.</para>
    ///
    /// <para>Refuses to do anything when <paramref name="configuredServerIds"/> is empty. An empty set is far
    /// more likely to mean "the server list could not be read" than "the fleet has no servers", and a cleanup
    /// that mistakes one for the other deletes everything.</para>
    /// </summary>
    /// <param name="configuredServerIds">Every configured server id, including disabled ones — disabled is not
    /// removed, and a server switched off for a week must keep its findings and their ages.</param>
    /// <returns>The number of stored results removed.</returns>
    int PruneRemovedServers(IReadOnlySet<string> configuredServerIds);

    void Clear();
    Task SaveAsync();
    CveSummary SummarizeServer(string serverId);

    /// <summary>De-duplicates every finding into one <see cref="CveGroup"/> per CVE-ID, listing all the
    /// real affected (server, container/OS, package) instances behind it. <paramref name="firstSeen"/>
    /// maps a finding's IdentityKey to when it was first detected (for the age indicator);
    /// <paramref name="serverNames"/> maps server id → display name.</summary>
    IReadOnlyList<CveGroup> BuildGroups(
        IReadOnlyDictionary<string, DateTime> firstSeen,
        IReadOnlyDictionary<string, string> serverNames);
}
