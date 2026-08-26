using Whiskers.Models;

namespace Whiskers.Mcp;

/// <summary>
/// Declares the minimum permission level a tool requires, right next to the tool itself.
///
/// <para>Why this exists: the level used to live only in <see cref="McpPermissionLevels.DefaultToolLevels"/>,
/// a hand-maintained dictionary. <c>McpPermissionCheck</c> resolves an unlisted tool to
/// <see cref="McpPermissionLevels.Admin"/> (fail-closed), so a tool whose dictionary entry someone forgot is
/// registered, appears in <c>tools/list</c>, and is then <b>always</b> denied to the in-process agent — whose
/// ceiling is <c>write</c> by default. It looks present and never is. Declaring the level on the method makes
/// that omission impossible to ship: <c>McpToolLevelTests</c> fails the build when a tool carries no level.</para>
///
/// <para>This attribute is a <b>declaration</b>, not the enforcement path. Runtime behaviour is unchanged —
/// <c>McpPermissionCheck</c> still reads <see cref="McpPermissionLevels.DefaultToolLevels"/>. The two are kept
/// in lockstep by a test that compares them element for element in both directions, so the dictionary can never
/// silently drift from what the tools declare.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpToolLevelAttribute : Attribute
{
    /// <param name="level">One of <see cref="McpPermissionLevels.Read"/>, <see cref="McpPermissionLevels.Write"/>
    /// or <see cref="McpPermissionLevels.Admin"/>. Anything else is rejected at construction rather than
    /// silently normalised — a typo must not quietly widen or narrow access.</param>
    public McpToolLevelAttribute(string level)
    {
        if (level != McpPermissionLevels.Read && level != McpPermissionLevels.Write && level != McpPermissionLevels.Admin)
            throw new ArgumentException(
                $"'{level}' is not a permission level. Use McpPermissionLevels.Read/Write/Admin.", nameof(level));
        Level = level;
    }

    public string Level { get; }
}
