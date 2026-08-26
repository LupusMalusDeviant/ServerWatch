# Log hygiene

Why the log-alert scan steps over certain containers, and how that decision is made without guessing.

| File | Purpose |
|---|---|
| `ILogScanExclusions.cs` | The contract: which containers are skipped, with a machine-readable reason and a human-readable justification. |
| `LogScanExclusions.cs` | Access-path detection plus the `SERVERWATCH_SELF_CONTAINERS` override. |

## The feedback loop this ends

Two containers triggered the 2026-08-26 incident: the tunnel and the socket proxy through which Whiskers
reaches Docker. Every request Whiskers makes is a line in their logs, so scanning them means scanning the
record of the scan. Left alone for two weeks they grew to 822 MB between them.

This removes the **trigger**. It does not remove the cause — that was a log fetch which was abandoned rather
than cancelled, fixed under Plan-0001. The distinction is repeated in the alert text and in the MCP report on
purpose, because symptom relief that looks like a cure is how the real fix gets postponed.

## Detected by path, never by name

A container is on the access path because Whiskers **connects to it**: the port it publishes is the port in
this server's configuration, on an address that configuration names. A container that merely happens to be
called `socket-proxy` keeps being scanned — it is somebody else's proxy and its logs are somebody else's
evidence.

The bind address is part of the match. A container on `127.0.0.1:2376` cannot be the thing we reach at
`100.64.0.1:2376`, and treating the port number alone as proof would silently unmonitor a loopback service on
every host that reuses the port.

**Only the outermost hop is knowable.** Where Whiskers talks to a tunnel which talks to a socket proxy, the
tunnel is detected and the proxy behind it is not — nothing in a container list says who talks to whom. The
detection says so in its own justification text and names `SERVERWATCH_SELF_CONTAINERS` as the way to add the
second hop, so an operator is told rather than left to notice.

## The exclusion must not become the blind spot

Excluding a container by mistake is worse than missing a proxy: the container disappears from log monitoring
and looks exactly like one with nothing to report. So every exclusion is visible in three places — the server
view, the `whiskers_log_scan_exclusions` gauge on `/metrics`, and the `get_log_hygiene_report` MCP tool. What
to watch is not the gauge's value but its **movement**: exclusions appearing without a configuration change
mean the detection has grown too greedy.

Exclusion applies to the **log scan only**. These containers stay under health, metric and CVE monitoring —
their log content is worthless to us, their state is not.
