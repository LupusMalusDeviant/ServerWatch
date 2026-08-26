# Documentation

| Document | What |
|---|---|
| [../README.md](../README.md) | Project overview, features, quick start, configuration, MCP, deployment |
| [product/README.md](product/README.md) | **Product strategy** — canonical positioning, the one-liner and pitch, target audience, the three product pillars, and the hero workflow ([product/POSITIONING.md](product/POSITIONING.md)) |
| [ARCHITECTURE.md](ARCHITECTURE.md) | The SSH-key-free design, mesh + mTLS, the three planes, PKI, onboarding |
| [container-hardening.md](container-hardening.md) | Full vs. locked-down container profiles, per-mode privilege matrix, socket-proxy, distroless roadmap |
| [prd/README.md](prd/README.md) | **Product Requirements Documents** — one per work package: problem, goals, requirements, acceptance criteria, and — project-specific — the measurement points and the failure modes of the solution itself |
| [plans/README.md](plans/README.md) | **Implementation plans** — one per PRD: work packages, order, dependencies, milestones with proof, rollback path, definition of done |
| [reviews/](reviews/) | Repo reviews and incident reports, including [the 2026-08-26 log-monitor incident](reviews/2026-08-26-logmonitor-dockerd-cpu-incident.md) that drives the SP work packages |
| [mcp-tool-catalog.md](mcp-tool-catalog.md) | **Generated** — every MCP tool this build serves, with its permission level and owning module. Regenerated from the code; `McpToolCatalogSnapshotTests` fails the build if it drifts. Do not edit by hand. |
| [../deploy/telemetry/README.md](../deploy/telemetry/README.md) | Mesh/mTLS telemetry deploy templates (node_exporter, VictoriaMetrics, Tailscale ACL) |

## Code documentation

Every source folder under [`../src/Whiskers/`](../src/Whiskers/) carries its own `README.md` describing the files within. Start points:

- [src/Whiskers/README.md](../src/Whiskers/README.md): application project layout
- [Services/README.md](../src/Whiskers/Services/README.md): a guided tour of every service
- [Mcp/README.md](../src/Whiskers/Mcp/README.md): the MCP server and its tools

## Conventions

- **Interface-first**: services live behind an `IFoo` interface; consumers depend on the interface.
- **English** in-code comments and XML docs throughout; the user-facing UI is localized (English default, German available) via `IStringLocalizer` / `.resx`.
