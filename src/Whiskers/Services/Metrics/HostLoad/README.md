# Host load

The rules that judge a **host**, not a container.

| File | Purpose |
|---|---|
| `HostSample.cs` | One reading, plus the CPU-scale reconciliation that makes the two figures comparable. |
| `HostLoadEvaluator.cs` | Sustained host CPU/memory, and host load no container accounts for. |

## The gap this closes

Alerts were evaluated per container, plus one server-level rule for disk. `dockerd` runs in no container, so
on 2026-08-26 Whiskers recorded roughly 8,900 measurements of BurgCloud over six days — practically every one
above 98% CPU — and judged none of them. It was noticed because a person happened to look at a dashboard.

## Two CPU numbers, two conventions

This is the work of the package and its main source of error:

- **Host CPU** is a percentage of the **whole machine**. 100 means every core is busy.
- **Container CPU** is Docker's convention, where one fully busy core is 100. A 2-core host can report 200.

During the incident the host read 98.3% while the outside measurement read 195.8 of 200 — the same load, two
scales. `HostSample.ContainerCpuPercentOfMachine` divides the container sum by the core count before anything
is compared.

Getting this wrong fails **silently and in the dangerous direction**: an un-converted container sum is
over-counted, the unexplained gap comes out too small, and the alert never fires. A test on a 4-core host
pins exactly that case, because BurgCloud's own numbers do not expose it — 98 − 24 clears the threshold
either way.

## Driven by sample time, never the wall clock

Every threshold here advances on the timestamp inside the sample. That is what lets `HostLoadReplayTests`
push a whole week through the rules in under a second and still get findings dated 20 August, 14:17. A rule
that consulted `DateTime.UtcNow` could not be replayed — and a rule that cannot be replayed cannot be shown
to catch the incident it was written for.

The replay series is **reconstructed from the incident report's documented values, not recorded**. It proves
the rules fire; it cannot prove they stay quiet through a normal week, because it has none of the texture
that produces false alarms. That second question needs the real series and is still open — see
[Plan-0004](../../../../docs/plans/0004-host-und-baseline-alarme.md).
