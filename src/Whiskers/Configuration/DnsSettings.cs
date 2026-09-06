namespace Whiskers.Configuration;

/// <summary>Global DNS-provider access (one account, many zones) — unlike cloud credentials, which are
/// per-server. Persisted via <c>IAppSettingsStore</c> under the <c>"Dns"</c> section of app-settings.json and
/// editable in <i>Settings → DNS</i>. The token is a secret: never log it, never return it from a tool.</summary>
public class DnsSettings
{
    public const string SectionName = "Dns";

    /// <summary>Provider id. Only <c>"infomaniak"</c> is implemented today.</summary>
    public string Provider { get; set; } = "infomaniak";

    /// <summary>Provider API token (Infomaniak: a token with the <c>domain</c> scope).</summary>
    public string ApiToken { get; set; } = "";

    /// <summary>Zones the tools may touch. Empty = every zone the token can reach. A non-empty list is the
    /// blast-radius fence: a typo in the zone argument then fails instead of editing a neighbouring domain.</summary>
    public List<string> AllowedZones { get; set; } = new();

    /// <summary>TTL applied when a caller passes none (seconds).</summary>
    public int DefaultTtl { get; set; } = 300;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiToken);
}
