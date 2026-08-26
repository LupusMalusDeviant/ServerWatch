using System.Reflection;
using System.Text;
using ModelContextProtocol.Server;

namespace Whiskers.Mcp;

/// <summary>One tool as its own source declares it: the wire name derived from the method, and the level from
/// <see cref="McpToolLevelAttribute"/> (null when the method carries none — that is the case the tests reject).
/// <paramref name="ModuleId"/> and <paramref name="Description"/> are filled in by the module-aware overload of
/// <see cref="McpToolLevelCatalog.Declarations(IEnumerable{Whiskers.Modules.IWhiskersModule})"/>.</summary>
public sealed record McpToolDeclaration(
    string ToolName,
    string MethodName,
    string DeclaringType,
    string? Level,
    string? ModuleId = null,
    string? Description = null);

/// <summary>
/// Reads the permission levels the tool methods declare and turns them into the same shape as
/// <see cref="Whiskers.Models.McpPermissionLevels.DefaultToolLevels"/>, so the two can be compared.
///
/// <para>Used by <c>McpToolLevelTests</c> today and by the tool-catalog snapshot (Plan-0013 WP3) later. It is
/// deliberately NOT wired into the request path: the permission check keeps reading the literal dictionary, so
/// this reflection can never decide access at runtime. A reflection bug here fails a test, not a request.</para>
/// </summary>
public static class McpToolLevelCatalog
{
    /// <summary>Every method carrying <c>[McpServerTool]</c> in the given types, with its declared level.</summary>
    public static IReadOnlyList<McpToolDeclaration> Declarations(IEnumerable<Type> toolTypes) =>
        toolTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(m => new McpToolDeclaration(
                ToolName: ResolveToolName(m),
                MethodName: m.Name,
                DeclaringType: m.DeclaringType?.Name ?? "?",
                Level: m.GetCustomAttribute<McpToolLevelAttribute>()?.Level))
            .OrderBy(d => d.ToolName, StringComparer.Ordinal)
            .ToList();

    /// <summary>The same, but attributed to the module that contributes each tool and carrying the description
    /// the agent sees. This is what the published catalog is rendered from.</summary>
    public static IReadOnlyList<McpToolDeclaration> Declarations(IEnumerable<Whiskers.Modules.IWhiskersModule> modules) =>
        modules
            .SelectMany(m => Declarations(m.McpToolTypes).Select(d => d with
            {
                ModuleId = m.Id,
                Description = DescriptionOf(d, m.McpToolTypes)
            }))
            .OrderBy(d => d.ToolName, StringComparer.Ordinal)
            .ToList();

    private static string? DescriptionOf(McpToolDeclaration declaration, IEnumerable<Type> toolTypes) =>
        toolTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .FirstOrDefault(m => m.Name == declaration.MethodName && m.DeclaringType?.Name == declaration.DeclaringType)
            ?.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()
            ?.Description;

    /// <summary>The declared levels as a tool-name → level map. Methods without a level are left out; callers
    /// that care about completeness use <see cref="Undeclared"/>, which is the whole point of this type.</summary>
    public static Dictionary<string, string> DeclaredLevels(IEnumerable<Type> toolTypes) =>
        Declarations(toolTypes)
            .Where(d => d.Level is not null)
            .ToDictionary(d => d.ToolName, d => d.Level!, StringComparer.Ordinal);

    /// <summary>Tools that declare no level. Every entry here would fall back to admin at runtime and be
    /// unreachable for the agent — so this list must stay empty.</summary>
    public static IReadOnlyList<McpToolDeclaration> Undeclared(IEnumerable<Type> toolTypes) =>
        Declarations(toolTypes).Where(d => d.Level is null).ToList();

    /// <summary>The name the tool is registered under: the attribute's explicit <c>Name</c> when set, otherwise
    /// the method name in snake_case — mirroring how the MCP SDK derives it. A test pins this against the names
    /// the built server actually reports, so the mirroring cannot drift with an SDK update.</summary>
    public static string ResolveToolName(MethodInfo method) =>
        method.GetCustomAttribute<McpServerToolAttribute>()?.Name ?? ToSnakeCase(method.Name);

    /// <summary><c>GetContainerLogs</c> → <c>get_container_logs</c>, <c>CloudHardReset</c> → <c>cloud_hard_reset</c>.</summary>
    public static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
