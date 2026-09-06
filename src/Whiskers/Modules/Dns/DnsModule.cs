using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Whiskers.Configuration;
using Whiskers.Mcp.Tools;
using Whiskers.Services.Dns;

namespace Whiskers.Modules.Dns;

/// <summary>DNS zone records at an external provider (Infomaniak today): the <c>list/set/delete_dns_record</c>
/// MCP tools and the <i>Settings → DNS</i> panel. No page, no nav entry — the operator's workflow is "tell
/// the agent". Enabled by default but inert until a token is configured: without one every tool answers a
/// hint instead of calling out. Adding a provider = one more <see cref="IDnsProviderClient"/> registration;
/// <see cref="DnsRecordService"/> picks the one matching <c>DnsSettings.Provider</c>.</summary>
public sealed class DnsModule : IWhiskersModule
{
    public string Id => "dns";
    public string DisplayName => "DNS";
    public bool EnabledByDefault => true;
    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public IReadOnlyList<NavItem> NavItems => Array.Empty<NavItem>();

    public IReadOnlyList<Type> McpToolTypes { get; } = new[] { typeof(DnsTools) };

    public Task InitializeAsync(IServiceProvider sp, CancellationToken ct) => Task.CompletedTask;

    public void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<DnsSettings>(config.GetSection(DnsSettings.SectionName));

        // Rotating primary handler, like the cloud clients: a long-lived client must re-resolve DNS now and then.
        services.AddHttpClient<IDnsProviderClient, InfomaniakDnsClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton<IDnsRecordService, DnsRecordService>();
    }
}
