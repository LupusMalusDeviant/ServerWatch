using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Whiskers.Services.Observability.SelfMetrics;

namespace Whiskers.Tests;

/// <summary>
/// The <c>whiskers_self_*</c> series on the real endpoint (Plan-0003 WP3).
///
/// <para>Whiskers exported the container inventory of the whole fleet and not one number about itself. These
/// tests boot the real application and read <c>/metrics</c>, because "the exporter compiles" and "the series
/// are served" are different claims — and it was the second one that failed unnoticed for the MCP surface
/// from 0.12.0 to 0.13.0.</para>
/// </summary>
[Collection("WebAppBoot")] // serialized: the boot needs the process-wide WHISKERS_DATA_DIR env var
public class SelfMetricsEndpointTests
{
    private const string ScrapeToken = "test-scrape-token";

    private static async Task WithAppAsync(Func<HttpClient, IServiceProvider, Task> body)
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "whiskers-selfmetrics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        var previous = Environment.GetEnvironmentVariable("WHISKERS_DATA_DIR");
        Environment.SetEnvironmentVariable("WHISKERS_DATA_DIR", dataDir);

        try
        {
            await using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(b =>
                {
                    b.UseEnvironment("Development");
                    b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                        new List<KeyValuePair<string, string?>>
                        {
                            new("Auth:Disabled", "true"),
                            new("Metrics:ScrapeToken", ScrapeToken)
                        }));
                });

            using var client = factory.CreateClient();
            await body(client, factory.Services);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WHISKERS_DATA_DIR", previous);
            try { Directory.Delete(dataDir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    private static async Task<string> ScrapeAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ScrapeToken);

        var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, $"/metrics answered {(int)response.StatusCode}");
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task The_endpoint_carries_the_loop_health_series() => await WithAppAsync(async (client, services) =>
    {
        var metrics = services.GetRequiredService<ISelfMetrics>();
        metrics.RecordCycle("logmonitor", "badwolf", TimeSpan.FromSeconds(2), success: true);
        metrics.RecordSkip("cve", "k3s-cluster", "Kubernetes server, Docker-only loop");
        metrics.Count("log_fetch_timeouts", "badwolf");

        var body = await ScrapeAsync(client);

        Assert.Contains("whiskers_self_loop_last_success_age_seconds", body);
        Assert.Contains("loop=\"logmonitor\",server=\"badwolf\"", body);
        Assert.Contains("whiskers_self_log_fetch_timeouts_total", body);

        // A server a loop deliberately skips must still be visible. Absent, "not monitored here" reads
        // exactly like "nothing to report" — the confusion this whole package exists to remove.
        Assert.Contains("server=\"k3s-cluster\"", body);
        Assert.Contains("result=\"skipped\"", body);
    });

    [Fact]
    public async Task The_self_series_survive_a_fleet_that_answers_nothing() => await WithAppAsync(async (client, _) =>
    {
        // No Docker host is reachable in the test environment, so the inventory section fails. The self
        // metrics must still be there — they are exactly what you need when the fleet has gone silent, and
        // putting them behind the inventory would have made them disappear at the worst moment.
        var body = await ScrapeAsync(client);

        Assert.Contains("whiskers_self_", body);
    });

    [Fact]
    public async Task The_endpoint_stays_shut_without_the_token() => await WithAppAsync(async (client, _) =>
    {
        var response = await client.GetAsync("/metrics");

        // The payload now says how Whiskers behaves and which servers exist. It must not become readable
        // just because it grew more interesting.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    });
}
