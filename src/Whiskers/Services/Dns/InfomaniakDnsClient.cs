using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Whiskers.Models.Dns;

namespace Whiskers.Services.Dns;

/// <summary>
/// Client for the Infomaniak DNS zone API (<c>https://api.infomaniak.com/2/zones/{zone}/records</c>,
/// Bearer token with the <c>domain</c> scope). Every response is an envelope
/// <c>{"result":"success","data":…}</c> or <c>{"result":"error","error":{"code":…,"description":…}}</c> —
/// and an error can arrive with HTTP 200, so <see cref="ReadEnvelopeAsync"/> checks both the status and the
/// <c>result</c> field. Record fields on the wire: <c>id</c>, <c>source</c> (zone-relative label),
/// <c>type</c>, <c>target</c>, <c>ttl</c>.
///
/// <para>Apex spelling: the API returns <c>"."</c> as the source of an apex record while the Manager UI leaves
/// the field blank; this client accepts <c>""</c>, <c>"."</c> and <c>"@"</c> on the way in and writes
/// <see cref="ApexSource"/> on the way out, so callers only ever see <c>"@"</c>.</para>
/// </summary>
public sealed class InfomaniakDnsClient : IDnsProviderClient
{
    public const string Id = "infomaniak";
    public const string BaseUrl = "https://api.infomaniak.com";

    /// <summary>What we send as <c>source</c> for a record at the zone apex — mirrors what the API itself
    /// returns for such records.</summary>
    public const string ApexSource = ".";

    private readonly HttpClient _http;
    private readonly ILogger<InfomaniakDnsClient> _logger;

    public InfomaniakDnsClient(HttpClient http, ILogger<InfomaniakDnsClient> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderId => Id;

    public async Task<List<DnsRecord>> ListRecordsAsync(string token, string zone, CancellationToken ct = default)
    {
        using var req = Build(token, HttpMethod.Get, $"/2/zones/{Uri.EscapeDataString(zone)}/records");
        using var resp = await _http.SendAsync(req, ct);
        var data = await ReadEnvelopeAsync(resp, ct);
        if (data.ValueKind != JsonValueKind.Array) return new();

        var list = new List<DnsRecord>();
        foreach (var el in data.EnumerateArray())
        {
            var rec = ToRecord(el);
            if (rec is not null) list.Add(rec);
        }
        return list;
    }

    public async Task<DnsRecord> CreateRecordAsync(string token, string zone, DnsRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var req = Build(token, HttpMethod.Post, $"/2/zones/{Uri.EscapeDataString(zone)}/records");
        req.Content = JsonContent.Create(new
        {
            source = ToSource(record.Name),
            type = record.Type,
            target = record.Value,
            ttl = record.Ttl,
        });
        using var resp = await _http.SendAsync(req, ct);
        var data = await ReadEnvelopeAsync(resp, ct);
        // The create answer is sometimes the bare record, sometimes just {"id": n}; fall back to what we sent.
        return ToRecord(data) ?? record with { Id = IdOf(data) };
    }

    public async Task<DnsRecord> UpdateRecordAsync(string token, string zone, string recordId, DnsRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(recordId)) throw new ArgumentException("recordId required", nameof(recordId));

        using var req = Build(token, HttpMethod.Put,
            $"/2/zones/{Uri.EscapeDataString(zone)}/records/{Uri.EscapeDataString(recordId)}");
        // Only value and TTL change; name and type are the identity of the record we matched on.
        req.Content = JsonContent.Create(new { target = record.Value, ttl = record.Ttl });
        using var resp = await _http.SendAsync(req, ct);
        var data = await ReadEnvelopeAsync(resp, ct);
        return ToRecord(data) ?? record with { Id = recordId };
    }

    public async Task DeleteRecordAsync(string token, string zone, string recordId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(recordId)) throw new ArgumentException("recordId required", nameof(recordId));

