# Observability

Two separate things live here: what Whiskers **did** (governance recording, below) and whether Whiskers is
**still working at all** (`SelfMetrics/` and `ScanSupervisor.cs`).

## Is Whiskers still working?

| File | Purpose |
|---|---|
| `SelfMetrics/` | Loop health per (loop, server): last success, last attempt, cycle duration, failures, and **skips with a reason**. Exported as `whiskers_self_*` on `/metrics`. See [`SelfMetrics/ISelfMetrics.cs`](SelfMetrics/ISelfMetrics.cs). |
| `ScanSupervisor.cs` | Watches the watchers: raises `monitoring_stalled` when a loop has not completed a cycle for a server in three of its own intervals — whatever the cause. |

Whiskers exported the container inventory of the whole fleet and not one number about itself. On 2026-08-26
the log monitor wrote "timed out after 15s" into every cycle for six days; nothing counted it, so nothing
could act on it, and the host sat at 98% CPU until a person happened to look at a dashboard.

Two design points are worth keeping:

- **The age of the last success is the number that matters**, not a failure counter. Failures are only
  produced while something still happens; a loop that has stopped produces nothing at all.
- **A skipped server must stay visible.** Four loops filter Kubernetes hosts out. Absent from the metrics,
  "this loop does not cover that server" reads exactly like "that server has nothing wrong with it".

The supervisor makes a deliberately *weaker* claim than the specific guards — it only knows that nothing has
been reported — and is therefore far harder to defeat. It must never become suppressible by the mechanisms it
supervises: a switch that can silence the alarm about being silent is not a switch, it is a blindfold.

## What Whiskers did

Governance recording for the agent/MCP layer, **every** tool call Whiskers executes is captured here so it can be reviewed in the **Agent-History** dashboard (`/agent-history`).

## What gets recorded

For each call: timestamp, actor + actor type, tool name, required permission level, secret-redacted parameters, the guardrail verdict (`allow` / `confirm` / `deny`), success, duration, an optional result summary, server id and any error. The entity is [`McpToolCallEntity`](../../Models/McpToolCall.cs); it lives in the `McpToolCalls` table of [`MetricsDbContext`](../Persistence/MetricsDbContext.cs) with the same 90-day retention as the audit log (pruned in [`MetricsCollectorService`](../Metrics/MetricsCollectorService.cs)).

## Two recording points (so nothing is missed)

| Path | Recorded by | Actor type |
|---|---|---|
| In-process agent (web chat, `instruct_agent`, AI triggers) | [`AgentToolInvoker`](../Agent/AgentToolInvoker.cs): at every return path, with full params + verdict + result + duration | `agent-web`, `agent-mcp`, `trigger` |
| External / direct MCP `tools/call` (e.g. Claude Code calling a tool itself) | [`McpCallLogMiddleware`](../../Mcp/McpCallLogMiddleware.cs): sniffs the JSON-RPC body on `POST /mcp` | `mcp-direct` |

## Files

| File | Purpose |
|---|---|
| `McpCallLogStore.cs` | `IMcpCallLogStore` + `McpCallLogStore`, records and queries tool-call entries. Writes through a scoped `MetricsDbContext` (safe to call from singletons). Query filters: actor, tool, verdict, writes-only, since. |

## Related

- Dashboard: [`../../Components/Pages/AgentHistory.razor`](../../Components/Pages/AgentHistory.razor)
- Secret redaction: [`../../Utils/SecretRedactor.cs`](../../Utils/)
- Guardrail verdicts: [`../Agent/Guardrails/`](../Agent/Guardrails/)
