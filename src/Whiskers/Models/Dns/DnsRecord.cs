namespace Whiskers.Models.Dns;

/// <summary>One record of a DNS zone, provider-neutral. <see cref="Name"/> is relative to the zone
/// (<c>"@"</c> = apex, <c>"holler.app"</c> = holler.app.&lt;zone&gt;), never an FQDN.</summary>
public sealed record DnsRecord(
    string? Id,
    string Name,
    string Type,
    string Value,
    int Ttl)
{
    public override string ToString() => $"{Name} {Type} {Value} (TTL {Ttl})";
}

/// <summary>What <c>set_dns_record</c> did.</summary>
public enum DnsSetAction
{
    Created,
    Updated,
    Unchanged,
}

/// <summary>Before/after of an idempotent set, for a human-readable answer.</summary>
public sealed record DnsSetResult(DnsSetAction Action, DnsRecord? Before, DnsRecord After);