        using var req = Build(token, HttpMethod.Delete,
            $"/2/zones/{Uri.EscapeDataString(zone)}/records/{Uri.EscapeDataString(recordId)}");
        using var resp = await _http.SendAsync(req, ct);
        await ReadEnvelopeAsync(resp, ct);
    }

    // --- wire helpers -------------------------------------------------------------------------------------

    private static HttpRequestMessage Build(string token, HttpMethod method, string path)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new DnsProviderException("Infomaniak-API-Token ist nicht konfiguriert (Einstellungen → DNS).");

        var req = new HttpRequestMessage(method, BaseUrl + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    /// <summary>Unwraps the envelope. Throws <see cref="DnsProviderException"/> with HTTP status + the API's
    /// own description on any failure — HTTP error, <c>result != success</c>, or a body that is not JSON.</summary>
    private async Task<JsonElement> ReadEnvelopeAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var status = (int)resp.StatusCode;
        var body = await resp.Content.ReadAsStringAsync(ct);

        JsonDocument? doc = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(body)) doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            // Not JSON (a proxy page, an HTML error): report the status and a bounded excerpt.
        }

        using (doc)
        {
            JsonElement? root = doc?.RootElement;
            var result = root is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty("result", out var res)
                ? res.GetString()
                : null;

            if (!resp.IsSuccessStatusCode || (result is not null && !string.Equals(result, "success", StringComparison.OrdinalIgnoreCase)))
            {
                var detail = DescribeError(root, body);
                _logger.LogWarning("Infomaniak DNS API answered {Status} for {Method} {Path}: {Detail}",
                    status, resp.RequestMessage?.Method, resp.RequestMessage?.RequestUri?.AbsolutePath, detail);
                throw new DnsProviderException(
                    $"Infomaniak-API-Fehler (HTTP {status}){Hint(status)}: {detail}", status);
            }

            if (root is { ValueKind: JsonValueKind.Object } obj && obj.TryGetProperty("data", out var data))
                return data.Clone();
            return root?.Clone() ?? default;
        }
    }

    private static string Hint(int status) => status switch
    {
        401 => " — Token ungültig oder abgelaufen",
        403 => " — Token ohne 'domain'-Berechtigung oder Zone gehört nicht zu diesem Konto",
        404 => " — Zone oder Eintrag nicht gefunden",
        429 => " — Ratelimit erreicht",
        _ => "",
    };

    /// <summary>The API's error text, whatever shape it comes in: <c>error.description</c>, <c>error.message</c>,
    /// <c>error.code</c>, a plain <c>error</c> string, or, failing all that, the raw body (bounded).</summary>
    private static string DescribeError(JsonElement? root, string body)
    {
        if (root is { ValueKind: JsonValueKind.Object } obj && obj.TryGetProperty("error", out var err))
        {
            if (err.ValueKind == JsonValueKind.String) return err.GetString() ?? "(leer)";
            if (err.ValueKind == JsonValueKind.Object)
            {
                var parts = new List<string>();
                foreach (var key in new[] { "description", "message" })
                    if (err.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                        parts.Add(v.GetString()!);
                if (err.TryGetProperty("code", out var code))
                    parts.Add($"[{code}]");
                if (err.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                    foreach (var e in errors.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
                            parts.Add(d.GetString()!);
                if (parts.Count > 0) return string.Join(" ", parts);
            }
        }
        var excerpt = body.Trim();
        if (excerpt.Length > 300) excerpt = excerpt[..300] + "…";
        return string.IsNullOrEmpty(excerpt) ? "(keine Fehlermeldung)" : excerpt;
    }

    private static string? IdOf(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("id", out var id))
            return id.ValueKind == JsonValueKind.Number ? id.GetRawText() : id.GetString();
        // A bare number as the whole "data" (seen for creates).
        if (el.ValueKind == JsonValueKind.Number) return el.GetRawText();
        return null;
    }

    /// <summary>A wire record → <see cref="DnsRecord"/>, or null when the element is not a full record
    /// (e.g. a create answer carrying only the id).</summary>
    internal static DnsRecord? ToRecord(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty("type", out var type) || !el.TryGetProperty("target", out var target)) return null;

        var source = el.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        var ttl = el.TryGetProperty("ttl", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;

        return new DnsRecord(
            Id: IdOf(el),
            Name: FromSource(source),
            Type: (type.GetString() ?? "").ToUpperInvariant(),
            Value: target.GetString() ?? "",
            Ttl: ttl);
    }

    /// <summary>Wire source → zone-relative name ("@" for the apex).</summary>
    internal static string FromSource(string? source)
    {
        var s = (source ?? "").Trim().TrimEnd('.');
        return s.Length == 0 || s == "@" ? "@" : s;
    }

    /// <summary>Zone-relative name → wire source.</summary>
    internal static string ToSource(string name) =>
        name == "@" || string.IsNullOrWhiteSpace(name) ? ApexSource : name;
}
