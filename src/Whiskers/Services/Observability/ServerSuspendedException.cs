namespace Whiskers.Services.Observability;

/// <summary>
/// Thrown when a background caller tries to reach a server whose loops are paused (Plan-0005 WP1).
///
/// <para>Its own type, not a generic failure: a paused server is not a broken one, and the loops have to be
/// able to tell those apart. Reporting a pause as a failure would page someone about the very thing they just
/// switched off, and would bury the announcement that the pause already made.</para>
/// </summary>
public sealed class ServerSuspendedException : Exception
{
    public string ServerId { get; }

    public ServerSuspendedException(string serverId, string reason)
        : base($"Background checks for server '{serverId}' are paused ({reason}).")
    {
        ServerId = serverId;
    }
}
