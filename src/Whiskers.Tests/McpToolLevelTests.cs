using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Whiskers.Mcp;
using Whiskers.Models;
using Whiskers.Modules;

namespace Whiskers.Tests;

/// <summary>
/// Guards the permission level of every MCP tool (Plan-0013 WP1).
///
/// <para>The failure this prevents is a quiet one: <c>McpPermissionCheck</c> resolves a tool that has no entry in
/// <see cref="McpPermissionLevels.DefaultToolLevels"/> to <c>admin</c>. That is the right fail-closed default, but
/// it means a forgotten entry produces a tool that is registered, listed in <c>tools/list</c>, and then denied to
/// the in-process agent on every single call — whose ceiling is <c>write</c>. Nothing errors, nothing logs, the
/// tool simply never works. These tests turn that omission into a red build.</para>
/// </summary>
public class McpToolLevelTests
{
    // Same source the server uses: the enabled modules' tool types (default configuration = all modules on).
    private static List<Type> ToolTypes() =>
        ModuleCatalog.DiscoverEnabled(new ConfigurationBuilder().Build())
            .SelectMany(m => m.McpToolTypes)
            .Distinct()
            .ToList();

    [Fact]
    public void Every_registered_tool_declares_its_permission_level()
    {
        var undeclared = McpToolLevelCatalog.Undeclared(ToolTypes());

        Assert.True(undeclared.Count == 0,
            "These tools carry no [McpToolLevel] and would fall back to admin — registered, listed, and " +
            "unreachable for the agent:\n  " +
            string.Join("\n  ", undeclared.Select(d => $"{d.DeclaringType}.{d.MethodName} -> {d.ToolName}")));
    }

    [Fact]
    public void Declared_levels_match_the_runtime_dictionary_exactly()
    {
        var declared = McpToolLevelCatalog.DeclaredLevels(ToolTypes());
        var runtime = McpPermissionLevels.DefaultToolLevels;

        // Both directions, element for element. A one-sided check would let the dictionary keep a stale entry
        // for a tool that no longer exists, or miss a tool that quietly declares a different level.
        var mismatches = declared
            .Where(kv => !runtime.TryGetValue(kv.Key, out var lvl) || lvl != kv.Value)
            .Select(kv => $"{kv.Key}: declared '{kv.Value}', dictionary '{(runtime.TryGetValue(kv.Key, out var l) ? l : "<missing>")}'")
            .ToList();

        var stale = runtime.Keys
            .Where(k => !declared.ContainsKey(k))
            .Select(k => $"{k}: in the dictionary, but no tool declares it")
            .ToList();

        Assert.True(mismatches.Count == 0 && stale.Count == 0,
            "The declared levels and McpPermissionLevels.DefaultToolLevels have drifted apart:\n  " +
            string.Join("\n  ", mismatches.Concat(stale)));
    }

    [Fact]
    public void Declared_tool_names_match_the_names_the_server_registers()
    {
        // The level is keyed by the wire name. If the name this catalog derives ever stops matching the name the
        // SDK registers — an SDK naming change, or an explicit Name on the attribute — every level lookup for
        // that tool misses and lands on the admin fallback. Pin the two together against the built server.
        var services = new ServiceCollection();
        services.AddLogging();
        IEnumerable<Type> toolTypes = ToolTypes();       // IEnumerable<Type>, never Type[] — see McpToolRegistrationTests
        services.AddMcpServer().WithTools(toolTypes);

        var registered = services.BuildServiceProvider()
            .GetServices<McpServerTool>()
            .Select(t => t.ProtocolTool.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var derived = McpToolLevelCatalog.Declarations(ToolTypes())
            .Select(d => d.ToolName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(registered, derived);
    }

    [Fact]
    public void Level_attribute_rejects_an_unknown_level()
    {
        // A typo must not be normalised into some level silently — it has to fail loudly at construction.
        Assert.Throws<ArgumentException>(() => new McpToolLevelAttribute("readonly"));
        Assert.Throws<ArgumentException>(() => new McpToolLevelAttribute(""));
    }

    [Fact]
    public void Every_tool_method_gates_on_its_own_tool_name()
    {
        // The third place the tool name occurs (Plan-0013 WP2.4). Two of them are now pinned: method name →
        // wire name, and wire name → level. The third is the string literal the method hands to the permission
        // gate, and it is hand-written. Misspell it — "list_container" — and the lookup misses, the level falls
        // back to admin, and the tool is denied to the agent on every call with nothing logged. Only the source
        // can be asked about that literal, so this test reads it.
        //
        // The assertion is deliberately "the method mentions its own tool name", not "it calls CheckAccess with
        // it": CloudTools routes six tools through a private Guarded(...) helper that takes the name as an
        // argument, which is a good pattern and must not be broken by a test that only knows one call shape.
        var toolsDir = Path.Combine(RepositoryRoot(), "src", "Whiskers", "Mcp", "Tools");
        var files = Directory.GetFiles(toolsDir, "*.cs");

        // A source scanner that finds nothing and reports success is the very failure mode this package exists
        // to remove, so an empty scan is a failure, not a pass.
        Assert.True(files.Length > 0, $"no tool sources found under {toolsDir}");

        var toolAttribute = new Regex(@"\[McpServerTool(?!Type)", RegexOptions.Compiled);
        var signature = new Regex(
            @"public\s+static\s+(?:async\s+)?[A-Za-z0-9_<>\?\[\],\. ]+?\s+([A-Za-z0-9_]+)\s*\(",
            RegexOptions.Compiled);

        var scanned = 0;
        var problems = new List<string>();

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            var starts = toolAttribute.Matches(source).Select(m => m.Index).ToList();
            for (var i = 0; i < starts.Count; i++)
            {
                var end = i + 1 < starts.Count ? starts[i + 1] : source.Length;
                var body = source[starts[i]..end];

                var name = signature.Match(body);
                if (!name.Success) continue;

                scanned++;
                var toolName = McpToolLevelCatalog.ToSnakeCase(name.Groups[1].Value);
                if (!body.Contains($"\"{toolName}\"", StringComparison.Ordinal))
                    problems.Add($"{Path.GetFileName(file)}.{name.Groups[1].Value}: never mentions its own tool name \"{toolName}\" — the permission gate is keyed on a different string");
            }
        }

        Assert.True(problems.Count == 0, "Tool name and permission gate have drifted apart:\n  " + string.Join("\n  ", problems));

        // Guards the scanner itself: if the parsing quietly stopped matching, the loop above would report a
        // clean run over nothing.
        Assert.Equal(McpToolLevelCatalog.Declarations(ToolTypes()).Count, scanned);
    }

    /// <summary>Walks up from the test binary to the directory holding <c>Whiskers.slnx</c>.</summary>
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Whiskers.slnx")))
            dir = dir.Parent;

        Assert.True(dir is not null, $"could not find Whiskers.slnx above {AppContext.BaseDirectory}");
        return dir!.FullName;
    }

    [Theory]
    [InlineData("ListContainers", "list_containers")]
    [InlineData("GetContainerLogs", "get_container_logs")]
    [InlineData("CloudHardReset", "cloud_hard_reset")]
    [InlineData("HetznerEnableRescue", "hetzner_enable_rescue")]
    public void Snake_case_conversion_matches_the_wire_names(string method, string expected)
        => Assert.Equal(expected, McpToolLevelCatalog.ToSnakeCase(method));
}
