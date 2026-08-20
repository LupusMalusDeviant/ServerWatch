# Services/HealthMonitor

Background **container health watching**. Continuously tracks container health state across the fleet, raises notifications on transitions (unhealthy / stopped / OOM / restart loops), watches whether the servers themselves still answer, and keeps a short health history for the UI's health reports.

## Files

| File | Purpose |
|---|---|
| `ContainerHealthMonitor.cs` | The background service: polls container health, detects state transitions and restart loops, and fires notifications (the per-container mute/prefs are applied centrally in the composite notification service). |
| `ServerReachabilityTracker.cs` | Turns the per-cycle "which servers answered" signal into `server_unreachable` / `server_recovered` events, with a consecutive-failure threshold (`HealthMonitor:ServerUnreachableCycles`, default 2) so a single slow cycle is not an outage. |
| `IHealthStore.cs` / `InMemoryHealthStore.cs` | Stores recent health state/history per container for reports and the dashboard. |

## Why reachability lives here

A host that stops answering used to be silent: the dashboard marked it unreachable, but nothing was sent —
and every container alert and log-alert rule covering that host quietly stopped producing anything, which
is indistinguishable from "all quiet". The monitor already lists the whole fleet each cycle, so it gets the
signal for free via `ListAllContainersDetailedAsync`.

The same signal guards the pruning of the per-container maps: state belonging to a server that did **not**
answer is kept, otherwise a recovered host looks brand new and its outage window is never evaluated.

## Related

- Notification dispatch + per-container prefs: [`../Notifications/`](../Notifications/)
- UI: [`../../Components/Pages/HealthReports.razor`](../../Components/Pages/HealthReports.razor), [`Dashboard.razor`](../../Components/Pages/Dashboard.razor)
- MCP tool: `get_health_summary`
